# Aedis — Padrão de Código e Documentação

Convenção **default** da plataforma. Todo código novo (e revisado) segue estas regras. Vale para `src/` e `tests/`.

## 1. Documentação XML (`///`) didática

**Toda** a superfície pública é documentada — sem exceção (alvo: `CS1591 = 0`). Cada **tipo público** e **membro público** tem um `<summary>` que ensina, não que repete o nome:

- **O quê** faz, **quando/como** usar e, em uma frase, **como** funciona.
- Mesmo membros fluentes/óbvios (ex.: `WhereEquals`, `OrderBy`) recebem uma linha **útil** — descreva o que a chamada faz/adiciona, nunca um eco do nome.
- Use `<param>`, `<returns>`, `<typeparam>` quando agregam informação (não para repetir o tipo).
- Use `<see cref="..."/>` para ligar a tipos/membros relacionados, `<example>` para padrões de uso, `<remarks>` para nuances (performance, thread-safety, ciclo de vida, pegadinhas).
- Onde a doc já vem de uma interface/base, use `<inheritdoc />` em vez de duplicar.
- Tipos/membros **internos e privados** com lógica não óbvia também merecem `<summary>`; trivialidades, não.

Bom (didático): explica o porquê e o uso.
```csharp
/// <summary>
///     Sessão transacional sobre uma conexão Npgsql, com acesso via Dapper. Expõe a conexão subjacente
///     (<see cref="GetConnection" />) para o caminho de alta performance do COPY. No descarte sem
///     commit/rollback, faz rollback automático.
/// </summary>
```

Ruim (repete o nome):
```csharp
/// <summary>Classe UnitOfWork.</summary>
```

## 2. Sem comentários inline no corpo do código

**Não** use `//` nem `/* */` dentro de métodos. Código profissional se explica por:

- **Nomes claros** (variáveis, métodos, tipos) no lugar de um comentário.
- **Extração de método** com nome que descreve a intenção.
- Quando o *porquê* é essencial (ex.: workaround, decisão de performance, invariante sutil), ele vai no **`<summary>`/`<remarks>`** do membro — não em uma linha solta.

Exceções permitidas: diretivas do compilador/ferramenta (`#pragma`, `// TODO:` rastreável, atributos de gerador). XML em arquivos de projeto (`<!-- -->`) não é comentário inline de código.

## 3. Testes

- **Classe de teste**: `<summary>` descrevendo o comportamento verificado (o contrato sob teste), de forma didática.
- **Métodos de teste**: o **nome** é a documentação (`Faz_X_quando_Y`); não precisam de XML.
- Sem comentários inline — o arranjo *arrange/act/assert* e nomes claros bastam.

## 4. Idioma e voz

Português, presente, conciso e profissional. Consistente com o restante da base.

## 5. Enforcement

- `GenerateDocumentationFile = true` em todos os projetos (`Directory.Build.props`).
- `CS1591` (membro público sem doc) **não é suprimido** — aparece como aviso, guiando a cobertura.
- A regra "sem comentários inline" é verificada em code review (não há analisador que a imponha).
