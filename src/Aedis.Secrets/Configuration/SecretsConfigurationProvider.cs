using Aedis.Secrets.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aedis.Secrets.Configuration;

/// <summary>
///     <see cref="ConfigurationProvider" /> que, no startup, carrega segredos do cofre para o
///     <see cref="IConfiguration" /> conforme um mapa <c>chave-de-config → nome-do-segredo</c>. Permite que
///     o consumidor leia credenciais via <c>IOptions</c>/<c>IConfiguration</c> como qualquer outra
///     configuração, sem acoplar o código ao cofre. A leitura é síncrona (contrato do <c>ConfigurationProvider</c>).
/// </summary>
public sealed class SecretsConfigurationProvider : ConfigurationProvider
{
    private readonly IReadOnlyDictionary<string, string> _mappings;
    private readonly bool _optional;
    private readonly ISecretsProvider _provider;

    /// <summary>Cria o provider de configuração a partir do cofre, do mapa de chaves e da obrigatoriedade.</summary>
    /// <param name="provider">Cofre de onde os segredos são lidos.</param>
    /// <param name="mappings">Mapa <c>chave-de-config → nome-do-segredo</c>.</param>
    /// <param name="optional">Quando <c>false</c>, um segredo ausente lança no startup; quando <c>true</c>, é ignorado.</param>
    public SecretsConfigurationProvider(ISecretsProvider provider, IReadOnlyDictionary<string, string> mappings,
        bool optional) {
        _provider = provider;
        _mappings = mappings;
        _optional = optional;
    }

    /// <inheritdoc />
    public override void Load() {
        foreach (var (configKey, secretName) in _mappings) {
            var value = _provider.GetSecretAsync(secretName).GetAwaiter().GetResult();
            if (value is not null)
                Data[configKey] = value;
            else if (!_optional)
                throw new SecretNotFoundException(secretName);
        }
    }
}
