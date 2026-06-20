# Templates do Aedis

Templates `dotnet new` que sobem um serviço com **probes + segurança** por default.

| Template | `dotnet new` | Gera |
|---|---|---|
| **aedis-api** | `dotnet new aedis-api -n MinhaApi` | API REST sobre `AedisApiHost` (health probes, security headers, rate limiting, ProblemDetails, validação 422, auth fail-closed; Swagger opt-in) + Dockerfile/k8s endurecidos |
| **aedis-worker** | `dotnet new aedis-worker -n MeuWorker` | Worker headless sobre `AedisWorkerHost` (observabilidade, health, shutdown gracioso) + Dockerfile/k8s endurecidos |
| **aedis-lib** | `dotnet new aedis-lib -n MinhaLib` | Biblioteca com as convenções do Aedis (net10, nullable, doc XML) |

## Instalar e usar

```bash
dotnet new install Aedis.Templates
dotnet new aedis-api -n MinhaApi
```

Cada projeto gerado traz seu próprio `README.md` com os próximos passos.
