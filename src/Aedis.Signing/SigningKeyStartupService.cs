using Aedis.Exceptions;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aedis.Signing;

/// <summary>
///     Bootstrap da chave de assinatura na subida: insiste em erro transitório do provider (com backoff) até
///     <see cref="SigningOptions.BootstrapTimeoutSeconds" />; erro permanente aborta o host. Enquanto não termina,
///     o health check <c>signing</c> (tag <c>ready</c>) segura o tráfego.
/// </summary>
public sealed class SigningKeyStartupService(
    ISigningKeyProvider keyProvider,
    SigningKeyState state,
    SigningOptions options,
    ILogger<SigningKeyStartupService> logger,
    TimeProvider? timeProvider = null)
    : IHostedService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) {
        var timeout = TimeSpan.FromSeconds(options.BootstrapTimeoutSeconds);
        var started = _timeProvider.GetTimestamp();
        var delay = TimeSpan.FromSeconds(1);

        while (true) {
            try {
                var handle = await keyProvider.EnsureKeyAsync(cancellationToken);
                logger.LogDebug("Bootstrap da chave de assinatura concluído (kid {KeyId}, {Algorithm}).", handle.KeyId, handle.Algorithm);
                return;
            }
            catch (ServiceTemporarilyUnavailableException ex) when (_timeProvider.GetElapsedTime(started) + delay < timeout) {
                logger.LogWarning(ex, "Bootstrap da chave de assinatura: falha transitória; nova tentativa em {Delay}.", delay);
                await Task.Delay(delay, _timeProvider, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                state.SetFaulted(ex);
                logger.LogCritical(ex, "Bootstrap da chave de assinatura falhou; o host não pode subir sem a chave.");
                throw new InvalidOperationException("O bootstrap da chave de assinatura falhou; veja a exceção interna.", ex);
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
