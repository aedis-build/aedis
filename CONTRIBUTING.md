# Contribuindo com o Aedis

Obrigado pelo interesse em contribuir! O Aedis cresce com a comunidade, e contribuições de todos os tamanhos são bem-vindas — de correções de typo a novos módulos.

> Este guia vale como padrão para **todos os repositórios** da organização [`aedis-build`](https://github.com/aedis-build). Um repositório pode publicar o seu próprio `CONTRIBUTING.md` com instruções específicas (build, testes, estrutura) que complementam este.

Antes de começar, leia nosso [Código de Conduta](CODE_OF_CONDUCT.md). Participar do projeto significa concordar em mantê-lo.

## Formas de contribuir

- 🐛 **Reportar bugs** — abra uma *issue* descrevendo o comportamento esperado, o observado e como reproduzir.
- 💡 **Propor ideias** — use *Discussions* para arquitetura, novos módulos ou mudanças de design antes de abrir um PR grande.
- 📖 **Melhorar docs** — documentação clara é tão valiosa quanto código.
- 🔧 **Enviar código** — correções e features via Pull Request (veja abaixo).

> Para vulnerabilidades de segurança, **não** abra uma issue pública. Siga o [SECURITY.md](SECURITY.md).

## Ambiente de desenvolvimento

Pré-requisitos: **.NET 10 SDK** (ou superior). Cada repositório documenta seus passos específicos; o fluxo típico é:

```bash
git clone https://github.com/aedis-build/<repo>.git
cd <repo>
dotnet build
dotnet test
```

## Fluxo de Pull Request

1. **Discuta antes** de mudanças grandes — abra uma issue ou Discussion para alinhar a direção.
2. Crie um *branch* a partir de `main` (`feat/nome-curto`, `fix/nome-curto`).
3. Mantenha o PR **pequeno e focado** — um propósito por PR. Vincule a issue relacionada.
4. Inclua **testes** para novo comportamento e mantenha a suíte verde.
5. Garanta que `dotnet build` e `dotnet test` passam localmente.
6. Abra o PR com uma descrição clara do *quê* e do *porquê*.

## Padrões de commit

Usamos [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(messaging): adiciona suporte a outbox no publisher
fix(validation): corrige resposta 422 para coleções vazias
docs(readme): ajusta exemplo de quickstart
```

Tipos comuns: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`.

## Padrões de código

- **Respeite a regra de dependência.** Código de domínio e aplicação referencia **apenas** contratos (`Aedis.Core`, `Aedis.*.Abstractions`); somente o *host*/*composition root* referencia implementações concretas (`Aedis.*.<Provider>`). Não acople o domínio à infraestrutura — é o que torna a portabilidade um *package swap*.
- **Respeite os limites de contexto.** Cada módulo (`Aedis.*`) tem responsabilidade clara; não acople domínios. Uma fonte da verdade por contexto.
- **Domínio primeiro.** Modele o negócio com linguagem ubíqua antes da tecnologia.
- **Eventos sobre chamadas síncronas** onde fizer sentido — coreografia, não orquestração central.
- **Seguro por construção.** Não desabilite controles padrão sem justificar o trade-off; mantenha a postura segura como caminho de menor resistência.
- Siga o `.editorconfig` do repositório. `nullable` e `implicit usings` habilitados.
- Prefira código testável e explícito; evite otimização prematura, mas documente quando otimizar de propósito.
- APIs públicas devem ser previsíveis e estáveis — trate contratos como produto.

## Licença das contribuições

Ao enviar um Pull Request, você concorda que sua contribuição é licenciada sob a [Apache-2.0](LICENSE), a mesma licença do projeto. Recomendamos assinar seus commits (`git commit -s`) para incluir o *sign-off* do [DCO](https://developercertificate.org/).

## Revisão

PRs são revisados pelos mantenedores. Podemos pedir ajustes — é parte normal do processo e não um julgamento sobre o seu trabalho. Seja paciente e aberto ao diálogo; faremos o mesmo.

---

Obrigado por ajudar a construir uma fundação segura para o ecossistema .NET. 🦫
