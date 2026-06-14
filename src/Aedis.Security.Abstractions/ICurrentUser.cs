namespace Aedis.Security.Abstractions;

/// <summary>
///     Representa o usuário autenticado no contexto atual (requisição/escopo).
///     Contrato agnóstico — a implementação extrai os dados do provider de identidade (ex.: JWT/OIDC).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Indica se há um usuário autenticado no contexto atual.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Identificador único do usuário (ex.: <c>sub</c> do token), ou null se anônimo.</summary>
    string? Id { get; }

    /// <summary>Nome de exibição do usuário, ou null se indisponível.</summary>
    string? Name { get; }

    /// <summary>Papéis (roles) atribuídos ao usuário.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Obtém o valor da primeira claim do tipo informado, ou null se ausente.</summary>
    string? FindClaim(string type);
}
