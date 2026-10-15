# Política de segurança

## Versões suportadas

| Versão | Suporte |
|---|---|
| 0.x (preview) | correções em `main` |

## Como reportar uma vulnerabilidade

Use **Report a vulnerability** na aba Security deste repositório (GitHub Private Vulnerability Reporting). Não abra
issue pública para vulnerabilidades.

- Confirmação de recebimento em até 3 dias úteis.
- Avaliação inicial de severidade (CVSS) em até 7 dias.
- Correção ou mitigação documentada para severidade alta/crítica em até 30 dias.
- Crédito ao pesquisador no advisory, se desejar.

## Escopo

Todos os pacotes `Aedis.*` publicados a partir deste repositório, os templates `dotnet new aedis-*` e o analisador
`Aedis.Analyzers`. A entrega em Kubernetes (chart, políticas, workflows) é do
[aedis-platform](https://github.com/aedis-build/aedis-platform), que tem a sua própria política.

## Práticas deste repositório

- Actions pinadas por SHA, `permissions: {}` por padrão, sem credenciais de longa duração.
- Dependências atualizadas por Dependabot com cooldown; CodeQL e Semgrep (regras do Aedis) a cada PR; OpenSSF
  Scorecard publicado; selo OpenSSF Best Practices em andamento.
- Postura de segurança dos pacotes em `docs/SECURITY-COMPLIANCE.md` e `docs/SECURITY-HARDENING.md`.
