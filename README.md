<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://cdn.aedis.build/brand/aedis-mascot-lockup-dark.svg">
  <img src="https://cdn.aedis.build/brand/aedis-mascot-lockup.svg" alt="Aedis" width="420">
</picture>

**A fundação segura para construir serviços .NET orientados a domínio e a eventos.**

<sub><em>Golden-Path Platform · cloud-portable by design · secure by construction</em></sub>

<br>

[![License](https://img.shields.io/badge/license-Apache--2.0-C8A24C?style=flat-square&labelColor=1A2740)](LICENSE)
<img src="https://img.shields.io/badge/-10%20LTS-C8A24C?style=flat-square&labelColor=1A2740&logo=dotnet&logoColor=ECE5D5" alt=".NET 10 LTS">
<img src="https://img.shields.io/badge/cloud--portable-by%20design-1A2740?style=flat-square&labelColor=1A2740" alt="cloud-portable by design">
<img src="https://img.shields.io/badge/secure-by%20construction-1A2740?style=flat-square&labelColor=1A2740" alt="secure by construction">
<img src="https://img.shields.io/badge/OpenTelemetry-native-C8A24C?style=flat-square&labelColor=1A2740&logo=opentelemetry&logoColor=ECE5D5" alt="OpenTelemetry-native">
[![Status](https://img.shields.io/badge/status-preview-orange?style=flat-square&labelColor=1A2740)](#-status)

</div>

---

## O que é o Aedis

Aedis é uma **Golden-Path Platform** .NET **segura por construção**. Na era do *vibe coding* — em que estudos apontam que parte relevante do código gerado por IA introduz vulnerabilidades — o Aedis entrega os *guardrails* já corretos por padrão: autenticação, validação, tratamento de segredos, observabilidade e os padrões de *bounded context* vêm montados e coerentes entre si.

Não é greenfield: nasce da **extração de um framework .NET 10 em produção**, destilando o que já roda numa frota de microsserviços em blocos reutilizáveis.

> O nome vem do latim *aedis* — o edifício central do templo romano: a estrutura que sustentava tudo e, ao mesmo tempo, o cofre que guardava o tesouro. Estrutura **e** guarda.

## A arquitetura

Dizer só "hexagonal" subvende o Aedis — ele opera em três níveis:

| Camada | Padrão | No Aedis |
|---|---|---|
| Domínio & fronteiras | Hexagonal / Ports & Adapters | `*.Abstractions` (portas) ↔ implementações por provider (adaptadores) |
| Plataforma & pacotes | Microkernel / Plug-in | `Aedis.Core` (kernel) + plug-ins por *package swap* |
| Hosting | Framework IoC / Template Method | `WebApiHost`/`StandaloneApp` entregam o *golden path* |

**Hexagonal no domínio, Microkernel na plataforma.** Seu código de domínio depende **só de contratos**; portar entre nuvem, broker ou banco é **trocar um pacote NuGet** no *composition root* — o domínio nem recompila. Detalhes em **[ARCHITECTURE.md](ARCHITECTURE.md)**.

## Módulos (topologia em tiers)

| Tier | Pacotes |
|---|---|
| **1 · Core puro** | `Aedis.Core` · `Aedis.Exceptions` · `Aedis.Events` · `Aedis.Domain` · `Aedis.Commands` |
| **2 · Abstrações** | `Aedis.Cache.Abstractions` · `Aedis.Messaging.Abstractions` · `Aedis.Database.Abstractions` · `Aedis.Storage.Abstractions` · `Aedis.Hosting.Abstractions` · `Aedis.Observability.Abstractions` · `Aedis.Security.Abstractions` |
| **3 · Implementações** | `Aedis.Cache.Redis` · `Aedis.Messaging.{RabbitMq,IbmMq,AwsSqs}` · `Aedis.Database.{Postgres,SqlServer}` · `Aedis.Storage.S3` · `Aedis.Observability.{Serilog,Otlp}` · `Aedis.Hosting.{AspNetCore,Worker}` · `Aedis.Security.Keycloak` · `Aedis.Diagnostics` |
| **4 · Meta + tooling** | `Aedis.Build` (batteries-included) · `Aedis.Templates` · `Aedis.Analyzers` |

## Quickstart

> ⚠️ API ilustrativa do design pretendido — em desenvolvimento, ainda não publicada.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddAedis(aedis =>
{
    aedis.AddDomain();
    aedis.AddMessaging().WithRabbitMq(builder.Configuration);   // portar p/ AWS = .WithAwsSqs(...)
    aedis.AddSecurity().WithKeycloak(builder.Configuration);
    aedis.AddObservability().WithOtlp();
});

var app = builder.Build();
app.UseAedis();
app.Run();
```

## 🚧 Status

Aedis está em **preview**, no caminho para a **v1.0 (GA)** via decomposição do framework de origem. A arquitetura e os módulos estão definidos; as APIs ainda vão mudar. Veja **[MIGRATION.md](MIGRATION.md)** para o plano de extração e acompanhe os *issues*/*milestones*.

## Contribuindo

Contribuições são bem-vindas — veja **[CONTRIBUTING.md](CONTRIBUTING.md)**. Para vulnerabilidades, siga a **[política de segurança](https://github.com/aedis-build/.github/blob/main/SECURITY.md)** (não abra issue pública).

## Licença

[Apache-2.0](LICENSE) — open source, sem pegadinhas.

---

<div align="center">
<sub>Sistemas orientados a domínio, distribuídos por eventos, portáveis entre nuvens e com ownership claro de dados — seguros e cloud-native por construção.</sub>
</div>
