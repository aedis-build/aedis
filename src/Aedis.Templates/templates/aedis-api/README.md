# AedisApi1

API REST segura por construção, gerada com `dotnet new aedis-api`. Sobe sobre o `AedisApiHost`, então já
inclui: **health probes** (`/health`, `/health/live`, `/health/ready`), **security headers**, **rate
limiting**, **ProblemDetails** (RFC 9457), **validação 422** e **portão de autenticação fail-closed**.
Swagger é opt-in.

## Configurar

Ajuste a seção `Auth` no `appsettings.json` com o seu Keycloak (`Authority` e `Audience`). A autenticação
é exigida por default — o host recusa subir em produção sem ela.

## Rodar

```bash
dotnet run
# chame o endpoint (com um Bearer token válido do seu Keycloak):
curl -H "Authorization: Bearer <token>" http://localhost:8080/api/hello
# health (sem auth):
curl http://localhost:8080/health/ready
```

## Imagem e deploy

```bash
docker build -t aedisapi1 .
```

O `Dockerfile` usa base *chiseled* non-root; o `deploy/deployment.yaml` aplica o endurecimento de runtime
(root FS read-only, drop de capabilities, `no-new-privileges`, seccomp). Ver
`docs/SECURITY-HARDENING.md` do Aedis.

## Próximos passos

- Registre seus serviços em `ApiHost.ConfigureServices`.
- Adicione endpoints/controllers em `ApiHost.ConfigureMiddleware`.
- Para a stack completa (Postgres + Redis + mensageria + OTLP), referencie o meta-pacote `Aedis.Build`.
