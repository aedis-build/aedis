# Política de segurança

Use **Report a vulnerability** na aba Security deste repositório (GitHub Private Vulnerability Reporting). Não abra
issue pública para vulnerabilidades.

- Confirmação em até 3 dias úteis; avaliação de severidade em até 7 dias; correção ou mitigação para alta/crítica
  em até 30 dias.
- Segredos nunca entram neste repositório: `appsettings.json` versionado só tem valores vazios; em desenvolvimento
  use `appsettings.Development.json` ou user-secrets; em cluster o Aedis Platform os sincroniza do cofre.
