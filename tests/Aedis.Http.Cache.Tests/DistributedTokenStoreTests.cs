using Aedis.Cache.Abstractions;
using Aedis.Http.Cache;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.Http.Cache.Tests;

/// <summary>
///     Garante que o <see cref="DistributedTokenStore" /> delega ao <see cref="ICache" /> do Aedis: leitura,
///     gravação com TTL e remoção mapeiam diretamente para as operações de string do cache distribuído.
/// </summary>
public sealed class DistributedTokenStoreTests
{
    [Fact]
    public async Task Get_le_a_string_do_cache() {
        var cache = Substitute.For<ICache>();
        cache.GetStringAsync("k", Arg.Any<CancellationToken>()).Returns("tok");
        var store = new DistributedTokenStore(cache);

        (await store.GetAsync("k")).Should().Be("tok");
    }

    [Fact]
    public async Task Set_grava_a_string_com_o_ttl() {
        var cache = Substitute.For<ICache>();
        var store = new DistributedTokenStore(cache);
        var ttl = TimeSpan.FromMinutes(30);

        await store.SetAsync("k", "tok", ttl);

        await cache.Received(1).SetStringAsync("k", "tok", ttl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_apaga_a_chave_no_cache() {
        var cache = Substitute.For<ICache>();
        var store = new DistributedTokenStore(cache);

        await store.RemoveAsync("k");

        await cache.Received(1).RemoveAsync("k", Arg.Any<CancellationToken>());
    }
}
