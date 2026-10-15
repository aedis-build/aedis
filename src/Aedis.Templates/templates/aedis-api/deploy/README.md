# deploy/

`app.yaml` são os values do chart `aedis-app` do [Aedis Platform](https://github.com/aedis-build/aedis-platform)
para esta aplicação. Não é aplicado daqui: vai para `apps/<nome>/app.yaml` do repositório GitOps
(`aedis platform new-app <nome> --from-project .` já o gera a partir deste projeto).

Baseline de pod, segredos via External Secrets, identidade de workload, canário e NetworkPolicy vêm do chart;
aqui ficam só o que é desta app: imagem, recursos, chaves de segredo, exposição e escala.
