# Upsert portável — estado atual e roadmap

Documento de design do upsert agnóstico de provider do Aedis. Registra **o que existe hoje**
(modelo B, implementado) e **o modelo A** (AST de expressão genérica), deliberadamente adiado até
surgir um caso real que o B não cubra.

## Princípio

A intenção de upsert é declarada **uma vez**, de forma neutra, na camada de contratos
(`Aedis.Database.Abstractions`), e cada provider a **compila para o seu dialeto**. O mesmo
`GetUpsertSpec()` roda no Postgres e no SQL Server sem refatoração ao trocar o provider:

- Postgres → `INSERT … ON CONFLICT (…) DO UPDATE SET … WHERE <guarda>`
- SQL Server → `MERGE … WHEN MATCHED AND <guarda> THEN UPDATE SET …` (com `HOLDLOCK`)

Save (single-row) e bulk compartilham a mesma compilação, garantindo comportamento idêntico nos
dois caminhos.

---

## Modelo B — IMPLEMENTADO (commit `8f4c0ee`)

DSL de política **restrita** que cobre o caso dominante de upsert.

### Superfície

`UpsertSpec` (`src/Aedis.Database.Abstractions/UpsertSpec.cs`):

- `OnKey(params string[])` — propriedades-chave de conflito (resolvidas para colunas pela convenção).
- `Preserve(params string[])` — colunas inseridas mas **nunca** sobrescritas na atualização (ex. `CreatedAt`).
- `SetServerUtcNow(string)` — coluna que recebe o "agora" do servidor (`now()` ↔ `SYSUTCDATETIME()`).
- `DoNothingOnConflict()` — preserva a linha em colisão.
- `When(g => …)` — guarda condicional restrita:
  - frescor combinado por **OR**: `Newer(col)` (`>=`), `OrGreater(col)` (`>`), null-aware;
  - booleanos combinados por **AND**: `AndExistingFalse/True(col)`, `AndNotDeleted()`.
  - guarda final = `(OR frescor) AND (AND booleanos)`.

### Compiladores (internos, um por provider)

- `PostgresUpsertCompiler` (`src/Aedis.Database.Postgres/`) → sufixo `ON CONFLICT … DO UPDATE … WHERE`.
- `SqlServerUpsertCompiler` (`src/Aedis.Database.SqlServer/`) → corpo do `MERGE` (`WHEN MATCHED …`).

Encapsulam as diferenças de dialeto: `EXCLUDED`/tabela vs `s.`/`t.`, booleano `false` vs `0`,
`now()` vs `SYSUTCDATETIME()`, e frescor **null-aware** (`col IS NULL OR s.col >= t.col`) — sem
sentinela de `infinity`.

### Hook

`protected virtual UpsertSpec? GetUpsertSpec()` nos dois repositórios. No Postgres tem precedência
sobre o `GetOnConflictClause()` literal (mantido como **escape hatch PG-only** para guards arbitrários).

### Exemplo (idêntico nos dois providers)

```csharp
protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id")
    .Preserve("CreatedAt")
    .SetServerUtcNow("UpdatedAt")
    .When(g => g.Newer("ObservedAt").OrGreater("SourceSeq").AndNotDeleted());
```

Validado contra os dois bancos reais (Postgres 24/24; SQL Server 23/23 via `AEDIS_SQLSERVER_IT=1`):
"só atualiza se mais novo por `observed_at` OU `source_seq`, preserva `created_at`, ignora
soft-deleted" se comporta igual no `ON CONFLICT` e no `MERGE`.

### Limite do B

A guarda só expressa frescor (coluna×coluna) e booleano (coluna×constante de compilação). **Não**
expressa literais de valor, `NOT`, AND/OR aninhados arbitrários, `IS NULL` em operando qualquer, nem
funções. Para isso → modelo A.

---

## Modelo A — ADIADO (AST de expressão genérica)

Troca a guarda fechada do B por uma **árvore de expressão** percorrida por um **visitor por
provider**. O resto da `UpsertSpec` (chave, `Preserve`, `SetServerUtcNow`, SET) não muda.

### O que seria criado

Em `Aedis.Database.Abstractions` (namespace novo, ex. `…Upserts.Expressions`):

**Nós (operandos + predicados):**

```csharp
public abstract record UpsertNode;

// operandos
public sealed record IncomingColumn(string Property) : UpsertNode;   // EXCLUDED / s.
public sealed record ExistingColumn(string Property) : UpsertNode;   // tabela / t.
public sealed record Literal(object? Value)          : UpsertNode;   // vira bind parameter
public sealed record ServerCall(UpsertFunction Fn, IReadOnlyList<UpsertNode> Args) : UpsertNode;

// predicados
public sealed record Comparison(UpsertNode Left, ComparisonOp Op, UpsertNode Right) : UpsertNode;
public sealed record NullCheck(UpsertNode Operand, bool Negated) : UpsertNode;
public sealed record Logical(LogicalOp Op, IReadOnlyList<UpsertNode> Children) : UpsertNode; // And/Or/Not
```

+ enums `ComparisonOp` (`= <> < <= > >=`), `LogicalOp` (`And/Or/Not`), `UpsertFunction`
(whitelist pequena: `UtcNow`, `Coalesce`).

**Builder fluente** (para não montar nós na mão):

```csharp
.When(e => e.Or(
        e.Incoming("ObservedAt").GteOrExistingNull(),     // (t.x IS NULL OR s.x >= t.x)
        e.Incoming("SourceSeq").Gt(e.Existing("SourceSeq")))
    .And(e.Existing("IsDeleted").IsFalse())
    .And(e.Existing("Status").NotEq("LOCKED")));          // ← literal: o que B não faz
```

**Visitor por provider** (substitui o `BuildGuard` de cada compilador): `PostgresPredicateWriter`
e `SqlServerPredicateWriter`, cada um `string Visit(UpsertNode, ParameterBag)`. É aqui que mora o
dialeto:

| nó                       | Postgres         | SQL Server         |
|--------------------------|------------------|--------------------|
| `IncomingColumn(p)`      | `EXCLUDED.col`   | `s.[col]`          |
| `ExistingColumn(p)`      | `tabela.col`     | `t.[col]`          |
| `Literal(v)`             | `@g0` (bind)     | `@g0` (bind)       |
| `ServerCall(UtcNow)`     | `now()`          | `SYSUTCDATETIME()` |
| `Comparison/Logical/NullCheck` | composição textual | idem com `[ ]` |

### Como funciona ponta a ponta

1. `GetUpsertSpec().When(...)` passa a aceitar um **predicado raiz** (`UpsertNode` booleano).
2. No compilador, além do SET (igual ao B), o visitor percorre a árvore → **string SQL + saco de
   parâmetros** (`@g0, @g1…`).
3. Postgres injeta em `… DO UPDATE SET … WHERE <sql>`; SQL Server em `… WHEN MATCHED AND <sql> …`.
4. Os parâmetros da guarda são **constantes por lote** (não variam por linha) → entram uma vez no
   `ExecuteAsync` do MERGE/INSERT.

### Compatibilidade com o B

O `UpsertGuardBuilder` atual **não some** — vira açúcar que emite nós AST (lowering):

- `Newer("ObservedAt")` → `Or(NullCheck(Existing, negated:false), Comparison(Incoming, >=, Existing))`
- `AndNotDeleted()` → `Comparison(Existing("IsDeleted"), =, Literal(false))`

Os overrides e testes de hoje continuam idênticos; só o motor por baixo vira o visitor genérico.
A transição é "lowering", não rescrita do consumidor.

### O que destrava

Comparar com **literais** (`Status != "LOCKED"`), `NOT`, AND/OR aninhados arbitrários, `IS NULL`
em qualquer operando, e funções da whitelist (`COALESCE`).

### O que continua barrado de propósito

Tudo fora da whitelist. Cada visitor **rejeita** (`NotSupportedException` nomeando o nó + provider)
o que não sabe traduzir (ex. `ServerCall` PG-only, array/JSON). Falha no build do SQL, nunca gera
SQL errado em silêncio. Disciplina: cobrir **predicados de guarda**, não virar uma linguagem SQL.

### Custos / riscos (a partir de hoje)

- **Injeção**: literais **têm** que virar bind parameter (nunca interpolados) — mesma regra do
  `SqlIdentifier`/`RawCriteria`. Hoje a guarda não tem nenhum parâmetro; o A introduz o threading de
  `@gN` no Save **e** no comando MERGE do bulk.
- **Colisão de nomes**: `@gN` da guarda não pode bater com `@Id/@Name` da entidade nem com a staging
  — precisa de namespacing (prefixo `g`).
- **Matriz de teste**: paridade por tipo de nó nos dois bancos (comparações, NULL, NOT, COALESCE).
- **Esforço estimado**: ~1 sessão e meia (nós + 2 visitors + builder + lowering do builder atual +
  threading de parâmetros + testes de paridade por nó).

### Gatilho para implementar

Aparecer um guard real que o B não cobra — literal de valor, `NOT`, função, ou aninhamento
arbitrário na guarda de upsert. Até lá, o escape hatch `GetOnConflictClause()` (PG-only) resolve
casos pontuais sem portabilidade. Quando o A for feito, a transição é suave porque o builder do B
já vira açúcar do AST.
