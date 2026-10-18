using Aedis.Messaging.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary><c>AddAedisMessagingTelemetry</c> vincula <c>Messaging:Telemetry</c> às opções e aplica na instrumentação global.</summary>
[Collection("MessagingInstrumentation")]
public sealed class MessagingTelemetryRegistrationTests : IDisposable
{
    public void Dispose() => MessagingInstrumentation.Configure(new MessagingTelemetryOptions());

    [Fact]
    public void Vincula_a_secao_e_exige_servicos_e_configuracao() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Messaging:Telemetry:EnableMetrics"] = "false",
            ["Messaging:Telemetry:RecordMessageAge"] = "false"
        }).Build();

        using var provider = new ServiceCollection().AddAedisMessagingTelemetry(config).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MessagingTelemetryOptions>>().Value;
        options.EnableMetrics.Should().BeFalse();
        options.RecordMessageAge.Should().BeFalse();
        options.EnableTracing.Should().BeTrue();
        FluentActions.Invoking(() => new ServiceCollection().AddAedisMessagingTelemetry(null!)).Should().Throw<ArgumentNullException>();
    }
}
