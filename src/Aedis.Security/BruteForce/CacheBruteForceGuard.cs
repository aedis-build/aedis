using System.Security.Cryptography;
using System.Text;
using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;
using Microsoft.Extensions.Options;

namespace Aedis.Security.BruteForce;

/// <summary>
///     Implementação de <see cref="IBruteForceGuard" /> sobre o <see cref="ICache" /> do Aedis (ex.: Redis):
///     a contagem de falhas e o bloqueio são distribuídos, valendo para toda a frota. A credencial é
///     normalizada e <strong>hasheada</strong> antes de virar chave de cache (não vaza o identificador e
///     evita injeção na chave). O bloqueio escala a cada reincidência: <c>BaseLockout × EscalationFactor^(n-1)</c>,
///     limitado por <c>MaxLockout</c>, onde <c>n</c> é o número de bloqueios lembrados na janela de escalonamento.
/// </summary>
public sealed class CacheBruteForceGuard : IBruteForceGuard
{
    private readonly ICache _cache;
    private readonly BruteForceOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Cria o guard sobre o cache distribuído registrado e as opções vinculadas.</summary>
    public CacheBruteForceGuard(ICache cache, IOptions<BruteForceOptions> options, TimeProvider? timeProvider = null) {
        _cache = cache;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<BruteForceStatus> CheckAsync(string key, CancellationToken cancellationToken = default) {
        var id = Identify(key);
        var blocked = await ReadBlockAsync(id, cancellationToken);
        var failures = await ReadFailuresAsync(id, cancellationToken);
        return new BruteForceStatus(blocked is not null, failures, blocked);
    }

    /// <inheritdoc />
    public async Task<BruteForceStatus> RegisterFailureAsync(string key, CancellationToken cancellationToken = default) {
        var id = Identify(key);

        var alreadyBlocked = await ReadBlockAsync(id, cancellationToken);
        if (alreadyBlocked is not null)
            return new BruteForceStatus(true, await ReadFailuresAsync(id, cancellationToken), alreadyBlocked);

        var failures = await _cache.IncrementAsync(FailureKey(id), _options.AttemptWindow, cancellationToken);
        if (failures < _options.MaxAttempts)
            return new BruteForceStatus(false, (int)failures, null);

        var strikes = await _cache.IncrementAsync(StrikeKey(id), _options.EscalationWindow, cancellationToken);
        var lockout = ComputeLockout(strikes);
        var until = _timeProvider.GetUtcNow() + lockout;

        await _cache.SetStringAsync(LockKey(id), until.ToUnixTimeMilliseconds().ToString(), lockout, cancellationToken);
        await _cache.RemoveAsync(FailureKey(id), cancellationToken);

        return new BruteForceStatus(true, (int)failures, lockout);
    }

    /// <inheritdoc />
    public async Task ResetAsync(string key, CancellationToken cancellationToken = default) {
        var id = Identify(key);
        await _cache.RemoveAsync(FailureKey(id), cancellationToken);
        await _cache.RemoveAsync(LockKey(id), cancellationToken);
        await _cache.RemoveAsync(StrikeKey(id), cancellationToken);
    }

    private async Task<TimeSpan?> ReadBlockAsync(string id, CancellationToken cancellationToken) {
        var value = await _cache.GetStringAsync(LockKey(id), cancellationToken);
        if (value is null || !long.TryParse(value, out var untilMs))
            return null;

        var remaining = DateTimeOffset.FromUnixTimeMilliseconds(untilMs) - _timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private async Task<int> ReadFailuresAsync(string id, CancellationToken cancellationToken) {
        var value = await _cache.GetStringAsync(FailureKey(id), cancellationToken);
        return value is not null && int.TryParse(value, out var count) ? count : 0;
    }

    private TimeSpan ComputeLockout(long strikes) {
        var multiplier = Math.Pow(_options.EscalationFactor, Math.Max(0, strikes - 1));
        var ticks = _options.BaseLockout.Ticks * multiplier;

        if (double.IsInfinity(ticks) || ticks >= _options.MaxLockout.Ticks)
            return _options.MaxLockout;

        return TimeSpan.FromTicks((long)ticks);
    }

    private string FailureKey(string id) => $"{_options.KeyPrefix}{id}:fails";
    private string LockKey(string id) => $"{_options.KeyPrefix}{id}:lock";
    private string StrikeKey(string id) => $"{_options.KeyPrefix}{id}:strikes";

    private static string Identify(string key) {
        var normalized = key.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
