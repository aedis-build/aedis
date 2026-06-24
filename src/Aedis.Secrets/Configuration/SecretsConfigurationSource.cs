using Aedis.Secrets.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aedis.Secrets.Configuration;

/// <summary>Fonte de configuração que materializa um <see cref="SecretsConfigurationProvider" /> sobre um cofre.</summary>
public sealed class SecretsConfigurationSource : IConfigurationSource
{
    private readonly IReadOnlyDictionary<string, string> _mappings;
    private readonly bool _optional;
    private readonly ISecretsProvider _provider;

    /// <summary>Cria a fonte a partir do cofre, do mapa <c>chave-de-config → nome-do-segredo</c> e da obrigatoriedade.</summary>
    public SecretsConfigurationSource(ISecretsProvider provider, IReadOnlyDictionary<string, string> mappings,
        bool optional) {
        _provider = provider;
        _mappings = mappings;
        _optional = optional;
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretsConfigurationProvider(_provider, _mappings, _optional);
}
