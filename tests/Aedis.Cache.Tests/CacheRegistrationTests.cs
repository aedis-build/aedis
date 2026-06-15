using Aedis.Cache.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Aedis.Cache.Tests;

/// <summary>
///     As extensões de DI dos serviços agnósticos exigem um <see cref="ICache" /> registrado e
///     publicam os contratos certos.
/// </summary>
public sealed class CacheRegistrationTests
{
    private static ServiceCollection WithCache() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICache>());
        return services;
    }

    [Fact]
    public void AddAedisBatchCache_registra_IBatchCache() {
        var provider = WithCache().AddAedisBatchCache().BuildServiceProvider();

        provider.GetService<IBatchCache>().Should().NotBeNull();
    }

    [Fact]
    public void AddAedisExecutionCache_registra_a_factory() {
        var provider = WithCache().AddAedisExecutionCache().BuildServiceProvider();

        provider.GetService<IExecutionCacheContextFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddAedisBatchCache_sem_ICache_lanca() {
        var act = () => new ServiceCollection().AddAedisBatchCache();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICache*");
    }

    [Fact]
    public void AddAedisExecutionCache_sem_ICache_lanca() {
        var act = () => new ServiceCollection().AddAedisExecutionCache();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICache*");
    }
}
