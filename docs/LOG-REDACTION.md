# Ofuscação de logs (PII e segredos)

O pipeline de logging do Aedis (`Aedis.Observability.Serilog`) ofusca dados sensíveis **antes** de qualquer
sink (Console **e** OTLP) — **ligado por default**, secure-by-default. Cobre OWASP A09 (Security Logging
Failures) e o requisito LGPD/GDPR de não trafegar dado pessoal em claro nos logs.

## Como funciona

Um `RedactionEnricher` percorre recursivamente as propriedades de cada evento (objetos, dicionários, listas)
e ofusca aquelas cujo **nome** é sensível. Há dois grupos:

| Grupo | Estratégia default | Exemplos de nome |
|---|---|---|
| **Segredos** | máscara total (`***`) | `authorization`, `password`, `token`, `access_token`, `client_secret`, `apikey`, `connectionstring`, `cookie`, `private_key` |
| **PII** | parcial (últimos 4: `***6789`) | `cpf`, `cnpj`, `rg`, `email`, `telefone`, `creditcard`, `cardnumber`, `cvv` |

A comparação é por nome normalizado (minúsculas, sem separadores), então `AccessToken`, `access_token` e
`X-Api-Key` são reconhecidos igualmente. Funciona em qualquer profundidade:

```csharp
log.Information("token {Token}", jwt);                 // Token  -> "***"
log.Information("doc {Cpf}", "12345678901");           // Cpf    -> "***8901"
log.Information("req {@Payload}", payload);             // Payload.Password -> "***", Payload.Cpf -> "***8901"
log.Information("headers {@Headers}", headers);         // chave "Authorization" do dicionário -> "***"
```

> **Limitação honesta:** ofusca propriedades **estruturadas**, não segredo embutido em string interpolada
> (`$"token={t}"`). Logue de forma estruturada (`{Campo}`/`{@Objeto}`).

## Campos ambíguos: `[SensitiveData]`

Nomes como `Name` ou `Address` não entram nos defaults (seriam falso-positivo — um produto pode se chamar
`Name`). Para esses, marque explicitamente:

```csharp
public sealed class Customer {
    [SensitiveData]                       // usa a estratégia de PII (parcial)
    public string FullName { get; set; }
    [SensitiveData(RedactionStrategy.Mask)] // força máscara total
    public string Address { get; set; }
    public string City { get; set; }       // intacto
}
```

## Configuração (`Logging:Redaction`)

Tudo é ajustável; os defaults já são seguros.

```jsonc
{
  "Logging": {
    "Redaction": {
      "Enabled": true,
      "Placeholder": "***",
      "KeepLast": 4,
      "SecretStrategy": "Mask",      // Mask | Partial | Hash
      "PiiStrategy": "Partial",      // Mask | Partial | Hash
      "HashKey": "",                 // necessária se usar Hash (HMAC-SHA256, correlacionável)
      "AdditionalSecretKeys": [ "x-internal-signature" ],
      "AdditionalPiiKeys": [ "matricula" ]
    }
  }
}
```

- **`Mask`**: substitui o valor inteiro pelo placeholder. Use para segredos (não vaza nada).
- **`Partial`**: mantém os últimos `KeepLast` caracteres (correlação em suporte). Use só em PII.
- **`Hash`**: HMAC-SHA256 irreversível porém correlacionável (mesmo valor → mesmo hash); exige `HashKey`.
  Bom para auditoria sem expor o dado.

> Mesmo escolhendo `Partial` como estratégia geral, **segredos permanecem com máscara total** por padrão —
> mostrar os últimos dígitos de um token/senha vaza sem nenhum ganho operacional.

## Access-log de requisições (`UseAedisRequestLogging`)

O `AedisApiHost` liga, por padrão, um access-log estruturado (uma linha por requisição: método, rota, status,
tempo) — substituindo o logging ruidoso e multi-linha do ASP.NET. O enriquecimento é **seguro por construção**:

- Adiciona campos úteis e não sensíveis: `Host`, `Scheme`, `Protocol`, `UserAgent`, `ResponseContentType` e
  `UserId` (derivado do `sub` autenticado).
- A `QueryString` é registrada **já ofuscada** por `QueryStringRedactor` (mascara `?access_token=…`,
  `?token=…`, etc.) — usando as mesmas chaves desta configuração.
- O header `Authorization` **nunca** é registrado. E, como o evento final ainda passa pelo `RedactionEnricher`,
  há uma segunda camada de defesa por nome.
- `RequestPath` não inclui a query string (template padrão do Serilog), e `/health`/`favicon` são filtrados.

Desligue com `Logging:RequestLogging:Enabled = false`.
