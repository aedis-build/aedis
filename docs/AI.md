# Aedis.AI — agentes de IA

> Contrato para plataformas que executam agentes de IA em sessões gerenciadas pelo provedor, e o primeiro provider, sobre o Claude Managed Agents (beta).

## Pacotes

| Pacote | Tier | Papel |
|---|---|---|
| `Aedis.AI.Abstractions` | 2 | `IAgentEngine`, sessão (`NewSession`, `MountedMemory`), eventos (`AgentMessage`, `ToolCall`, `SessionStopped`), `StopReasons`, `ToolResult`, `ToolArguments` + `InvalidToolCallException`, `AddAedisAI()` |
| `Aedis.AI.ManagedAgents` | 3 | `ManagedAgentsEngine` sobre o SDK oficial da Anthropic; `WithManagedAgents(configuration)` |

O código de aplicação depende só de `Aedis.AI.Abstractions`; a composition root escolhe o provider:

```csharp
services.AddAedisAI().WithManagedAgents(configuration);
```

```json
{ "AI": { "ManagedAgents": { "ApiKey": "<injetada por um provider de segredos>" } } }
```

Sem `AI:ManagedAgents:ApiKey`, o SDK usa `ANTHROPIC_API_KEY` (ou o perfil do `ant auth login`).

## Ciclo de uma sessão

1. `CreateSessionAsync(NewSession)` abre a sessão com agente, ambiente, memórias montadas, skills e **teto de gasto**.
2. `SendAndFollowAsync(sessionId, system, message, toolResults)` envia e acompanha até a sessão parar. O último evento é sempre `SessionStopped`; com `StopReasons.RequiresAction`, a aplicação executa as `ToolCall` e responde no próximo envio (ou com `SendToolResultsAsync`).
3. `GetCostCentsAsync` lê o gasto acumulado; `CreateMemoryStoreAsync`/`WriteMemoryAsync` mantêm memórias persistentes.

Comportamentos do provider Managed Agents:

- **Stream-first**: o stream de eventos abre antes do envio, então nada do que acontece antes de ele conectar se perde.
- **Reconexão**: se o stream cair, reconecta até 3 vezes (`ManagedAgentsEngine.MaxReconnects`) e cobre o intervalo com o histórico desde o envio, deduplicado por id. Se o stream terminar antes de a sessão parar, emite `SessionStopped` com `StopReasons.Error`.
- **Ordem do envio**: respostas de ferramentas, depois a mensagem, e o `system.message` por último (exigência da API).
- **Eventos brutos**: corpos e eventos são lidos do JSON da API, não das uniões tipadas do SDK, que mudam entre versões do beta.

## Segurança

- **Entrada de ferramenta é dado não confiável.** `ToolCall.Input` vem do modelo e pode ter sido induzido por prompt injection. Leia com `ToolArguments.GetString`/`GetOptionalString`, que validam na fronteira e lançam `InvalidToolCallException`; devolva a mensagem como `ToolResult(..., IsError: true)` para o agente corrigir a chamada. Autorize cada ação pelo contexto da aplicação (quem pediu, em qual tenant), nunca pelo que o modelo afirma.
- **Teto de gasto por sessão.** `NewSession.BudgetCents` é obrigatório e positivo: o provider recusa zero ou negativo antes de chamar a API. Ao atingir o teto a sessão para com `StopReasons.BudgetReached`. Dimensione o teto pela tarefa, não pelo pior caso.
- **Memória somente leitura.** Monte conteúdo curado pela plataforma (diretrizes, políticas, dados de referência) com `MountedMemory.ReadOnly = true` e altere-o só por `WriteMemoryAsync`. Escrita pelo agente fica restrita às memórias que ele mesmo mantém — assim uma sessão comprometida não reescreve as regras das próximas.
- **Chave da API.** Injete por um provider de segredos (`Aedis.Secrets.*`) ou variável de ambiente; não versione em `appsettings`.
