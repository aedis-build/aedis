# Aedis.App1

API REST em camadas (DDD) gerada pelo template **`aedis-api`**. Sobe segura por construção sobre o
`AedisApiHost` (health probes, security headers, rate limiting, ProblemDetails, validação 422 e portão de
autenticação fail-closed) e traz um CRUD de exemplo ponta a ponta — o agregado **Product** — com CQRS,
repositório PostgreSQL e HATEOAS.

## Estrutura

```
src/
├── Aedis.App1.Api/              Apresentação: AedisApiHost, Controllers, DTOs, Validators, Mappers, HATEOAS
├── Aedis.App1.Application/      Casos de uso (CQRS): Commands/Queries + Handlers, contrato do repositório
├── Aedis.App1.Domain/           Domínio: entidades (AuditableAggregateRoot)
└── Aedis.App1.Infrastructure/   Persistência: repositório (Aedis.Database.Postgres) + Criteria + migração
tests/
└── Aedis.App1.UnitTests/        Testes (NSubstitute + FluentAssertions): handler, link builder, criteria
```

A dependência aponta sempre para dentro: `Api → Application/Infrastructure → Domain`. Os pacotes Aedis são
referenciados nas bordas; o domínio depende apenas de `Aedis.Domain`.

## Endpoints (agregado Product)

| Verbo + rota | Ação | Sucesso |
|---|---|---|
| `POST /v1/products` | Cria | `201 Created` + `Location` |
| `GET /v1/products/{id}` | Lê por id | `200` (ou `404`) |
| `GET /v1/products?code=&name=&page=&pageSize=` | Lista paginada | `200` (coleção + links) |
| `PUT /v1/products/{id}` | Atualiza | `200` (ou `404`) |
| `DELETE /v1/products/{id}` | Remove (soft-delete) | `204` (ou `404`) |

Respostas em HAL: recurso único como `{ "data": {…}, "_links": {…} }`; coleção como
`{ "items": [...], "totalCount", "page", "pageSize", "_links": {…} }`. Código duplicado → `409`; validação de
entrada → `422`.

## Rodando

```bash
# 1) Banco: aplique a migração de referência
psql "$CONNECTION" -f src/Aedis.App1.Infrastructure/Persistence/Migrations/0001_create_product.sql

# 2) Configure Database:ConnectionString e Auth (Keycloak) em appsettings.json

# 3) Suba
dotnet run --project src/Aedis.App1.Api

# Testes
dotnet test
```

Os endpoints exigem autenticação (`[Authorize]`, fail-closed). Os links de hipermídia são declarados em
`ProductLinks` (resolvidos por *action*, sem URL na mão); para condicioná-los à permissão do usuário, injete
`ICurrentUser` e só declare o link quando o papel exigido estiver presente.

## Documentação OpenAPI (Scalar)

A documentação é um plugin (`Aedis.OpenApi` + `Aedis.OpenApi.Scalar`), ligado em `ApiHost.cs` só em Development:
`/openapi` abre a UI do Scalar e `/openapi/v1.json` serve o documento OpenAPI 3.1 (seção `OpenApi` do
`appsettings` para título, versão e prefixo). Actions `[HttpQuery]` saem sob `x-oai-additionalOperations`.

Além do contrato, cada operação publica extensões que portais e agentes leem em vez de reimplementar a API:

| Extensão | Origem | Conteúdo |
|---|---|---|
| `x-aedis-roles` | `[Authorize(Roles)]` e as roles das policies resolvidas | roles exigidas, ordenadas |
| `x-aedis-anonymous` | `[AllowAnonymous]` | `true` |
| `x-aedis-risk` | `[OperationRisk(RiskLevel, "motivo")]` | `{ "level": "HIGH", "reason": "..." }` — High/Critical exigem aprovação humana quando um agente executa |
| `x-aedis-rules` | validators FluentValidation do body/query | uma entrada por regra com a mensagem do 422 (`field`, `type`, `parameters`, `conditional`) |

## Imagem e deploy

```bash
docker build -t aedis.app1 .
```

O `Dockerfile` usa base *chiseled* non-root e restaura com `--locked-mode`: as versões vêm do `packages.lock.json`
de cada projeto, gerado no primeiro `dotnet restore` (`RestorePackagesWithLockFile`). Versione esses arquivos; o build
da imagem falha se um pacote mudar sem o lock acompanhar. O deploy é declarativo pelo [Aedis Platform](https://github.com/aedis-build/aedis-platform):
`deploy/app.yaml` são os values do chart `aedis-app` (baseline de pod, segredos via cofre, canário); copie-o para
`apps/<nome>/app.yaml` no repositório GitOps (`aedis platform new-app`). O `.github/workflows/aedis-ci.yml` roda o pipeline
único `aedis-ci` da plataforma (commits, cobertura, SAST, CodeQL, REUSE) e o `.github/workflows/release.yml`, em tag
`vX.Y.Z`, constrói a imagem assinada com SBOM e proveniência.

## Versão dos pacotes Aedis

A versão é parametrizável na geração e fica centralizada em `Directory.Packages.props`:

```bash
dotnet new aedis-api -n MinhaApi --AedisVersion 0.1.0-preview.1
```
