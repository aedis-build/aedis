using Aedis.Secrets;
using Aedis.Secrets.Abstractions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Secrets.AwsSecretsManager.Tests;

/// <summary>
///     Escrita de segredos: o <see cref="CacheInvalidatingSecretsWriter" /> invalida o cache após gravar ou
///     remover, o registro de DI expõe <see cref="ISecretsWriter" /> decorado quando o provider escreve, e o
///     health check por canário prova conectividade com <c>DescribeSecret</c> — nunca <c>ListSecrets</c>.
/// </summary>
public sealed class SecretsWriterTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AwsSecretsManager:Region"] = "us-east-1" }).Build();

    [Fact]
    public async Task Writer_invalida_o_cache_apos_gravar() {
        var inner = Substitute.For<ISecretsProvider, ISecretsWriter>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("k", "v1", null, null), new SecretValue("k", "v2", null, null));
        var cache = new CachingSecretsProvider(inner, TimeSpan.FromHours(1));
        var writer = new CacheInvalidatingSecretsWriter((ISecretsWriter)inner, cache);

        (await cache.GetSecretAsync("k")).Should().Be("v1");
        await writer.SetSecretAsync("k", "v2");

        (await cache.GetSecretAsync("k")).Should().Be("v2");
        await ((ISecretsWriter)inner).Received(1).SetSecretAsync("k", "v2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writer_invalida_o_cache_apos_remover() {
        var inner = Substitute.For<ISecretsProvider, ISecretsWriter>();
        inner.GetSecretWithMetadataAsync("k", Arg.Any<CancellationToken>())
            .Returns(new SecretValue("k", "v1", null, null), (SecretValue?)null);
        var cache = new CachingSecretsProvider(inner, TimeSpan.FromHours(1));
        var writer = new CacheInvalidatingSecretsWriter((ISecretsWriter)inner, cache);

        await cache.GetSecretAsync("k");
        await writer.DeleteSecretAsync("k");

        (await cache.GetSecretAsync("k")).Should().BeNull();
    }

    [Fact]
    public void Registro_expoe_writer_decorado_quando_o_cache_esta_ligado() {
        var provider = new ServiceCollection().AddLogging().AddAedisAwsSecretsManager(Config()).BuildServiceProvider();

        provider.GetRequiredService<ISecretsWriter>().Should().BeOfType<CacheInvalidatingSecretsWriter>();
    }

    [Fact]
    public void Registro_expoe_o_writer_cru_quando_o_cache_esta_desligado() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AwsSecretsManager:Region"] = "us-east-1",
            ["Secrets:CacheEnabled"] = "false"
        }).Build();

        var provider = new ServiceCollection().AddLogging().AddAedisAwsSecretsManager(config).BuildServiceProvider();

        provider.GetRequiredService<ISecretsWriter>().Should().BeOfType<AwsSecretsManagerProvider>();
    }

    [Fact]
    public async Task Health_check_sem_canario_configurado_trata_nao_encontrado_como_saudavel() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.DescribeSecretAsync(Arg.Any<DescribeSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns<DescribeSecretResponse>(_ => throw new ResourceNotFoundException("não existe"));
        var check = new AwsSecretsManagerHealthCheck(client, Options.Create(new AwsSecretsManagerOptions()),
            NullLogger<AwsSecretsManagerHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        await client.DidNotReceive().ListSecretsAsync(Arg.Any<ListSecretsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Health_check_com_canario_configurado_exige_que_ele_exista() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.DescribeSecretAsync(Arg.Any<DescribeSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns<DescribeSecretResponse>(_ => throw new ResourceNotFoundException("não existe"));
        var check = new AwsSecretsManagerHealthCheck(client,
            Options.Create(new AwsSecretsManagerOptions { HealthCheckSecretName = "app/canary" }),
            NullLogger<AwsSecretsManagerHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        await client.Received(1).DescribeSecretAsync(Arg.Is<DescribeSecretRequest>(r => r.SecretId == "app/canary"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Health_check_sem_acesso_e_unhealthy() {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.DescribeSecretAsync(Arg.Any<DescribeSecretRequest>(), Arg.Any<CancellationToken>())
            .Returns<DescribeSecretResponse>(_ => throw new AmazonSecretsManagerException("AccessDenied"));
        var check = new AwsSecretsManagerHealthCheck(client, Options.Create(new AwsSecretsManagerOptions()),
            NullLogger<AwsSecretsManagerHealthCheck>.Instance);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);
    }
}
