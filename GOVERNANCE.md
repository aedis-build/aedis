# Governance

Aedis is an open-source .NET framework maintained by the `aedis-build` organization under the Apache-2.0
license. This document describes who decides what, and how.

## Roles

- **Maintainers** have write access, review and merge pull requests, cut releases and own the security
  process described in [SECURITY.md](SECURITY.md). The current maintainers are listed in
  [CODEOWNERS](CODEOWNERS).
- **Contributors** are anyone who opens an issue or a pull request. Contributions follow
  [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

## How decisions are made

- Day-to-day changes are decided in pull requests. A change is merged when it passes the required checks
  (build with zero warnings, tests with the coverage gate, static analysis, Conventional Commits) and the
  review required by the repository ruleset. Changes to security packages, workflows, licensing and
  versioning always require a human maintainer review; everything else may be approved by the Aedis
  Compliance merge gate when every criterion of `.github/aedis-merge-policy.yaml` passes.
- Design decisions that affect more than one package (new provider, breaking change, new abstraction) are
  proposed in an issue first, with the alternatives considered, and recorded in `docs/` when accepted.
- Disagreements are resolved by consensus among maintainers; when consensus is not reached, the project lead
  (the `aedis-build` organization owner) decides and documents the reasoning in the issue.

## Becoming a maintainer

A contributor becomes a maintainer by invitation of the existing maintainers after sustained, high-quality
contributions (code, reviews, triage, documentation) over several months, and after accepting the security and
release responsibilities above. Maintainers who are inactive for six months may be moved to emeritus status.

## Security and releases

Vulnerabilities are reported privately as described in [SECURITY.md](SECURITY.md). Releases follow semantic
versioning derived from Conventional Commits, are signed, and ship with an SBOM and build provenance.

## Changes to this document

Changes to governance are made by pull request and require approval of a maintainer other than the author.
