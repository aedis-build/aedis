# Aedis.App1.Worker (adicionado)

Projeto worker adicionado a uma solução existente com `dotnet new aedis-worker --add <Solução.slnx>`. É um
consumidor de mensagens **autônomo** (RabbitMQ): consome `NotificationRequested`, processa e publica
`NotificationSent`. Não traz camadas próprias — para persistir, reuse as da sua solução.

## Reusar suas camadas (persistência)

1. Descomente as `ProjectReference` em `Aedis.App1.Worker.csproj` (Application/Domain/Infrastructure).
2. No `WorkerHost`, chame `services.AddInfrastructure(configuration)` e configure a seção `Database`.
3. No `NotificationRequestedEventHandler`, injete seu repositório e faça get-or-create + save de forma
   **idempotente** antes de publicar o follow-up (veja o template standalone para o padrão completo).

## Trocar de broker

O worker depende de `IMessageBrokerService`. Troque `AddAedisRabbitMq` por `AddAedisAwsSqs` /
`AddAedisAzureServiceBus` / `AddAedisIbmMq` no `WorkerHost` — o resto não muda.

## Rodar

```bash
dotnet run --project src/Aedis.App1.Worker   # exige RabbitMQ; configure a seção RABBITMQ
```
