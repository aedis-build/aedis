# Aedis.App1

Worker headless em camadas (DDD) gerado pelo template **`aedis-worker`**. Sobe sobre o `AedisWorkerHost`
(logging estruturado com ofuscação de PII/segredos, telemetria OTLP, health probe e shutdown gracioso) e
traz um **consumidor de mensagens** ponta a ponta: consome um evento de um broker (RabbitMQ), processa via
handler com repositório PostgreSQL de forma **idempotente** e publica um evento de follow-up.

> **Já tem uma solução e quer só adicionar um worker?** Use `dotnet new aedis-worker --add <Sua.slnx>` de
> dentro da pasta da solução — gera **apenas** um projeto worker autônomo e o adiciona à `.slnx`, sem
> duplicar camadas nem arquivos de raiz.

## Estrutura

```
src/
├── Aedis.App1.Worker/           Composition root: AedisWorkerHost + consumidor (BackgroundService)
│                                 + escopo por mensagem (ScopedMessageHandler)
├── Aedis.App1.Application/      Casos de uso: IMessageHandler, eventos (in/out), contrato do repositório
├── Aedis.App1.Domain/           Domínio: entidade Notification (AuditableAggregateRoot)
└── Aedis.App1.Infrastructure/   Persistência: repositório (Aedis.Database.Postgres) + Criteria + migração
tests/
└── Aedis.App1.UnitTests/        Testes (NSubstitute + FluentAssertions): handler (processa + idempotência)
```

## Fluxo

```
broker (RabbitMQ)  ──NotificationRequested──▶  NotificationConsumerService (BackgroundService)
                                                  │  (escopo de DI por mensagem)
                                                  ▼
                                        NotificationRequestedEventHandler
                                          1. já enviada? → ignora (idempotência)
                                          2. cria/marca Sent + persiste (PostgreSQL)
                                          3. publica NotificationSent  ──▶ broker
```

A entrega é *at-least-once*: o handler é **idempotente** (chave de negócio `Code`), e o consumidor aplica
**retry com backoff + dead-letter** após o limite de tentativas.

## Rodando

```bash
# 1) Banco: aplique a migração de referência
psql "$CONNECTION" -f src/Aedis.App1.Infrastructure/Persistence/Migrations/0001_create_notification.sql

# 2) Configure Database e RABBITMQ em src/Aedis.App1.Worker/appsettings.json (e suba RabbitMQ + Postgres)

# 3) Suba o worker
dotnet run --project src/Aedis.App1.Worker

# Testes (não exigem broker/banco)
dotnet test

# Health
curl http://localhost:8080/health
```

## Trocar de broker

O worker depende de `IMessageBrokerService` (contrato agnóstico). Para usar AWS SQS/SNS, Azure Service Bus ou
IBM MQ no lugar do RabbitMQ, troque `AddAedisRabbitMq` por `AddAedisAwsSqs`/`AddAedisAzureServiceBus`/
`AddAedisIbmMq` em `WorkerHost` — o resto do código não muda. Para jobs CRON em vez de consumo de fila, use
`Aedis.Scheduling.Hangfire`.

## Imagem e deploy

```bash
docker build -t aedis.app1 .
```

O `Dockerfile` usa base *chiseled* non-root; `deploy/deployment.yaml` aplica o endurecimento de runtime. Para
um worker **sem** servidor HTTP, sobrescreva `EnableHealthEndpoint => false` em `WorkerHost`.

## Versão dos pacotes Aedis

```bash
dotnet new aedis-worker -n MeuWorker --AedisVersion 0.1.0-preview.1
```
