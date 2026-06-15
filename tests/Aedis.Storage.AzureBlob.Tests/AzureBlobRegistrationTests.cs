using Aedis.Storage.Abstractions;
using Aedis.Storage.AzureBlob;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Storage.AzureBlob.Tests;

/// <summary>
///     Verifica a extensão de DI <c>AddAedisAzureBlob&lt;T&gt;()</c> — registro do container e de
///     <c>IBucket&lt;T&gt;</c>. Não resolve (evita construir o cliente Azure); valida os descriptors.
/// </summary>
public sealed class AzureBlobRegistrationTests
{
    public sealed class Reports(AzureBlobStorageOptions options) : AzureBlobBucketService<Reports>(options);

    [Fact]
    public void AddAedisAzureBlob_registra_o_container_e_o_contrato() {
        var services = new ServiceCollection();

        services.AddAedisAzureBlob<Reports>(new AzureBlobStorageOptions {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "reports"
        });

        services.Should().Contain(d => d.ServiceType == typeof(Reports));
        services.Should().Contain(d => d.ServiceType == typeof(IBucket<Reports>));
    }
}
