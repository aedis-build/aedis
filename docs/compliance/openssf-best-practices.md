# OpenSSF Best Practices: how Aedis meets each criterion

Aedis is registered at [bestpractices.dev, project 15704](https://www.bestpractices.dev/projects/15704), in the
Metal series. This page is the evidence behind every answer of the **passing** level: each criterion links to the
file or setting that proves it. When an answer changes, this page changes in the same pull request.

Repository: <https://github.com/aedis-build/aedis-dotnet>. Links below point to the `main` branch.

## General

| Field | Answer |
|---|---|
| Name | Aedis |
| Description | Secure-by-construction .NET foundation for domain-driven, event-driven services: hosts, security, messaging, data access and observability as swappable packages. |
| Language of this entry | English |
| Project URL | <https://github.com/aedis-build/aedis-dotnet> (until <https://aedis.build> is published) |
| License | Apache-2.0 |
| Implementation language | C# |
| CPE | none |

## Basics

| Criterion | Status | Justification and evidence |
|---|---|---|
| `description_good` | Met | The README opens with what Aedis is and the problem it solves: [What Aedis is](../../README.md#what-aedis-is). |
| `interact` | Met | Obtain: [Quickstart](../../README.md#quickstart). Feedback: [GitHub Issues](https://github.com/aedis-build/aedis-dotnet/issues) with bug and feature forms, and [Discussions](https://github.com/aedis-build/aedis-dotnet/discussions). Contribute: [CONTRIBUTING.md](../../CONTRIBUTING.md). |
| `contribution` | Met | Pull-request based process: [CONTRIBUTING.md, pull request flow](../../CONTRIBUTING.md#pull-request-flow). |
| `contribution_requirements` | Met | [Commit standards](../../CONTRIBUTING.md#commit-standards), [code standards](../../CONTRIBUTING.md#code-standards) and [docs/CODE-STYLE.md](../CODE-STYLE.md). |
| `floss_license` | Met | Apache-2.0. |
| `floss_license_osi` | Met | Apache-2.0 is OSI-approved. |
| `license_location` | Met | [LICENSE](../../LICENSE) at the repository root; per-file licensing checked by REUSE ([REUSE.toml](../../REUSE.toml)). |
| `documentation_basics` | Met | [README.md](../../README.md), [ARCHITECTURE.md](../../ARCHITECTURE.md) and the module guides in [docs/](../). |
| `documentation_interface` | Met | Every public type and member carries XML documentation; the build fails on a missing summary (CS1591 with warnings as errors), and the XML ships inside each NuGet package for IntelliSense. Module guides in [docs/](../) describe inputs, outputs and configuration. |
| `sites_https` | Met | The repository, releases and packages are served only over HTTPS (GitHub, nuget.org). |
| `discussion` | Met | [Issues](https://github.com/aedis-build/aedis-dotnet/issues), [pull requests](https://github.com/aedis-build/aedis-dotnet/pulls) and [Discussions](https://github.com/aedis-build/aedis-dotnet/discussions): searchable, addressable by URL, open to anyone with a GitHub account, no proprietary client. |
| `english` | Met | README, CONTRIBUTING, SECURITY, GOVERNANCE and the public API documentation are in English, and reports are accepted in English ([docs/CODE-STYLE.md](../CODE-STYLE.md)). |
| `maintained` | Met | Continuous commits and pull requests: [commit history](https://github.com/aedis-build/aedis-dotnet/commits/main). |

## Change control

| Criterion | Status | Justification and evidence |
|---|---|---|
| `repo_public` | Met | <https://github.com/aedis-build/aedis-dotnet>. |
| `repo_track` | Met | Git records author, date and content of every change. |
| `repo_interim` | Met | Every change lands through a reviewed pull request; interim work is visible before any release. |
| `repo_distributed` | Met | Git. |
| `version_unique` | Met | Each release has a unique SemVer version computed by [release.yml](../../.github/workflows/release.yml); packages carry it. |
| `version_semver` | Met | SemVer derived from Conventional Commits: `feat` bumps minor, `fix` bumps patch, `!`/`BREAKING CHANGE` bumps major. |
| `version_tags` | Met | Each release is the git tag `vX.Y.Z` created by [release.yml](../../.github/workflows/release.yml). |
| `release_notes` | Met after the first release | GitHub Releases generate the notes from the Conventional Commit titles of the merged pull requests (a curated, human-readable summary, not a raw log): [releases](https://github.com/aedis-build/aedis-dotnet/releases). |
| `release_notes_vulns` | N/A | No publicly known vulnerability has been fixed so far. |

## Reporting

| Criterion | Status | Justification and evidence |
|---|---|---|
| `report_process` | Met | [GitHub Issues](https://github.com/aedis-build/aedis-dotnet/issues/new/choose) with a bug report form. |
| `report_tracker` | Met | GitHub Issues. |
| `report_responses` | Met | Every issue opened so far was triaged and resolved by a linked pull request ([closed issues](https://github.com/aedis-build/aedis-dotnet/issues?q=is%3Aissue+is%3Aclosed)). |
| `enhancement_responses` | Met | Same tracker and process for enhancement requests. |
| `report_archive` | Met | [Issue archive](https://github.com/aedis-build/aedis-dotnet/issues?q=is%3Aissue), public and searchable. |
| `vulnerability_report_process` | Met | [SECURITY.md](../../SECURITY.md). |
| `vulnerability_report_private` | Met | GitHub Private Vulnerability Reporting: [report a vulnerability](https://github.com/aedis-build/aedis-dotnet/security/advisories/new). |
| `vulnerability_report_response` | N/A | No vulnerability report received in the last 6 months. The committed response time is 3 business days ([SECURITY.md](../../SECURITY.md)). |

## Quality

| Criterion | Status | Justification and evidence |
|---|---|---|
| `build` | Met | `dotnet build Aedis.slnx` rebuilds everything from source; the same command runs in CI. |
| `build_common_tools` | Met | .NET SDK and MSBuild. |
| `build_floss_tools` | Met | The .NET SDK is MIT-licensed; no proprietary tool is needed. |
| `test` | Met | xUnit suites under [tests/](../../tests), run with `dotnet test` as documented in [CONTRIBUTING.md](../../CONTRIBUTING.md#development-environment) and on every pull request by [aedis-ci.yml](../../.github/workflows/aedis-ci.yml). |
| `test_invocation` | Met | `dotnet test`, the standard command for .NET. |
| `test_most` | Met | Merged coverage gate of 95 % lines and 85 % branches on every pull request; Codecov publishes the report. |
| `test_continuous_integration` | Met | [aedis-ci.yml](../../.github/workflows/aedis-ci.yml) builds and tests every pull request and every push to `main`. |
| `test_policy` | Met | New behavior must come with tests: [CONTRIBUTING.md, pull request flow](../../CONTRIBUTING.md#pull-request-flow). |
| `tests_are_added` | Met | The coverage gate rejects changes that lower coverage below the floor, so new code arrives tested; recent example: the MySQL provider shipped with its own suite. |
| `tests_documented_added` | Met | Stated in [CONTRIBUTING.md](../../CONTRIBUTING.md#pull-request-flow). |
| `warnings` | Met | Compiler warnings, nullable reference types, .NET analyzers and the Aedis analyzers ([Aedis.Analyzers](../../src/Aedis.Analyzers)). |
| `warnings_fixed` | Met | `TreatWarningsAsErrors` is on for every project ([Directory.Build.props](../../Directory.Build.props)): a build with a warning fails. |
| `warnings_strict` | Met | Warnings as errors, nullable enabled, documentation warnings enforced. |

## Security

| Criterion | Status | Justification and evidence |
|---|---|---|
| `know_secure_design` | Met | The lead maintainer designs the secure-by-default hosts (fail-closed authentication, minimal surface, opt-in documentation) documented in [docs/SECURITY-COMPLIANCE.md](../SECURITY-COMPLIANCE.md). |
| `know_common_errors` | Met | Injection, log forging, SSRF and secret leakage are covered with countermeasures in [docs/INJECTION-DEFENSE.md](../INJECTION-DEFENSE.md) and [docs/LOG-REDACTION.md](../LOG-REDACTION.md), and enforced by Aedis-specific Semgrep rules. |
| `crypto_published` | Met | ECDSA P-256 with SHA-256 (JWS ES256), HTTP Message Signatures (RFC 9421) and HMAC-SHA256: all published, expert-reviewed algorithms. |
| `crypto_call` | Met | Cryptography comes from `System.Security.Cryptography`, AWS KMS or the NSign library; Aedis implements none of its own primitives. |
| `crypto_floss` | Met | The in-process signing provider runs entirely on the MIT-licensed .NET runtime; cloud KMS providers are optional. |
| `crypto_keylength` | Met | Signing keys are P-256 (128-bit security, beyond the NIST 2030 minimum); the in-process provider rejects any other curve. |
| `crypto_working` | Met | No MD4, MD5, DES, RC4 or Dual_EC_DRBG anywhere in the code base. |
| `crypto_weaknesses` | Met | No SHA-1 or weak modes in default mechanisms. |
| `crypto_pfs` | N/A | Aedis does not implement key agreement; TLS is terminated by Kestrel or the ingress, which negotiate forward-secret suites. |
| `crypto_password_storage` | N/A | Aedis never stores user passwords; authentication is delegated to an OpenID Connect provider. |
| `crypto_random` | Met | Nonces come from `RandomNumberGenerator`; keys are generated by `ECDsa.Create` or by the KMS. |
| `delivery_mitm` | Met | Source, releases and packages are delivered over HTTPS; release assets are signed with Sigstore and carry build provenance. |
| `delivery_unsigned` | Met | No hash is fetched over HTTP; signatures are verified with Sigstore and `gh attestation verify`. |
| `vulnerabilities_fixed_60_days` | Met | Dependabot alerts and updates are on, and every build runs a known-vulnerability scan that fails on High/Critical findings without an expiring acceptance. |
| `vulnerabilities_critical_fixed` | Met | Same scan; critical findings block merges until fixed. |
| `no_leaked_credentials` | Met | CI uses OIDC only, with no long-lived secrets in the repository; Semgrep `p/secrets` runs on every pull request and GitHub secret scanning with push protection is on. |

## Analysis

| Criterion | Status | Justification and evidence |
|---|---|---|
| `static_analysis` | Met | CodeQL and Semgrep run on every pull request and before every release ([aedis-ci.yml](../../.github/workflows/aedis-ci.yml)). |
| `static_analysis_common_vulnerabilities` | Met | CodeQL security queries, Semgrep `p/owasp-top-ten`, `p/secrets`, `p/csharp` and the Aedis rules. |
| `static_analysis_fixed` | Met | The SAST gate fails on any unaccepted ERROR finding; accepted risks need a reason and an expiry date in [sast-accept.yaml](../../sast-accept.yaml). |
| `static_analysis_often` | Met | On every commit and weekly on `main`. |
| `dynamic_analysis` | Met | Property-based tests with FsCheck on the parsers of external input, plus integration suites against real SQL Server, LocalStack, Vault and IBM MQ containers. |
| `dynamic_analysis_unsafe` | N/A | C# is memory-safe; Aedis contains no unsafe code. |
| `dynamic_analysis_enable_assertions` | Met | Invariants are guard clauses that are always active (not `Debug.Assert`), so every test run exercises them. |
| `dynamic_analysis_fixed` | Met | Dynamic analysis runs on every pull request and a failure blocks the merge (required checks plus the Aedis Compliance merge gate), so any exploitable finding is fixed before the change lands. None of medium or higher severity has been found so far. |
