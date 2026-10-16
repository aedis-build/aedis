using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Secrets.AzureKeyVault.Tests;

/// <summary>Health check do Key Vault: canário padrão (404 é saudável), canário configurado (404 é falha) e erros.</summary>
public sealed class AzureKeyVaultHealthCheckTests
{
    private static AzureKeyVaultHealthCheck Create(SecretClient client, string? configured = null) =>
        new(client, Options.Create(new AzureKeyVaultOptions { HealthCheckSecretName = configured }),
            NullLogger<AzureKeyVaultHealthCheck>.Instance);

    private static SecretClient ClientThat(Exception failure) {
        var client = Substitute.For<SecretClient>();
        client.GetSecretAsync("qualquer").ThrowsAsyncForAnyArgs(failure);
        return client;
    }

    [Fact]
    public async Task Segredo_encontrado_e_saudavel() {
        var secret = Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties("x"), "v"),
            Substitute.For<Response>());
        var client = Substitute.For<SecretClient>();
        client.GetSecretAsync("qualquer").ReturnsForAnyArgs(Task.FromResult(secret));

        var result = await Create(client).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Canario_padrao_ausente_continua_saudavel_mas_configurado_ausente_falha() {
        var client = ClientThat(new RequestFailedException(404, "not found"));

        (await Create(client).CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
        (await Create(client, "meu-canario").CheckHealthAsync(new HealthCheckContext())).Status
            .Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Outros_erros_sao_unhealthy() {
        var client = ClientThat(new RequestFailedException(403, "forbidden"));

        var result = await Create(client).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<RequestFailedException>();
    }
}
