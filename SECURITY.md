# Security policy

## Supported versions

| Version | Support |
|---|---|
| 0.x (preview) | fixes land on `main` |

## Reporting a vulnerability

Use **Report a vulnerability** in the Security tab of this repository (GitHub Private Vulnerability Reporting). Do not
open a public issue for vulnerabilities.

- Acknowledgement within 3 business days.
- Initial severity assessment (CVSS) within 7 days.
- Fix or documented mitigation for high/critical severity within 30 days.
- Credit to the researcher in the advisory, if desired.

## Scope

Every `Aedis.*` package published from this repository, the `dotnet new aedis-*` templates and the `Aedis.Analyzers`
analyzer. Kubernetes delivery (chart, policies, workflows) belongs to
[aedis-platform](https://github.com/aedis-build/aedis-platform), which has its own policy.

## Practices of this repository

- Actions pinned by SHA, `permissions: {}` by default, no long-lived credentials.
- Dependencies updated by Dependabot with cooldown; CodeQL and Semgrep (Aedis rules) on every pull request;
  known-vulnerability scan of dependencies (`deps-summary`) on every build; OpenSSF Scorecard published; OpenSSF Best
  Practices badge in progress.
- Releases versioned from Conventional Commits, signed with Sigstore, with SBOM (SPDX) and build provenance (GitHub
  Artifact Attestations).
- Security posture of the packages in `docs/SECURITY-COMPLIANCE.md` and `docs/SECURITY-HARDENING.md`.
