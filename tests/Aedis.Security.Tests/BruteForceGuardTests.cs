using Aedis.Cache.Abstractions;
using Aedis.Security.Abstractions;
using Aedis.Security.BruteForce;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aedis.Security.Tests;

/// <summary>
///     Garante o <see cref="CacheBruteForceGuard" />: bloqueia ao exceder o limite de falhas, escala a
///     duração do bloqueio a cada reincidência, zera no reset e — por chavear pela credencial, não pelo IP —
///     acumula falhas da mesma credencial independentemente da origem (imune a rotação de IP).
/// </summary>
public sealed class BruteForceGuardTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (CacheBruteForceGuard Guard, FakeTime Time) Create() {
        var time = new FakeTime(Start);
        var options = Options.Create(new BruteForceOptions {
            MaxAttempts = 3,
            AttemptWindow = TimeSpan.FromMinutes(15),
            BaseLockout = TimeSpan.FromMinutes(1),
            EscalationFactor = 2.0,
            MaxLockout = TimeSpan.FromHours(1),
            EscalationWindow = TimeSpan.FromHours(12)
        });
        return (new CacheBruteForceGuard(new FakeCache(time), options, time), time);
    }

    [Fact]
    public async Task Bloqueia_ao_exceder_o_limite_de_falhas() {
        var (guard, _) = Create();

        await guard.RegisterFailureAsync("alice");
        await guard.RegisterFailureAsync("alice");
        var status = await guard.RegisterFailureAsync("alice");

        status.IsBlocked.Should().BeTrue();
        status.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        (await guard.CheckAsync("alice")).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task O_bloqueio_escala_a_cada_reincidencia() {
        var (guard, time) = Create();

        var first = await FailUntilBlockedAsync(guard, "alice");
        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        var second = await FailUntilBlockedAsync(guard, "alice");
        time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        var third = await FailUntilBlockedAsync(guard, "alice");

        first.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        second.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
        third.RetryAfter.Should().Be(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task Reset_zera_o_estado_apos_sucesso() {
        var (guard, _) = Create();
        await guard.RegisterFailureAsync("alice");
        await guard.RegisterFailureAsync("alice");

        await guard.ResetAsync("alice");

        var status = await guard.CheckAsync("alice");
        status.IsBlocked.Should().BeFalse();
        status.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Chaveia_pela_credencial_e_nao_pelo_ip() {
        var (guard, _) = Create();

        await guard.RegisterFailureAsync("alice");
        await guard.RegisterFailureAsync("alice");
        var alice = await guard.RegisterFailureAsync("alice");
        var bob = await guard.RegisterFailureAsync("bob");

        alice.IsBlocked.Should().BeTrue("3 falhas da mesma credencial bloqueiam, mesmo vindas de IPs diferentes");
        bob.IsBlocked.Should().BeFalse("outra credencial tem contagem independente");
    }

    private static async Task<BruteForceStatus> FailUntilBlockedAsync(IBruteForceGuard guard, string key) {
        BruteForceStatus status = new(false, 0, null);
        for (var attempt = 0; attempt < 3; attempt++)
            status = await guard.RegisterFailureAsync(key);

        return status;
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class FakeCache(TimeProvider time) : ICache
    {
        private readonly Dictionary<string, (string Value, DateTimeOffset ExpiresAt)> _data = new();

        public Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) {
            Purge();
            if (_data.TryGetValue(key, out var entry)) {
                var next = long.Parse(entry.Value) + 1;
                _data[key] = (next.ToString(), entry.ExpiresAt);
                return Task.FromResult(next);
            }

            _data[key] = ("1", time.GetUtcNow() + ttl);
            return Task.FromResult(1L);
        }

        public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) {
            Purge();
            return Task.FromResult(_data.TryGetValue(key, out var entry) ? entry.Value : null);
        }

        public Task SetStringAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default) {
            _data[key] = (value, time.GetUtcNow() + expiration);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_data.Remove(key));

        private void Purge() {
            var now = time.GetUtcNow();
            foreach (var key in _data.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToList())
                _data.Remove(key);
        }

        public Task<IAsyncDisposable?> IsLeaderAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RenewLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
