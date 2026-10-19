# Multi-tenancy

> Como um serviço Aedis atende várias empresas clientes (tenants) sem que uma enxergue os dados da outra — no HTTP, no worker e no banco.

## Peças

| Pacote | Tier | O quê |
|---|---|---|
| `Aedis.Tenancy.Abstractions` | 2 | `ICurrentTenant` (leitura, `RequiredId` fail-closed), `ITenantContext` (`BeginScope`), `ITenantResolver`, `TenantRequiredException` (→ 403) |
| `Aedis.Tenancy` | 3 | `AmbientTenantContext` (AsyncLocal, singleton), `ClaimTenantResolver`, `AddAedisTenancy` |
| `Aedis.Security.Keycloak` | 3 | `KeycloakOrganizationTenantResolver`, `AddAedisKeycloakOrganizationTenancy` |
| `Aedis.Hosting.AspNetCore` | 3 | `UseAedisTenancy` + `EnableTenancy` no `AedisApiHost` |
| `Aedis.Hosting.Worker` | 3 | `EnableTenancy` no `AedisWorkerHost` |
| `Aedis.Database.Postgres` | 3 | sessões gravam o tenant em `aedis.tenant_id` (base para RLS) |

Domínio e aplicação dependem só de `Aedis.Tenancy.Abstractions`.

## Regra fail-closed

Sem tenant, `ICurrentTenant.Id` é `null` e `ICurrentTenant.RequiredId` lança `TenantRequiredException`. Nunca há um valor "todos os tenants". No HTTP a exceção vira **403 ProblemDetails**; no worker indica que o trabalho não abriu escopo. No banco, a política RLS abaixo **lança erro** (`42501`) quando a sessão não tem tenant — em vez de devolver vazio ou tudo.

## HTTP

```csharp
public sealed class ApiHost : AedisApiHost
{
    protected override bool EnableTenancy => true;   // AddAedisTenancy + UseAedisTenancy após a autorização

    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) {
        services.AddAedisKeycloakAuth(configuration);
        services.AddAedisKeycloakOrganizationTenancy();   // opcional: tenant = organização do Keycloak
    }
}
```

O middleware chama o `ITenantResolver` uma vez por requisição (já autenticada) e abre o escopo para o resto do pipeline. Origens do tenant, da mais simples à mais específica:

| Origem | Como |
|---|---|
| Claim do token | padrão; `Tenancy:ClaimType` (default `tenant_id`) |
| Keycloak Organizations | `AddAedisKeycloakOrganizationTenancy()`; alias por padrão, `Auth:TenantFromOrganizationId=true` para o id |
| Cadastro da aplicação | registre o seu `ITenantResolver` (scoped, pode consultar repositório) |

**Keycloak Organizations:** o token precisa do escopo `organization` (claim `organization`). Membro de nenhuma ou de várias organizações → sem tenant; o cliente escolhe uma pedindo o escopo `organization:<alias>`. O resolvedor fica em `Aedis.Security.Keycloak` porque o formato da claim é do Keycloak, e esse pacote já é o adaptador do token Keycloak.

## Worker, consumer, job

Fora do HTTP não há requisição de onde resolver: o tenant viaja **no trabalho** (mensagem, item da mailbox, parâmetro do job) e quem processa abre o escopo.

```csharp
public sealed class WorkerHost : AedisWorkerHost
{
    protected override bool EnableTenancy => true;
}

public sealed class Processador(ITenantContext tenant, IUnitOfWorkFactory db)
{
    public async Task ProcessarAsync(Tarefa tarefa, CancellationToken ct) {
        using (tenant.BeginScope(tarefa.TenantId)) {
            await using var uow = await db.CreateWriteSessionAsync(ct);   // já nasce com o tenant
            // ...
        }
    }
}
```

O escopo segue o fluxo assíncrono (`await`, `Task.Run`) e é isolado entre mensagens concorrentes. Escopos aninham e restauram o anterior no descarte.

## Dados

### Filtro explícito

Em qualquer SQL escopado, use `RequiredId` — nunca `Id`:

```csharp
uow.QueryAsync<Pedido>("SELECT * FROM pedidos WHERE tenant_id = @tenant AND estado = @estado",
    new { tenant = currentTenant.RequiredId, estado });
```

### PostgreSQL Row-Level Security (recomendado)

Com tenancy registrada, toda sessão da `UnitOfWorkFactory` aberta dentro de um escopo executa `set_config('aedis.tenant_id', <tenant>, true)` — local à transação, então não vaza pelo pool. Com a política abaixo, o isolamento vale para **todo** SQL, inclusive o escrito à mão e o que esquece o filtro.

```sql
CREATE OR REPLACE FUNCTION aedis_current_tenant() RETURNS text
LANGUAGE plpgsql STABLE AS $$
DECLARE tenant text := nullif(current_setting('aedis.tenant_id', true), '');
BEGIN
    IF tenant IS NULL THEN
        RAISE EXCEPTION 'aedis: acesso a dado escopado sem tenant' USING ERRCODE = '42501';
    END IF;
    RETURN tenant;
END $$;

ALTER TABLE pedidos ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON pedidos
    USING (tenant_id = (SELECT aedis_current_tenant()))
    WITH CHECK (tenant_id = (SELECT aedis_current_tenant()));
```

Cuidados:

- O papel da aplicação **não** pode ser superusuário, dono da tabela nem ter `BYPASSRLS` — esses ignoram a política. Use um papel próprio com `GRANT` nas tabelas, ou `ALTER TABLE … FORCE ROW LEVEL SECURITY` se o dono for o mesmo papel.
- `(SELECT aedis_current_tenant())` avalia a função uma vez por consulta, não por linha.
- O tenant vale **na transação** da sessão. Depois de `CommitAsync`/`RollbackAsync`, consultas na mesma sessão rodam sem tenant e falham fechado: abra outra sessão.
- Tabelas globais (cadastro de tenants, catálogos) ficam sem RLS. A resolução do tenant que consulta o banco roda antes do escopo e, portanto, só pode ler tabelas globais.
- Coluna `uuid`: compare com `(SELECT aedis_current_tenant())::uuid`.

O teste `TenancyRowLevelSecurityTests` (Testcontainers) executa exatamente este SQL e prova: leitura e escrita cruzadas impossíveis, falha sem tenant, nada vaza pelo pool nem entre fluxos concorrentes.
