using System.Collections.Concurrent;

namespace Aedis.Payments.Abstractions;

/// <summary>
///     <see cref="IPaymentEventStore" /> em memória do processo. Serve a testes e a uma instância única: não sobrevive a
///     restart nem é visto por outras réplicas. Uma reserva sem conclusão expira em <see cref="ClaimTimeout" />, para
///     um processamento que morreu no meio não bloquear o reenvio para sempre.
/// </summary>
public sealed class InMemoryPaymentEventStore(TimeProvider? timeProvider = null) : IPaymentEventStore
{
    /// <summary>Prazo de uma reserva não concluída.</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (bool Completed, DateTimeOffset ClaimedAt)> _events = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(string eventKey, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        var now = _time.GetUtcNow();
        while (true) {
            if (_events.TryAdd(eventKey, (false, now))) return Task.FromResult(true);
            if (!_events.TryGetValue(eventKey, out var current)) continue;
            if (current.Completed || now - current.ClaimedAt < ClaimTimeout) return Task.FromResult(false);
            if (_events.TryUpdate(eventKey, (false, now), current)) return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string eventKey, CancellationToken cancellationToken) {
        _events[eventKey] = (true, _time.GetUtcNow());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string eventKey, CancellationToken cancellationToken) {
        _events.TryRemove(eventKey, out _);
        return Task.CompletedTask;
    }
}
