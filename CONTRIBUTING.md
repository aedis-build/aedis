# Contributing to Aedis

Thank you for your interest. Aedis grows with its community, and contributions of every size are welcome, from typo
fixes to new modules.

> This guide is the standard for **every repository** of the [`aedis-build`](https://github.com/aedis-build)
> organization. A repository may publish its own `CONTRIBUTING.md` with specific build, test and layout instructions
> that complement this one.

Before you start, read our [Code of Conduct](CODE_OF_CONDUCT.md). Taking part in the project means agreeing to uphold
it. How decisions are made and how maintainers are chosen is in [GOVERNANCE.md](GOVERNANCE.md).

## Ways to contribute

- 🐛 **Report bugs**: open an issue describing the expected behavior, the observed one and how to reproduce it.
- 💡 **Propose ideas**: use Discussions for architecture, new modules or design changes before opening a large PR.
- 📖 **Improve the docs**: clear documentation is as valuable as code.
- 🔧 **Send code**: fixes and features through pull requests (see below).

> For security vulnerabilities, **do not** open a public issue. Follow [SECURITY.md](SECURITY.md).

## Development environment

Prerequisite: **.NET 10 SDK** (or later). Each repository documents its specific steps; the usual flow is:

```bash
git clone https://github.com/aedis-build/<repo>.git
cd <repo>
dotnet build
dotnet test
```

Integration tests use Testcontainers and are opt-in by environment variable (for example `AEDIS_SQLSERVER_IT=1`);
Docker must be available for them. The CI runs them all.

### Testing a change from an app (local feed)

To consume Aedis from an app before a release, pack with a suffix that is **unique** per pack:

```bash
dotnet pack Aedis.slnx -c Release -o ../aedis-feed /p:VersionSuffix=preview.1.local.$(date +%Y%m%d%H%M)
```

and use that version in the app's `Directory.Packages.props`. Repacking with the same version (`0.1.0-preview.1`) never
reaches the app: NuGet does not re-extract a version that is already in the global cache (`~/.nuget/packages`) and
keeps restoring the old content. The typical symptom is the app failing on a service the current host already
registers (for example `Unable to resolve service for type ICommandExecutor`).

## Pull request flow

1. **Discuss first** for large changes: open an issue or a Discussion to align on direction.
2. Branch from `main` (`feat/short-name`, `fix/short-name`).
3. Keep the PR **small and focused**, one purpose per PR. Link the related issue.
4. **Tests are required.** Every new feature and every bug fix comes with automated tests that exercise it, and the
   suite stays green. The coverage gate is 95 % lines / 85 % branches.
5. Make sure `dotnet build` and `dotnet test` pass locally. Warnings are errors: a build with a warning fails.
6. Open the PR with a clear description of *what* and *why*. PRs that meet every criterion of
   `.github/aedis-merge-policy.yaml` are approved and merged by the Aedis Compliance merge gate; changes to security
   packages, workflows, licensing and versioning always get a human review.

## Commit standards

We use [Conventional Commits](https://www.conventionalcommits.org/); the CI rejects anything else:

```
feat(messaging): add outbox support to the publisher
fix(validation): fix 422 response for empty collections
docs(readme): adjust quickstart example
```

Common types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`. Subject up to 100 characters; scope with
`[a-z0-9._/-]` only.

## Code standards

- **Respect the dependency rule.** Domain and application code reference **only** contracts (`Aedis.Core`,
  `Aedis.*.Abstractions`); only the host / composition root references concrete implementations
  (`Aedis.*.<Provider>`). Do not couple the domain to infrastructure; that is what makes portability a package swap.
- **Respect context boundaries.** Each module (`Aedis.*`) has a clear responsibility; do not couple domains. One
  source of truth per context.
- **Domain first.** Model the business in ubiquitous language before technology.
- **Events over synchronous calls** where it makes sense: choreography, not central orchestration.
- **Secure by construction.** Do not disable default controls without justifying the trade-off; keep the secure
  posture as the path of least resistance.
- Follow the repository `.editorconfig`. `nullable` and `implicit usings` are enabled.
- Every public member has an XML summary in English (`CS1591` is on); no inline comments. See
  [docs/CODE-STYLE.md](docs/CODE-STYLE.md).
- Prefer explicit, testable code; avoid premature optimization, but document deliberate optimizations.
- Public APIs must be predictable and stable: treat contracts as a product.

## Contribution license

By submitting a pull request you agree that your contribution is licensed under [Apache-2.0](LICENSE), the same license
as the project. We recommend signing off your commits (`git commit -s`) per the
[DCO](https://developercertificate.org/).

## Review

Maintainers review PRs. We may ask for changes; that is a normal part of the process, not a judgment of your work. Be
patient and open to dialogue; we will do the same.

---

Thank you for helping build a secure foundation for the .NET ecosystem. 🦫
