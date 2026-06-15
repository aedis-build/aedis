using Aedis.Storage.Abstractions;
using Aedis.Storage.S3;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aedis.Storage.S3.Tests;

/// <summary>
///     Verifica a extensão de DI <c>AddAedisS3&lt;T&gt;()</c> — registro do bucket e de <c>IBucket&lt;T&gt;</c>.
///     Não resolve (evita construir o cliente AWS); valida os descriptors.
/// </summary>
public sealed class S3RegistrationTests
{
    public sealed class Invoices(S3StorageOptions options) : S3BucketService<Invoices>(options);

    [Fact]
    public void AddAedisS3_registra_o_bucket_e_o_contrato() {
        var services = new ServiceCollection();

        services.AddAedisS3<Invoices>(new S3StorageOptions { BucketName = "invoices" });

        services.Should().Contain(d => d.ServiceType == typeof(Invoices));
        services.Should().Contain(d => d.ServiceType == typeof(IBucket<Invoices>));
    }
}
