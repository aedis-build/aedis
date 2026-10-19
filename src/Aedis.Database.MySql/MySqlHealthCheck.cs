using Aedis.Database.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aedis.Database.MySql;

/// <summary>
///     Health check de <em>readiness</em> do MySQL: executa <c>SELECT 1</c> em uma sessão de leitura e
///     <strong>cacheia o resultado</strong> por <see cref="DatabaseOptions.HealthCheckCacheTtl" /> — o banco é
///     sondado no máximo uma vez por intervalo, independentemente da frequência com que probes do Kubernetes
///     ou o HealthCheckPublisher invocam o check. Sondagem acima de
///     <see cref="DatabaseOptions.HealthCheckTimeout" /> responde <c>Degraded</c>.
/// </summary>
public sealed class MySqlHealthCheck : IHealthCheck
{
    private readonly IUnitOfWorkFactory _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<MySqlHealthCheck> _logger;
    private readonly DatabaseOptions _options;

    private (DateTime At, HealthCheckResult Result)? _cached;

    /// <summary>Cria o health check com a fábrica de sessões, as opções (que definem o TTL do cache) e o logger.</summary>
    /// <param name="factory">Fábrica usada para abrir a sessão de leitura sondada pelo <c>SELECT 1</c>.</param>
    /// <param name="options">Opções do provider; fornece o <see cref="DatabaseOptions.HealthCheckCacheTtl" />.</param>
    /// <param name="logger">Logger para registrar falhas de sondagem.</param>
    public MySqlHealthCheck(IUnitOfWorkFactory factory, IOptions<DatabaseOptions> options,
        ILogger<MySqlHealthCheck> logger) {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) {
        if (TryGetFresh(out var cached))
            return cached;

        await _lock.WaitAsync(cancellationToken);
        try {
            if (TryGetFresh(out var stillFresh))
                return stillFresh;

            var result = await ProbeAsync(cancellationToken);
            _cached = (DateTime.UtcNow, result);
            return result;
        }
        finally {
            _lock.Release();
        }
    }

    private bool TryGetFresh(out HealthCheckResult result) {
        if (_cached is { } c && DateTime.UtcNow - c.At < _options.HealthCheckCacheTtl) {
            result = c.Result;
            return true;
        }

        result = default;
        return false;
    }

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) {
        using var timeout = new CancellationTokenSource(_options.HealthCheckTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var ct = linked.Token;
        try {
            await using var session = await _factory.CreateReadSessionAsync(ct);
            var value = await session.QuerySingleOrDefaultAsync<int>("SELECT 1", null, ct);

            return value == 1
                ? HealthCheckResult.Healthy("Conexão MySQL saudável.")
                : HealthCheckResult.Unhealthy("Resposta inesperada do MySQL.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            _logger.LogWarning("Health check do MySQL excedeu {Timeout}.", _options.HealthCheckTimeout);
            return HealthCheckResult.Degraded($"Sondagem do MySQL excedeu {_options.HealthCheckTimeout}.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Health check do MySQL falhou.");
            return HealthCheckResult.Unhealthy("Conexão MySQL indisponível.", ex);
        }
    }
}
