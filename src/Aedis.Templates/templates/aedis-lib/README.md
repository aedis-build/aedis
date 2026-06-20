# AedisLibrary1

Biblioteca de classes gerada com `dotnet new aedis-lib`, já configurada com as convenções do Aedis:
`net10.0`, nullable, `ImplicitUsings`, **documentação XML** ligada (`GenerateDocumentationFile`).

## Convenções

- Documente toda a superfície pública com `///` `<summary>` didático (alvo `CS1591 = 0`).
- **Sem comentários inline** no corpo dos métodos — o porquê essencial vai no `<summary>`/`<remarks>`.
- Para expor injeção de dependência no estilo Aedis, adicione uma extensão `AddX` em
  `Microsoft.Extensions.DependencyInjection` (requer `Microsoft.Extensions.DependencyInjection.Abstractions`).

Ver `docs/CODE-STYLE.md` do Aedis.
