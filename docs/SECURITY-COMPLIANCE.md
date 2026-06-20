# Aedis — Matriz de Conformidade de Segurança

A camada de Hosting do Aedis é **secure-by-default**: ao herdar de `AedisApiHost`, a aplicação já sobe com os controles abaixo ligados, sem código adicional. Este documento rastreia cada controle aos frameworks de referência.

## Escopo e honestidade

Uma biblioteca entrega **controles técnicos de camada de aplicação**. Por isso:

- **OWASP Top 10** e **mitigações MITRE ATT&CK** têm cobertura quase completa no nível da aplicação.
- **NIST CSF 2.0** e **ISO/IEC 27001:2022** incluem controles **organizacionais** (políticas, gestão de risco, pessoas, processos do ISMS) que **nenhuma biblioteca cobre**. O Aedis atende ao **subconjunto técnico** — principalmente ISO 27001 **Annex A.8** (controles tecnológicos) e as funções **Protect/Detect** do CSF.
- Controles que dependem da operação (certificados TLS no ingress, rotação de segredos, hardening de SO/cluster, backups) permanecem **responsabilidade do operador** — listados em "Fora do escopo da biblioteca".

## Matriz de controles

| # | Controle Aedis | Onde | OWASP 2021 | NIST CSF 2.0 | ISO 27001:2022 A.8 | MITRE ATT&CK |
|---|---|---|---|---|---|---|
| 1 | Security headers (CSP, X-Frame-Options, nosniff, Referrer-Policy, Permissions-Policy, COOP/CORP) | `SecurityHeadersMiddleware` | A05 | PR.PS | A.8.9, A.8.23 | M1050; mitiga T1185, T1059.007 |
| 2 | HSTS + redirecionamento HTTPS | `HttpsOptions` | A02 | PR.DS-02 | A.8.24 | M1041; mitiga T1557, T1040 |
| 3 | Rate limiting por cliente (usuário/IP) | `RateLimitingOptions` | A04, A07 | PR.PS, DE.CM | A.8.6, A.8.20 | M1036; mitiga T1110, T1499 |
| 4 | Proteção de cabeçalho Host | `HostHeaderProtectionMiddleware` | A05 | PR.PS | A.8.20, A.8.23 | mitiga T1190 |
| 5 | Forwarded headers confiáveis (IP/esquema reais) | `ForwardedHeadersHardeningOptions` | A05 | PR.PS | A.8.20 | mitiga spoofing de origem (T1090) |
| 6 | Endurecimento do Kestrel (limites de corpo/cabeçalho, timeouts, sem header Server) | `KestrelHardeningOptions` | A05 | PR.PS | A.8.9 | M1037; mitiga T1499, T1592 |
| 7 | Erros seguros em ProblemDetails (sem vazar stack/mensagem interna) | `GlobalExceptionHandlingMiddleware`, `ExceptionToProblemDetailsMapper` | A05, A09 | PR.PS, DE.CM | A.8.8 | mitiga T1592, T1210 |
| 8 | Validação de entrada → 422 | `AddAedisApiValidation` (FluentValidation) | A03 | PR.PS | A.8.26, A.8.28 | mitiga T1190, T1059 |
| 9 | Portão de autenticação fail-closed (recusa subir inseguro fora de Development) | `AedisApiHost` | A01, A07 | PR.AA | A.8.5, A.8.26 | M1035; mitiga T1078 |
| 10 | Autorização deny-by-default (fallback exige usuário autenticado) | `AedisApiHost` | A01 | PR.AA-05 | A.8.3, A.8.5 | M1018, M1026; mitiga T1078 |
| 11 | Validação de JWT (issuer/audience/lifetime, claims preservados) | provider (ex.: `Aedis.Security.Keycloak`) | A07 | PR.AA-03 | A.8.5 | mitiga T1550.001 |
| 12 | Logging estruturado + telemetria OTLP + contexto de auditoria | `Aedis.Observability.*`, `IAuditContext` | A09 | DE.CM, DE.AE | A.8.15, A.8.16 | M1047; suporta detecção de T1110/T1078 |
| 13 | Shutdown gracioso (drena requisições, libera recursos) | `Aedis.Diagnostics` | A04 | PR.PS, RC.RP | A.8.14 | reduz perda/condição de corrida no encerramento |
| 14 | Swagger desligado por default (opt-in) | `AedisApiHost.EnableSwagger` | A05 | PR.PS | A.8.9 | mitiga T1592 (redução de superfície) |
| 15 | Build determinístico, CPM com versões fixas, override de transitivo vulnerável | `Directory.*.props` | A06, A08 | ID.RA, PR.PS | A.8.8, A.8.29, A.8.30 | mitiga T1195 (supply chain) |
| 16 | Compressão de resposta consciente de HTTPS | `AedisApiHost` (response compression) | — | — | — | trade-off documentado (BREACH) |
| 17 | Anti-força-bruta por credencial com bloqueio em 3 níveis configuráveis (chaveado pelo alvo, não pelo IP → imune a IP-rotation; distribuído via ICache) | `IBruteForceGuard`/`CacheBruteForceGuard` | A07 | PR.AA, DE.CM | A.8.5 | M1036; mitiga T1110 (incl. T1110.003 com rotação de IP) |
| 18 | Revogação de token (denylist distribuída imposta na validação do JWT): por `jti` (token vazado) ou por usuário `sub` (corte de sessão por `iat` — kill-all-sessions); guard de abuso que revoga ao detectar uso abusivo; e serviço/endpoint administrativo de revogação (SOC) | `ITokenDenylist`, `ITokenAbuseGuard`, `ITokenRevocation`, `MapAedisTokenRevocation` | A07, A01 | PR.AA-01, RS.MI | A.8.5 | M1018; mitiga T1528, T1550.001 (uso de token roubado) |
| 19 | Código gerenciado **memory-safe** (zero `unsafe`/`stackalloc`) — elimina estruturalmente a classe de buffer overflow; ASLR/DEP/NX/CFG/CET herdados do runtime/SO; hardening de AOT opt-in | C# + runtime .NET; `Aedis.Build` (props AOT); `docs/SECURITY-HARDENING.md` | A06 | PR.PS | A.8.28 | M1050; mitiga T1203 / corrupção de memória |
| 20 | **Proteção SSRF** no cliente HTTP de saída: recusa, no momento de conectar, IPs internos (loopback, privados, link-local/**metadata** `169.254.169.254`, ULA) — imune a DNS rebinding; allowlist/blocklist de host (opt-in) | `SsrfPolicy` (`Aedis.Http`); `docs/INJECTION-DEFENSE.md` | A10 | PR.PS, PR.AC | A.8.20, A.8.23 | mitiga T1552.005 (metadata), T1090 (proxy), T1046 (descoberta de rede) |

## Fora do escopo da biblioteca (responsabilidade do operador)

| Tema | Onde tratar |
|---|---|
| Terminação TLS, certificados e sua rotação | ingress/API gateway (ALB, APISIX, Kong, App Gateway) |
| Gestão e rotação de segredos | gerenciador de segredos (Secrets Manager, Key Vault, Vault) via env/CSI |
| Hardening de SO, container e cluster | imagem base, políticas do Kubernetes, admission controllers |
| WAF / proteção DDoS de borda | camada de rede/CDN |
| Brute force de **credencial** (login) e **revogação definitiva** de conta/sessão | **IdP (Keycloak)**: o app nunca vê a senha — o ataque de credencial bate na porta do IdP, que tem brute-force detection nativo + disable/lockout de conta + logout de sessão. O app cobre o abuso *autenticado* (token válido/vazado) via `IBruteForceGuard` + denylist local, sem credencial de IdP. |
| Endurecimento de **binário/processo** (ASLR/DEP/NX/CFG/CET) e de **imagem** (non-root, root FS read-only, drop de capabilities, `no-new-privileges`, seccomp) | **runtime .NET + SO** (automático no código gerenciado — `unsafe` ausente elimina buffer overflow) e **deploy/imagem**: baseline em `docs/SECURITY-HARDENING.md` + `docs/hardening/`. RELRO/canary só se aplicam em publish **NativeAOT** (props opt-in em `Aedis.Build`). |
| Gestão de risco, políticas, treinamento, resposta a incidentes (ISMS) | processo organizacional ISO 27001 |
| Backup e recuperação de dados | infraestrutura de dados |

## Como ajustar a postura

Todos os controles são ligados por default e configuráveis pela seção `Security` do `IConfiguration` (ver `WebSecurityOptions`). Em deploy atrás de um ingress que termina TLS, desligue o redirecionamento HTTPS local:

```json
{
  "Security": {
    "Https": { "EnableHttpsRedirection": false, "EnableHsts": false },
    "RateLimiting": { "PermitLimit": 300 }
  }
}
```

Desligar um controle é uma decisão explícita e auditável — o default permanece seguro.
