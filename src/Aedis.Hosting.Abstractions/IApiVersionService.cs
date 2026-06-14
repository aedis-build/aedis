namespace Aedis.Hosting.Abstractions;

/// <summary>
///     Serviço para acessar informações de versionamento da API.
///     Útil em controllers e em outras partes da aplicação.
/// </summary>
public interface IApiVersionService
{
    /// <summary>Versão da API formatada para display (ex.: "v1", "v2.0").</summary>
    string DisplayVersion { get; }

    /// <summary>Versão da API formatada para rotas (ex.: "1", "2").</summary>
    string RouteVersion { get; }

    /// <summary>Prefixo de rota quando o versionamento por prefixo está habilitado (ex.: "v1").</summary>
    string? RoutePrefix { get; }
}
