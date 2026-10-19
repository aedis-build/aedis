<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://cdn.aedis.build/brand/aedis-mascot-lockup-dark.svg">
  <img src="https://cdn.aedis.build/brand/aedis-mascot-lockup.svg" alt="Aedis" width="420">
</picture>

**The secure foundation for domain-driven, event-driven .NET services.**

<sub><em>Golden-Path Platform · cloud-portable by design · secure by construction</em></sub>

<br>

[![aedis-ci](https://github.com/aedis-build/aedis-dotnet/actions/workflows/aedis-ci.yml/badge.svg?branch=main)](https://github.com/aedis-build/aedis-dotnet/actions/workflows/aedis-ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/aedis-build/aedis-dotnet/badge)](https://scorecard.dev/viewer/?uri=github.com/aedis-build/aedis-dotnet)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15704/badge)](https://www.bestpractices.dev/projects/15704)
[![codecov](https://codecov.io/gh/aedis-build/aedis-dotnet/graph/badge.svg)](https://codecov.io/gh/aedis-build/aedis-dotnet)
[![SLSA 3](https://slsa.dev/images/gh-badge-level3.svg)](https://slsa.dev/spec/v1.0/levels#build-l3)
[![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-C8A24C?style=flat-square&labelColor=1A2740)](https://www.conventionalcommits.org/)
[![License](https://img.shields.io/badge/license-Apache--2.0-C8A24C?style=flat-square&labelColor=1A2740)](LICENSE)
<img src="https://img.shields.io/badge/-10%20LTS-C8A24C?style=flat-square&labelColor=1A2740&logo=dotnet&logoColor=ECE5D5" alt=".NET 10 LTS">
<img src="https://img.shields.io/badge/OpenTelemetry-native-C8A24C?style=flat-square&labelColor=1A2740&logo=opentelemetry&logoColor=ECE5D5" alt="OpenTelemetry-native">
[![Status](https://img.shields.io/badge/status-preview-orange?style=flat-square&labelColor=1A2740)](#-status)

</div>

---

## What Aedis is

Aedis is a **secure-by-construction** .NET **Golden-Path Platform**. In the age of *vibe coding*, where studies show that a
meaningful share of AI-generated code introduces vulnerabilities, Aedis ships the guardrails already correct by default:
authentication, validation, secret handling, observability and the bounded-context patterns come assembled and
consistent with each other.

It is not greenfield: it was **extracted from a .NET 10 framework running in production**, distilling what already powers
a fleet of microservices into reusable building blocks.

> The name comes from the Latin *aedis*, the central building of a Roman temple: the structure that held everything up
> and, at the same time, the vault that kept the treasure. Structure **and** guard.

## Architecture

Calling it "hexagonal" undersells it. Aedis works on three levels:

| Layer | Pattern | In Aedis |
|---|---|---|
| Domain and boundaries | Hexagonal / Ports and Adapters | `*.Abstractions` (ports) ↔ one implementation per provider (adapters) |
| Platform and packages | Microkernel / Plug-in | `Aedis.Core` (kernel) + plug-ins by *package swap* |
| Hosting | IoC framework / Template Method | `AedisApiHost` / `AedisWorkerHost` deliver the golden path |

**Hexagonal in the domain, Microkernel in the platform.** Your domain code depends **only on contracts**; moving between
clouds, brokers or databases is **swapping a NuGet package** in the composition root. The domain does not even
recompile. Details in **[ARCHITECTURE.md](ARCHITECTURE.md)**.

## Modules (tiered topology)

| Tier | Packages |
|---|---|
| **1 · Pure core** | `Aedis.Core` · `Aedis.Exceptions` · `Aedis.Events` · `Aedis.Domain` · `Aedis.Commands` |
| **2 · Abstractions** | `Aedis.Cache.Abstractions` · `Aedis.Messaging.Abstractions` · `Aedis.Database.Abstractions` · `Aedis.Storage.Abstractions` · `Aedis.Hosting.Abstractions` · `Aedis.Hypermedia.Abstractions` · `Aedis.OpenApi.Abstractions` · `Aedis.Observability.Abstractions` · `Aedis.Security.Abstractions` · `Aedis.Secrets.Abstractions` · `Aedis.Signing.Abstractions` · `Aedis.Tenancy.Abstractions` · `Aedis.Payments.Abstractions` · `Aedis.ExchangeRates.Abstractions` · `Aedis.AI.Abstractions` |
| **3 · Implementations** | `Aedis.Cache.Redis` · `Aedis.Messaging.{RabbitMq,IbmMq,AwsSqs,AzureServiceBus}` · `Aedis.Database.{Postgres,SqlServer,MySql}` · `Aedis.Storage.{S3,AzureBlob}` · `Aedis.Secrets.{AwsSecretsManager,AzureKeyVault,Vault}` · `Aedis.Signing` · `Aedis.Signing.{AspNetCore,AwsKms}` · `Aedis.Observability.{Serilog,Otlp}` · `Aedis.Hosting.{AspNetCore,Worker}` · `Aedis.Hypermedia.AspNetCore` · `Aedis.OpenApi` · `Aedis.OpenApi.Scalar` (OpenAPI 3.1 + QUERY via `x-oai-additionalOperations`, rendered by Scalar) · `Aedis.Security.Keycloak` · `Aedis.Tenancy` · `Aedis.Diagnostics` · `Aedis.Payments.Stripe` · `Aedis.ExchangeRates.Ptax` · `Aedis.AI.ManagedAgents` |
| **4 · Meta and tooling** | `Aedis.Build` (batteries included) · `Aedis.Templates` · `Aedis.Analyzers` |

Real-time events to the browser (Server-Sent Events) through the secure host: **[docs/SERVER-SENT-EVENTS.md](docs/SERVER-SENT-EVENTS.md)**.

## Quickstart

The templates generate a four-layer (DDD) API or worker with the secure host, tests, Dockerfile and CI already wired:

```bash
dotnet new install Aedis.Templates
dotnet new aedis-api -n Shop
cd Shop && dotnet test
```

A host is a class that composes the layers; everything secure by default (fail-closed authentication, security headers,
rate limiting, RFC 9457 problem details, health probes, OpenTelemetry) comes from the base class:

```csharp
public sealed class ApiHost : AedisApiHost
{
    protected override void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) =>
        services.AddAedisKeycloakAuth(configuration);

    protected override void ConfigureServices(IConfiguration configuration, IServiceCollection services) {
        services.AddControllers();
        services.AddAedisPostgres(configuration);   // moving to SQL Server or MySQL is a package swap
    }

    protected override void ConfigureMiddleware(WebApplication app) => app.MapControllers();
}

await new ApiHost().RunAsync(args);
```

Until the packages reach NuGet, build the feed locally as described in [CONTRIBUTING.md](CONTRIBUTING.md).

## Security and supply chain

Every pull request runs the build with zero warnings, tests with a 95 % line / 85 % branch coverage gate, CodeQL and
Semgrep (with Aedis-specific rules); releases are versioned from Conventional Commits, signed with Sigstore and ship with
an SBOM and build provenance; actions are pinned by SHA and workflows start from `permissions: {}`. The repository is
audited against the Aedis conformance levels (L1–L3) by [aedis-platform](https://github.com/aedis-build/aedis-platform),
which also runs the policy-based merge gate. Details in [docs/SECURITY-COMPLIANCE.md](docs/SECURITY-COMPLIANCE.md) and
[docs/SECURITY-HARDENING.md](docs/SECURITY-HARDENING.md). How each OpenSSF Best Practices criterion is met, with the
evidence for every answer, is in [docs/compliance/openssf-best-practices.md](docs/compliance/openssf-best-practices.md).

## Documentation

- **Architecture**: [ARCHITECTURE.md](ARCHITECTURE.md), the layers and the dependency rule.
- **Module guides** in [docs/](docs): multi-tenancy, payments, AI, Server-Sent Events, log redaction, injection
  defense, hardening and the code style.
- **API reference**: every public type and member is documented in XML, shipped inside each NuGet package and shown by
  IntelliSense; the build fails when a public member has no documentation.
- **Templates**: `dotnet new aedis-api` and `dotnet new aedis-worker` generate a documented, tested starting point.

## Getting help and reporting issues

- **Bugs and feature requests**: [open an issue](https://github.com/aedis-build/aedis-dotnet/issues/new/choose) with
  the bug or feature form. The [issue archive](https://github.com/aedis-build/aedis-dotnet/issues?q=is%3Aissue) is
  public and searchable.
- **Questions and ideas**: [Discussions](https://github.com/aedis-build/aedis-dotnet/discussions).
- **Vulnerabilities**: never in a public issue. Use
  [Report a vulnerability](https://github.com/aedis-build/aedis-dotnet/security/advisories/new), as described in
  [SECURITY.md](SECURITY.md).

## Versioning and releases

Aedis follows [Semantic Versioning](https://semver.org/). The version is derived from the Conventional Commits merged
since the previous release (`feat` bumps the minor, `fix` the patch, a breaking change the major), and each release is
the git tag `vX.Y.Z`. [Releases](https://github.com/aedis-build/aedis-dotnet/releases) carry human-readable notes
built from the merged pull requests, the NuGet packages, Sigstore signatures, an SPDX SBOM and SLSA build provenance.

## 🚧 Status

Aedis is in **preview**, on its way to **v1.0 (GA)** through the decomposition of the source framework. Architecture and
modules are defined; APIs may still change. See **[MIGRATION.md](MIGRATION.md)** for the extraction plan and follow the
issues and milestones.

## Contributing

Contributions are welcome. See **[CONTRIBUTING.md](CONTRIBUTING.md)**, the **[Code of Conduct](CODE_OF_CONDUCT.md)**
and **[GOVERNANCE.md](GOVERNANCE.md)**. For vulnerabilities, follow the **[security policy](SECURITY.md)** and do not
open a public issue.

## License

[Apache-2.0](LICENSE). Open source, no strings attached.

---

<div align="center">
<sub>Domain-driven systems, distributed through events, portable across clouds, with clear data ownership. Secure and cloud-native by construction.</sub>
</div>
