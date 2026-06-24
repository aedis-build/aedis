using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Testcontainers.LocalStack;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     Leitura de segredos contra um AWS Secrets Manager real emulado pelo LocalStack (Testcontainers).
///     Prova o caminho de produção — cliente construído pela <strong>cadeia de credenciais padrão da AWS</strong>
///     (variáveis de ambiente), sem chaves explícitas nas opções —, os metadados (VersionId), o <c>null</c>
///     em segredo inexistente, o lançamento do required e a fonte de <see cref="IConfiguration" />. Integração
///     opt-in: ligue com a env <c>AEDIS_AWS_IT=1</c>.
/// </summary>
public sealed class AwsSecretsManagerIntegrationTests : IClassFixture<AwsSecretsManagerIntegrationTests.LocalStackFixture>
{
    private readonly LocalStackFixture _fixture;

    public AwsSecretsManagerIntegrationTests(LocalStackFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Le_segredo_e_metadados() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_AWS_IT=1 para rodar a integração LocalStack.");
        var name = $"aedis/test/{Guid.NewGuid():N}";
        await _fixture.CreateSecretAsync(name, "s3cr3t-value");
        var provider = _fixture.Provider();

        (await provider.GetSecretAsync(name)).Should().Be("s3cr3t-value");

        var metadata = await provider.GetSecretWithMetadataAsync(name);
        metadata.Should().NotBeNull();
        metadata!.Value.Should().Be("s3cr3t-value");
        metadata.Version.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Segredo_inexistente_devolve_null_e_required_lanca() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_AWS_IT=1 para rodar a integração LocalStack.");
        var provider = _fixture.Provider();
        var missing = $"aedis/missing/{Guid.NewGuid():N}";

        (await provider.GetSecretAsync(missing)).Should().BeNull();

        var act = () => provider.GetRequiredSecretAsync(missing);
        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    [SkippableFact]
    public async Task Config_source_carrega_o_segredo_no_iconfiguration() {
        Skip.IfNot(_fixture.Enabled, "Defina AEDIS_AWS_IT=1 para rodar a integração LocalStack.");
        var name = $"aedis/conn/{Guid.NewGuid():N}";
        await _fixture.CreateSecretAsync(name, "Host=db;Pwd=p");

        var config = new ConfigurationBuilder()
            .AddAedisSecrets(_fixture.Provider(), new Dictionary<string, string> {
                ["Database:ConnectionString"] = name
            }).Build();

        config["Database:ConnectionString"].Should().Be("Host=db;Pwd=p");
    }

    public sealed class LocalStackFixture : IAsyncLifetime
    {
        private LocalStackContainer? _container;

        /// <summary>Integração opt-in (LocalStack é pesado): liga com a env <c>AEDIS_AWS_IT=1</c>.</summary>
        public bool Enabled { get; } = Environment.GetEnvironmentVariable("AEDIS_AWS_IT") == "1";

        public async Task InitializeAsync() {
            if (!Enabled) return;
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", "us-east-1");
            _container = new LocalStackBuilder().WithImage("localstack/localstack:3.8.1").Build();
            await _container.StartAsync();
        }

        public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

        public AwsSecretsManagerProvider Provider() => AwsSecretsManagerProvider.Create(BuildOptions());

        public async Task CreateSecretAsync(string name, string value) {
            var config = new AmazonSecretsManagerConfig {
                ServiceURL = _container!.GetConnectionString(),
                AuthenticationRegion = "us-east-1"
            };
            using var client = new AmazonSecretsManagerClient(config);
            await client.CreateSecretAsync(new CreateSecretRequest { Name = name, SecretString = value });
        }

        private AwsSecretsManagerOptions BuildOptions() => new() {
            ServiceUrl = _container!.GetConnectionString(),
            Region = "us-east-1"
        };
    }
}
