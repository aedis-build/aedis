using Aedis.Storage.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Storage.Tests;

/// <summary>
///     Cobre a representação textual de <see cref="BucketObject" />: <c>bucket/caminho</c> com barras
///     duplicadas colapsadas, e o nome do arquivo como último segmento do caminho.
/// </summary>
public sealed class BucketObjectTests
{
    [Fact]
    public void ToString_junta_bucket_e_caminho_colapsando_barras() {
        var bucketObject = new BucketObject("invoices", "//2026//10/nota.pdf", DateTimeOffset.UnixEpoch);

        bucketObject.ToString().Should().Be("invoices/2026/10/nota.pdf");
        bucketObject.FileName.Should().Be("nota.pdf");
    }

    [Fact]
    public void Construtor_sem_data_carimba_o_instante_atual() {
        var before = DateTimeOffset.UtcNow;

        var bucketObject = new BucketObject("invoices", "nota.pdf");

        bucketObject.LastModified.Should().BeOnOrAfter(before);
        bucketObject.ToString().Should().Be("invoices/nota.pdf");
    }
}
