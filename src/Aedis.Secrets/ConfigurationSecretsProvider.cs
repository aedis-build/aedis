using Aedis.Secrets.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aedis.Secrets;

/// <summary>
///     Provider de segredos que lê do próprio <see cref="IConfiguration" /> — fallback para
///     desenvolvimento local, onde os segredos vêm de <c>appsettings</c>/env-vars/user-secrets em vez de um
///     cofre externo. Procura cada segredo sob o prefixo de seção configurado (padrão <c>Secrets</c>): o
///     segredo <c>DbPassword</c> é lido da chave <c>Secrets:DbPassword</c>. Não expõe versão/rotação.
/// </summary>
public sealed class ConfigurationSecretsProvider : ISecretsProvider
{
    private readonly IConfiguration _configuration;
    private readonly string? _sectionPrefix;

    /// <summary>Cria o provider sobre a configuração, lendo segredos sob o prefixo <paramref name="sectionPrefix" /> (vazio = raiz).</summary>
    public ConfigurationSecretsProvider(IConfiguration configuration, string? sectionPrefix = "Secrets") {
        _configuration = configuration;
        _sectionPrefix = string.IsNullOrWhiteSpace(sectionPrefix) ? null : sectionPrefix;
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_configuration[Key(name)]);

    /// <inheritdoc />
    public Task<SecretValue?> GetSecretWithMetadataAsync(string name, CancellationToken cancellationToken = default) {
        var value = _configuration[Key(name)];
        return Task.FromResult(value is null ? null : new SecretValue(name, value, null, null));
    }

    private string Key(string name) => _sectionPrefix is null ? name : $"{_sectionPrefix}:{name}";
}
