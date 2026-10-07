using Aedis.Signing.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aedis.Signing;

/// <summary>
///     Readiness da assinatura: Unhealthy enquanto a chave não foi resolvida, se o bootstrap falhou ou se a
///     chave foi rejeitada em runtime. Com <c>Signing:HealthCheck:ProbeProvider</c>, consulta o
///     <see cref="ISigningKeyProbe" /> do provider (quando registrado) sobre a chave específica — nunca lista
///     chaves.
/// </summary>
public sealed class SigningKeyHealthCheck(
    SigningKeyState state,
    SigningOptions options,
    IServiceProvider serviceProvider,
    ILogger<SigningKeyHealthCheck> logger)
    : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        if (!options.Enabled) return HealthCheckResult.Healthy("Assinatura desligada.");
        if (!options.HealthCheck.Enabled) return HealthCheckResult.Healthy("Health check da assinatura desligado.");

        if (state.Fault is { } fault)
            return HealthCheckResult.Unhealthy("O bootstrap da chave de assinatura falhou.", fault);

        if (!state.TryGet(out var handle) || handle is null)
            return HealthCheckResult.Unhealthy("Chave de assinatura ainda não resolvida.");

        if (state.Degraded is { } degraded)
            return HealthCheckResult.Unhealthy($"Chave de assinatura {handle.KeyId} rejeitada em runtime.", degraded);

        if (!options.HealthCheck.ProbeProvider || serviceProvider.GetService<ISigningKeyProbe>() is not { } probe)
            return HealthCheckResult.Healthy($"Chave de assinatura pronta (kid {handle.KeyId}).");

        try {
            var result = await probe.ProbeAsync(handle, cancellationToken);
            return result.IsUsable
                ? HealthCheckResult.Healthy($"Chave de assinatura pronta (kid {handle.KeyId}; {result.Description}).")
                : HealthCheckResult.Unhealthy($"Chave de assinatura {handle.KeyId}: {result.Description}.");
        }
        catch (Exception ex) {
            logger.LogError(ex, "A sondagem da chave de assinatura falhou.");
            return HealthCheckResult.Unhealthy("A sondagem da chave de assinatura falhou.", ex);
        }
    }
}
