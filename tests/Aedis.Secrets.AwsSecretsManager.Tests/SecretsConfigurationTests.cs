using Aedis.Secrets.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     Fonte de <see cref="IConfiguration" /> baseada em cofre (sem AWS): carrega segredos no
///     <c>IConfiguration</c> pelo mapa chave→nome, lança no build se um segredo obrigatório falta e ignora
///     ausências quando opcional.
/// </summary>
public sealed class SecretsConfigurationTests
{
    [Fact]
    public void Carrega_segredos_no_iconfiguration() {
        var provider = new FakeProvider(new Dictionary<string, string> { ["db/conn"] = "Host=x", ["api/key"] = "abc" });

        var config = new ConfigurationBuilder()
            .AddAedisSecrets(provider, new Dictionary<string, string> {
                ["Database:ConnectionString"] = "db/conn",
                ["Api:Key"] = "api/key"
            }).Build();

        config["Database:ConnectionString"].Should().Be("Host=x");
        config["Api:Key"].Should().Be("abc");
    }

    [Fact]
    public void Segredo_ausente_obrigatorio_lanca_no_build() {
        var provider = new FakeProvider(new Dictionary<string, string>());

        var act = () => new ConfigurationBuilder()
            .AddAedisSecrets(provider, new Dictionary<string, string> { ["X"] = "missing" })
            .Build();

        act.Should().Throw<SecretNotFoundException>().Which.SecretName.Should().Be("missing");
    }

    [Fact]
    public void Segredo_ausente_opcional_e_ignorado() {
        var provider = new FakeProvider(new Dictionary<string, string>());

        var config = new ConfigurationBuilder()
            .AddAedisSecrets(provider, new Dictionary<string, string> { ["X"] = "missing" }, optional: true)
            .Build();

        config["X"].Should().BeNull();
    }

    private sealed class FakeProvider(Dictionary<string, string> secrets) : ISecretsProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(secrets.TryGetValue(name, out var value) ? value : null);

        public Task<SecretValue?> GetSecretWithMetadataAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(secrets.TryGetValue(name, out var value) ? new SecretValue(name, value, null, null) : null);
    }
}
