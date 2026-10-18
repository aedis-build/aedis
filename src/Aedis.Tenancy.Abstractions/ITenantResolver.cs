namespace Aedis.Tenancy.Abstractions;

/// <summary>
///     Descobre o tenant da requisição atual (ex.: a partir de uma claim do token, da organização do Keycloak
///     ou de uma consulta ao cadastro). Chamado uma vez por requisição pelo middleware de tenancy, já com o
///     usuário autenticado. Registrado como <em>scoped</em>, então pode depender de repositórios.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    ///     Devolve o identificador do tenant da requisição, ou <c>null</c> quando não há como determiná-lo
    ///     (anônimo, claim ausente, ambíguo). <c>null</c> não é erro: o acesso a dado escopado é que falha fechado.
    /// </summary>
    /// <param name="ct">Cancelamento da requisição.</param>
    ValueTask<string?> ResolveAsync(CancellationToken ct = default);
}
