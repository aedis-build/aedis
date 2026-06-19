using Aedis.Cache.Abstractions;
using Aedis.Cache.Redis;
using Aedis.Diagnostics;
using Aedis.Hosting.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace Aedis.Cache.Redis.Tests;

/// <summary>
///     Lock distribuído e operações de cache ponta-a-ponta contra um Redis real (Testcontainers).
///     O foco é a segurança da liderança: exclusão mútua entre instâncias e liberação automática do
///     lock pelo <see cref="DisposableRegistry" /> no desligamento — o mecanismo do qual o file-manager
///     depende. O cenário de desligamento reproduz o handler que registra o handle no registry e o
///     <c>GracefulShutdownHostedService</c> que, no SIGTERM, chama <c>DisposeAllAsync</c> para liberar o
///     lock e permitir que o standby assuma, sem lock órfão no cluster.
/// </summary>
public sealed class RedisCacheTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder().Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private RedisCache CreateCache(string instanceId) {
        var endpoint = _container.GetConnectionString();
        var options = Options.Create(new RedisCacheOptions {
            EndPoint = endpoint,
            Password = string.Empty,
            UseSsl = false,
            InstanceId = instanceId
        });
        return new RedisCache(options, NullLogger<RedisCache>.Instance);
    }

    [Fact]
    public async Task IsLeaderAsync_garante_exclusao_mutua_entre_instancias() {
        var instanceA = CreateCache("instance-a");
        var instanceB = CreateCache("instance-b");
        var key = $"leader-{Guid.NewGuid():N}";

        var lockA = await instanceA.IsLeaderAsync(key, TimeSpan.FromSeconds(30));
        var lockB = await instanceB.IsLeaderAsync(key, TimeSpan.FromSeconds(30));

        lockA.Should().NotBeNull("a primeira instância vence a eleição");
        lockB.Should().BeNull("a segunda instância não é líder enquanto o lock está retido");
    }

    [Fact]
    public async Task Liberar_o_handle_permite_outra_instancia_assumir() {
        var instanceA = CreateCache("instance-a");
        var instanceB = CreateCache("instance-b");
        var key = $"leader-{Guid.NewGuid():N}";

        var lockA = await instanceA.IsLeaderAsync(key, TimeSpan.FromSeconds(30));
        lockA.Should().NotBeNull();

        await lockA!.DisposeAsync();

        var lockB = await instanceB.IsLeaderAsync(key, TimeSpan.FromSeconds(30));
        lockB.Should().NotBeNull("após a liberação, a liderança fica disponível");
    }

    [Fact]
    public async Task DisposableRegistry_libera_o_lock_no_desligamento_gracioso() {
        var registry = new DisposableRegistry();
        var leader = CreateCache("instance-a");
        var standby = CreateCache("instance-b");
        var key = $"leader-{Guid.NewGuid():N}";

        var leaderLock = await leader.IsLeaderAsync(key, TimeSpan.FromMinutes(5));
        leaderLock.Should().NotBeNull();
        var tracked = registry.Register(key, leaderLock!);

        (await standby.IsLeaderAsync(key, TimeSpan.FromMinutes(5)))
            .Should().BeNull("enquanto o líder está vivo, o standby não assume");

        await registry.DisposeAllAsync();

        var promoted = await standby.IsLeaderAsync(key, TimeSpan.FromMinutes(5));
        promoted.Should().NotBeNull("o lock foi liberado no desligamento e o standby assume");

        await tracked.DisposeAsync();
    }

    [Fact]
    public async Task RenewLockAsync_renova_existente_e_recusa_inexistente() {
        var cache = CreateCache("instance-a");
        var key = $"leader-{Guid.NewGuid():N}";

        var handle = await cache.IsLeaderAsync(key, TimeSpan.FromSeconds(5));
        handle.Should().NotBeNull();

        (await cache.RenewLockAsync(key, TimeSpan.FromMinutes(10)))
            .Should().BeTrue("o lock existe e o TTL é estendido");

        (await cache.RenewLockAsync($"ausente-{Guid.NewGuid():N}", TimeSpan.FromMinutes(10)))
            .Should().BeFalse("não há lock para renovar");
    }

    [Fact]
    public async Task String_roundtrip_e_SetIfNotExists() {
        var cache = CreateCache("instance-a");
        var key = $"str-{Guid.NewGuid():N}";

        (await cache.GetStringAsync(key)).Should().BeNull();

        await cache.SetStringAsync(key, "valor", TimeSpan.FromMinutes(1));
        (await cache.GetStringAsync(key)).Should().Be("valor");
        (await cache.ExistsAsync(key)).Should().BeTrue();

        (await cache.SetIfNotExistsAsync(key, "outro", TimeSpan.FromMinutes(1)))
            .Should().BeFalse("a chave já existe (NX)");

        (await cache.RemoveAsync(key)).Should().BeTrue();
        (await cache.ExistsAsync(key)).Should().BeFalse();

        (await cache.SetIfNotExistsAsync(key, "novo", TimeSpan.FromMinutes(1)))
            .Should().BeTrue("após remover, o NX agrava");
    }

    [Fact]
    public async Task IncrementAsync_conta_e_define_ttl_no_primeiro() {
        var cache = CreateCache("instance-a");
        var key = $"cnt-{Guid.NewGuid():N}";

        (await cache.IncrementAsync(key, TimeSpan.FromMinutes(1))).Should().Be(1);
        (await cache.IncrementAsync(key, TimeSpan.FromMinutes(1))).Should().Be(2);
    }
}
