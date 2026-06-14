# Migração: framework de origem → `Aedis.*`

> Como o monolito de origem (um único csproj, .NET 10, todas as infra juntas) vira a frota de pacotes `Aedis.*`.
> Para o *porquê* da topologia, veja [ARCHITECTURE.md](ARCHITECTURE.md).

## Ponto de partida (o que existe hoje)

O framework de origem é **um projeto** que referencia simultaneamente AWS SDK, Redis, RabbitMQ, IBM MQ, Npgsql, SqlClient, Hangfire, QuestPDF, MiniExcel, OpenTelemetry e Serilog. A boa notícia: **a costura já existe parcialmente** — `Messaging/` já tem subpastas `Contracts/`, `RabbitMq/`, `IbmMq/`, `AwsSqs/`; `Cache/` tem `Contracts/` + `Redis/`; `Database/` tem `Abstractions/`. Boa parte da decomposição é **promover subpasta a projeto**, não reescrever.

## Mapa de decomposição (pasta → pacote → tier → infra)

| Pasta atual | Pacote(s) Aedis | Tier | Dependências de infra |
|---|---|:--:|---|
| `Contracts/` (BasicResult, Errors, Success, Enums) · `Utils/` | `Aedis.Core` | 1 | — (`UUIDNext`) |
| `Exceptions/` (tipos) | `Aedis.Exceptions` | 1 | — |
| `Events/` (CloudEvents) | `Aedis.Events` | 1 | — |
| `Domain/` (AggregateRoot, Specification, Strategy, Chain, Saga) | `Aedis.Domain` | 1 | — |
| `Commands/` (CQRS) | `Aedis.Commands` | 1 | `AspectInjector` (decorators) |
| `Attributes/` | distribuído por capability + `Aedis.Core` (interceptação base) | 1/3 | `AspectInjector` |
| `Cache/Contracts` | `Aedis.Cache.Abstractions` | 2 | — |
| `Cache/Redis` + `*Service`, `Builder`, `Batch*` | `Aedis.Cache.Redis` | 3 | `StackExchange.Redis`, `MessagePack` |
| `Messaging/Contracts`, `IMessage*`, `MessageBase` | `Aedis.Messaging.Abstractions` | 2 | — |
| `Messaging/RabbitMq` | `Aedis.Messaging.RabbitMq` | 3 | `RabbitMQ.Client` |
| `Messaging/IbmMq` | `Aedis.Messaging.IbmMq` | 3 | `IBMMQDotnetClient` (+ override `Newtonsoft.Json`) |
| `Messaging/AwsSqs` | `Aedis.Messaging.AwsSqs` | 3 | `AWSSDK.SQS`, `AWSSDK.SimpleNotificationService`, `AWSSDK.Extensions.NETCore.Setup` |
| `Database/Abstractions` | `Aedis.Database.Abstractions` | 2 | — (`Dapper` no contrato mínimo) |
| `Database/{Repositories,Strategies,Infrastructure,TypeHandlers,Queries}` (Postgres) | `Aedis.Database.Postgres` | 3 | `Npgsql`, `Dapper` |
| `Database/…` (SqlServer dialect) | `Aedis.Database.SqlServer` | 3 | `Microsoft.Data.SqlClient`, `Dapper` |
| `Storage/IBucket`, `BucketObject`, `StreamContext` | `Aedis.Storage.Abstractions` | 2 | — |
| `Storage/Strategies` (S3) | `Aedis.Storage.S3` | 3 | `AWSSDK.S3`, `AWSSDK.SecurityToken` |
| `Logging/` (Serilog) | `Aedis.Observability.Abstractions` + `Aedis.Observability.Serilog` | 2/3 | `Serilog.*` |
| `Metrics/` (OTel/OTLP) | `Aedis.Observability.Otlp` | 3 | `OpenTelemetry.*` |
| `HealthChecks/` | `Aedis.Diagnostics` (zero-config) | 3 | `Microsoft.*.HealthChecks` |
| `Hosting/` (WebApiHost, Controllers, Hateoas, Middleware, Filters, GracefulShutdown) | `Aedis.Hosting.Abstractions` + `Aedis.Hosting.AspNetCore` | 2/3 | `Swashbuckle`, `Microsoft.OpenApi`, `HttpOverrides`, `ResponseCompression`, `FluentValidation.AspNetCore` |
| `Hosting/Authentication` (JWT) | `Aedis.Security.Abstractions` + `Aedis.Security.Keycloak` | 2/3 | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `StandaloneApp` + `HostedServices` | `Aedis.Hosting.Worker` | 3 | `Microsoft.Extensions.Hosting` |
| `HostedServices/Hangfire` + agendamento | `Aedis.Scheduling.Hangfire` | 3 | `Hangfire.AspNetCore`, `Hangfire.PostgreSql`, `Cronos` |
| `Excel/` | `Aedis.Excel.Abstractions` + `Aedis.Excel.MiniExcel` | 2/3 | `MiniExcel` |
| `Pdf/` | `Aedis.Pdf.Abstractions` + `Aedis.Pdf.QuestPdf` | 2/3 | `QuestPDF`, `SkiaSharp`, `ZXing.Net` |
| HTTP client (`Flurl.Http`) | `Aedis.Http` (cliente outbound) | 2/3 | `Flurl.Http` |
| `Extensions/` | dividido — cada extensão vai junto da sua capability | — | — |

> **Lacunas viram pacotes novos** (demanda comprovada pelo consumidor, §3/§8 do diagnóstico): `Aedis.Security.Abstractions` (`ICurrentUser`/`IAuditContext`) e `Aedis.Security.Secrets` (Vault/AWS SM).

## Estratégia: estrangulamento incremental

A migração é **faseada e reversível** — nunca um *big-bang*. Casada com F0–F2 do roteiro de GA:

| Passo | Ação | Risco | Trava de qualidade |
|--:|---|:--:|---|
| **0** | Monorepo `aedis` + `Directory.Packages.props` (CPM) + CI matriz multi-pacote + feed; publica **stub assinado** | baixo | pipeline publica pacote assinado |
| **1** | Extrair **Tier 1** (Core/Exceptions/Events/Domain/Commands) | baixo | compila isolado, zero dep de infra |
| **2** | Extrair **Tier 2** — promover as pastas `Contracts/`/`Abstractions/` | baixo | `ApiCompat` trava o contrato |
| **3** | Mover **Tier 3** provider-a-provider (Messaging já está separado) | médio | paridade 1:1 pela suíte de testes existente |
| **4** | Aplicar o split **contrato↔glue** (ProblemDetails, health registration, FluentValidation) | médio | analyzers + testes de arquitetura |
| **5** | Ligar `Aedis.Build` (meta) + `Aedis.Templates` + `Aedis.Analyzers` | baixo | `dotnet new` sobe serviço com probes/segurança |
| **6** | O pacote de origem vira **shim** (só referencia os `Aedis.*`) | baixo | consumidores migram serviço-a-serviço |

### Ordem importa
Tier 1 primeiro porque **não tem dependência de infra** — valida a base sem arrastar AWS/Redis/etc. Implementações por último, uma de cada vez, com a suíte de testes atual como *gate* de paridade.

## O shim de transição (zero big-bang)

O pacote de origem não é deletado de imediato: vira um **meta-pacote que só referencia os pacotes Aedis equivalentes**. Assim cada serviço consumidor migra no seu ritmo:

```
Pacote de origem (shim) ──► Aedis.Core + Aedis.Commands + Aedis.Database.Postgres + …
```

Quando o último consumidor estiver nos pacotes `Aedis.*` diretamente, o shim é descontinuado.

## Trilhos que impedem regressão

- **`Aedis.Analyzers`** (Roslyn) — domínio referenciando implementação = **erro de build**. A regra de dependência vira lei executável.
- **`Microsoft.DotNet.ApiCompat`** no CI — quebra de contrato entre Core e implementação = build vermelho.
- **`NetArchTest`** nos testes — conformidade arquitetural verificada por serviço.
- **Suíte de testes existente** do framework de origem — reaproveitada como *gate* de paridade funcional pós-extração.

## Migração do consumidor de referência (dogfooding)

O **consumidor de referência** (privado) valida a v1 e comprova a regra de dependência na prática:

| Passo | Ação | Risco |
|--:|---|:--:|
| 1 | `Domain.csproj`: framework de origem → `Aedis.Core` + `Aedis.*.Abstractions` | baixo |
| 2 | `Host`/`Worker`: adicionar `Aedis.Database.Postgres`, `Aedis.Messaging.AwsSqs`, `Aedis.Cache.Redis`, `Aedis.Hosting.AspNetCore`/`.Worker` | médio |
| 3 | Substituir health checks manuais pelo auto-registro (`Aedis.Diagnostics`) — fecha *gap #1* | baixo |
| 4 | Adotar `IAuditContext` — remover *threading* manual de `CreatedBy/UpdatedBy` — fecha *gap #3* | baixo |
| 5 | Avaliar generalizar o *guard* de multi-tenancy custom → *tenant-isolation capability* | backlog |
| 6 | Suíte de integração + smoke em staging; *canary* em produção | médio |

Os exemplos **públicos** ficam em `samples/` (quickstarts API + Worker, neutros) — sem expor o consumidor proprietário.

## Riscos principais

| Risco | Mitigação |
|---|---|
| Explosão de pacotes/versões | CPM + *lockstep* de Core + meta-pacote `Aedis.Build` |
| Quebra de contrato silenciosa | `ApiCompat` no CI + testes de contrato compartilhados |
| *Blast radius* de update na frota | Semver disciplinado + shim de transição + canary por serviço |
| Paridade incompleta pós-extração | Suíte de testes existente como *gate* (exit criteria do passo 3) |
