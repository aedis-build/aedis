using Aedis.Cache.Abstractions;
using Aedis.Http.Abstractions.Authentication;
using Aedis.Http.Cache;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Http.Cache.Tests;

/// <summary>
///     Garante que o <see cref="DistributedTokenStore" /> usa o <see cref="ICache" /> do Aedis: round-trip do
///     <see cref="CachedToken" /> via envelope JSON com TTL derivado da expiração, e o lock de geração
///     delegado ao lock distribuído (<see cref="ICache.IsLeaderAsync" />).
/// </summary>
public sealed class DistributedTokenStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Le_o_token_do_envelope_no_cache() {
        var cache = Substitute.For<ICache>();
        var expiresAt = Start.AddHours(1);
        var refreshAt = Start.AddMinutes(30);
        cache.GetStringAsync("k", Arg.Any<CancellationToken>())
            .Returns($"{{\"access_token\":\"tok\",\"expires_at\":{expiresAt.ToUnixTimeMilliseconds()},\"refresh_at\":{refreshAt.ToUnixTimeMilliseconds()}}}");
        var store = new DistributedTokenStore(cache, new FixedTime(Start));

        var token = await store.GetAsync("k");

        token!.AccessToken.Should().Be("tok");
        token.ExpiresAt.Should().Be(expiresAt);
        token.RefreshAt.Should().Be(refreshAt);
    }

    [Fact]
    public async Task Grava_o_token_com_ttl_ate_a_expiracao() {
        var cache = Substitute.For<ICache>();
        var store = new DistributedTokenStore(cache, new FixedTime(Start));
        var token = new CachedToken("tok", Start.AddHours(1), Start.AddMinutes(30));

        await store.SetAsync("k", token);

        await cache.Received(1).SetStringAsync("k", Arg.Any<string>(),
            Arg.Is<TimeSpan>(ttl => ttl == TimeSpan.FromHours(1)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_apaga_a_chave_no_cache() {
        var cache = Substitute.For<ICache>();
        var store = new DistributedTokenStore(cache, new FixedTime(Start));

        await store.RemoveAsync("k");

        await cache.Received(1).RemoveAsync("k", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_de_geracao_delega_ao_lock_distribuido() {
        var cache = Substitute.For<ICache>();
        var lease = Substitute.For<IAsyncDisposable>();
        cache.IsLeaderAsync("k:refresh-lock", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(lease);
        var store = new DistributedTokenStore(cache, new FixedTime(Start));

        var result = await store.TryAcquireRefreshLockAsync("k", TimeSpan.FromSeconds(30));

        result.Should().BeSameAs(lease);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
