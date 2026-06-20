# AedisWorker1

Serviço headless (worker/consumer/batch) gerado com `dotnet new aedis-worker`. Sobe sobre o
`AedisWorkerHost`, então já inclui **logging estruturado**, **telemetria OTLP**, **health probes**
(`/health/live`, `/health/ready`) e **shutdown gracioso**.

## Rodar

```bash
dotnet run
curl http://localhost:8080/health/ready
```

## Imagem e deploy

```bash
docker build -t aedisworker1 .
```

Base *chiseled* non-root; `deploy/deployment.yaml` aplica o endurecimento de runtime. Para um worker
**sem** servidor HTTP, sobrescreva `EnableHealthEndpoint => false` em `WorkerHost`.

## Próximos passos

- Substitua `SampleWorker` pela sua lógica (consumer de mensageria, job, batch).
- Adicione os pacotes que precisar (ex.: `Aedis.Messaging.RabbitMq`, `Aedis.Scheduling.Hangfire`).
