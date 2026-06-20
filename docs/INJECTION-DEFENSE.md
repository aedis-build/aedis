# Aedis — Defesa contra Injeção e Ataques Web

Como o Aedis trata cada classe de ataque de injeção/web — e por que **não** existe um "middleware que
sanitiza tudo".

## Princípio: defesa no *sink*, não sanitização de string

Não há (nem deve haver) um sanitizador genérico de `string`/`char`/`byte[]` por default. A defesa correta
depende do **destino (sink)** do dado, e um middleware não sabe para onde a string vai:

- **SQL** → parametrização.
- **HTML** → encoding de saída (contextual).
- **URL/saída HTTP** → validação de destino (SSRF).
- **Shell/LDAP** → escaping específico.

Um "sanitiza tudo" **corromperia dado legítimo** (`O'Brien`, `<`, senhas, e principalmente `byte[]` de
upload/imagem/blob) e **não** preveniria o problema no sink. O controle de **entrada** correto é
**validação que rejeita** (FluentValidation → 422), nunca mutação silenciosa. (Ver `RawCriteria`: *"a
segurança vem da parametrização, não de sanitização"*.)

## Como cada ataque é tratado

| Ataque | Onde se resolve | Como no Aedis | Status |
|---|---|---|---|
| **SQL Injection** | DB (sink) | Parametrização (Dapper `DynamicParameters`); `SqlIdentifier.Validate` (allowlist) para identificadores não parametrizáveis; `RawCriteria` também parametrizada | ✅ estrutural |
| **Clickjacking** | resposta HTTP | `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'` (`SecurityHeadersMiddleware`) | ✅ default |
| **XSS / HTML Injection** | saída HTML (app) | API JSON: `application/json` + `nosniff` + CSP `default-src 'none'` → não renderiza como HTML. Se o app emitir HTML, encoding na view (Razor auto-encoda) | ✅ N/A p/ API · app p/ HTML |
| **SSRF** | transporte HTTP de saída | **SSRF guard** no `Aedis.Http`: recusa, no momento de conectar, IPs internos (loopback, privados, link-local/metadata `169.254.169.254`, ULA), imune a DNS rebinding; allowlist/blocklist de host | ✅ opt-in |
| **Prompt Injection** | camada de IA (app) | Sem camada de IA/LLM no Aedis → fora de escopo. Com LLM: guardrails de input/output, separar instrução de dado | — fora de escopo |

## Habilitando o SSRF guard

É opt-in no perfil de transporte; ao ligar, bloqueia toda conexão a endereço interno:

```csharp
var profile = new HttpClientProfile { Ssrf = { Enabled = true } };
profile.Ssrf.AllowedHosts.Add("interno.svc.cluster.local"); // serviço interno legítimo (opcional)

// use o perfil ao criar o cliente / no OAuthTokenOptions.Transport
options.Transport = profile;
```

Recusas lançam `SsrfBlockedException` (fail-fast — o framework não engole).

## MITRE ATT&CK — cobertura validada

Técnicas aplicáveis a um framework de API/worker e como o Aedis as trata (detalhe e mapeamento de
controles em `docs/SECURITY-COMPLIANCE.md`):

| Técnica | Tratamento no Aedis |
|---|---|
| **T1190** Exploit Public-Facing Application | validação 422, proteção de Host, security headers, erros seguros, limites do Kestrel |
| **T1110 / T1110.003** Brute Force / Password Spraying | `IBruteForceGuard` chaveado pelo alvo (3 níveis), imune a IP-rotation |
| **T1078** Valid Accounts | auth fail-closed + authz deny-by-default |
| **T1528 / T1550.001** Steal / Use Application Access Token | denylist por `jti`/`sub`, abuse guard, revogação administrativa |
| **T1552.005** Cloud Instance Metadata API | **SSRF guard** bloqueia `169.254.169.254` (impede roubo de credencial de instância) |
| **T1090** Proxy · **T1046** Network Service Discovery | **SSRF guard** impede usar a app como pivô/scanner da rede interna |
| **T1557 / T1040** AiTM / Network Sniffing | HSTS + HTTPS-redirect |
| **T1185** Browser Session Hijacking | security headers (CSP, X-Frame-Options) |
| **T1499** Endpoint DoS | rate limiting + endurecimento do Kestrel |
| **T1059** Command and Scripting Interpreter (injeção) | SQL parametrizado, sem shell-out, validação de entrada |
| **T1203** Exploitation for Client Execution / corrupção de memória | código gerenciado memory-safe (0 `unsafe`) + hardening AOT |
| **T1195** Supply Chain Compromise | build determinístico, CPM com versões fixas, override de transitivo vulnerável |
| **T1592** Gather Victim Host Information | sem header `Server`, Swagger off por default, erros genéricos |

**Fora do escopo do framework (responsabilidade do IdP/operador/imagem):** brute force de **credencial** e
revogação definitiva (Keycloak); hardening de binário/processo/imagem (runtime/SO/container — ver
`docs/SECURITY-HARDENING.md`); **prompt injection** (MITRE **ATLAS AML.T0051**) — depende da camada de IA
do app, não do Aedis.
