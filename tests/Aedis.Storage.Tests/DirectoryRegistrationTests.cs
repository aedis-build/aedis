using Aedis.Storage.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     Verifica a extensão de DI <c>AddAedisDirectory()</c> do provider FileSystem default.
/// </summary>
public sealed class DirectoryRegistrationTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "aedis-di", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddAedisDirectory_registra_IDirectory() {
        var services = new ServiceCollection();

        services.AddAedisDirectory(TempPath());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDirectory>().Should().BeOfType<DirectoryService>();
    }

    [Fact]
    public void AddAedisDirectory_keyed_registra_uma_pasta_por_chave() {
        var services = new ServiceCollection();

        services.AddAedisDirectory("invoices", TempPath());
        services.AddAedisDirectory("reports", TempPath());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IDirectory>("invoices").Should().BeOfType<DirectoryService>();
        provider.GetRequiredKeyedService<IDirectory>("reports").Should().BeOfType<DirectoryService>();
    }
}
