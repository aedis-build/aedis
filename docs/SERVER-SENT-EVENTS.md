# Aedis — Server-Sent Events

`Aedis.Hosting.AspNetCore` entrega eventos ao navegador em tempo real (chat, progresso de tarefa, agentes de IA trabalhando) por **Server-Sent Events** (`text/event-stream`), passando pelo mesmo pipeline seguro do `AedisApiHost`: autenticação fail-closed, headers de segurança, rate limit e ProblemDetails antes do stream começar.

## Uso em um controller

```csharp
using System.Net.ServerSentEvents;
using Aedis.Hosting.AspNetCore.ServerSentEvents;

[HttpGet("conversas/{id:long}/eventos")]
public IActionResult Eventos(long id) {
    return this.ServerSentEvents((lastEventId, ct) =>
        _mensagens.AcompanharAsync(id, long.TryParse(lastEventId, out var ultimo) ? ultimo : 0, ct)
            .Select(m => new SseItem<MensagemResponse>(m, "mensagem") { EventId = m.Id.ToString() }));
}
```

- A fonte é um `IAsyncEnumerable<SseItem<T>>` (tipo da plataforma, `System.Net.ServerSentEvents`): `EventType` vira `event:`, `EventId` vira `id:` e `Data` sai como JSON com as opções do host (`SystemJsonOptionsFactory`: camelCase, nulos omitidos). `string` sai como texto.
- **Retomada:** na reconexão o navegador manda `Last-Event-ID` com o último `id:` recebido; a fábrica o recebe para continuar dali. O valor vem do cliente: o host recusa com `400` acima de 256 caracteres e a aplicação deve tratá-lo como entrada não confiável (parse, nunca concatenação em SQL).
- **Cancelamento:** o token da fábrica cancela quando o cliente desconecta ou a duração máxima vence. A fonte **deve** honrá-lo (`[EnumeratorCancellation]`), senão a conexão fica presa esperando o próximo item.
- **Erros:** validação e autorização acontecem antes do stream (viram ProblemDetails). O `200` sai na abertura, antes do primeiro item: exceção da fonte só derruba a conexão.

## O que o host faz pelo stream

| Ponto | Comportamento |
|---|---|
| Compressão | `Content-Encoding: identity` — o `ResponseCompression` do host não segura os eventos em buffer de gzip. |
| Buffer | `DisableBuffering()` e `FlushAsync` a cada evento; `X-Accel-Buffering: no` para nginx. |
| Cache | `Cache-Control: no-cache,no-store`. |
| Heartbeat | comentário `: heartbeat` a cada `HeartbeatInterval` (padrão 15 s) — ignorado pelo `EventSource`, mantém a conexão viva em proxies. |
| Duração máxima | ao vencer `MaxConnectionDuration` (padrão 30 min) o stream termina normalmente; o cliente reconecta com `Last-Event-ID`. Isso também força a revalidação do token, que só é checado na abertura. |
| Autenticação | o endpoint segue a política de fallback (`RequireAuthenticatedUser`): sem token, `401` antes de abrir o stream. |

```json
"ServerSentEvents": { "HeartbeatInterval": "00:00:15", "MaxConnectionDuration": "00:30:00" }
```

## Cliente e autenticação

O `EventSource` do navegador **não envia headers** (`Authorization`). Opções seguras:

- **BFF (recomendado com Keycloak):** o servidor web (ex.: route handler do Next.js) abre o stream na API com o bearer da sessão e repassa o corpo ao navegador; o token nunca vai ao browser.
- **`fetch` com stream** (ex.: `@microsoft/fetch-event-source`) mandando `Authorization: Bearer …`.

**Não** ponha o token na query string: ele vaza em logs de acesso de proxy e do ingress.

A CSP da **página** precisa permitir a origem da API em `connect-src`.

## Proxies e load balancers

- **nginx / ingress-nginx:** o host já manda `X-Accel-Buffering: no`; ajuste `proxy_read_timeout` acima do heartbeat (padrão 60 s basta) e use `proxy_http_version 1.1`. No ingress: `nginx.ingress.kubernetes.io/proxy-read-timeout`.
- **AWS ALB:** não bufferiza, mas fecha conexões ociosas no `idle_timeout` (padrão 60 s) — mantenha `HeartbeatInterval` abaixo dele.
- **CloudFront / CDN:** desligue cache e compressão na rota do stream.
- **Rate limit:** o limitador global conta requisições, não conexões abertas; reconexões em massa contam. Um teto de conexões simultâneas por usuário, se necessário, é regra da aplicação.
- **Kestrel:** `KeepAliveTimeout` e `RequestHeadersTimeout` do hardening não afetam uma resposta em andamento.
