using System.ComponentModel.DataAnnotations;
using Aedis.Database.Abstractions;
using Aedis.Database.SqlServer;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>Tipagem dos parâmetros string: varchar por padrão, nvarchar com [Unicode] ou com o padrão invertido, tamanho das anotações.</summary>
public sealed class SqlServerStringsTests
{
    private sealed class Sample
    {
        public string Plain { get; set; } = "";
        [Unicode] public string Wide { get; set; } = "";
        [MaxLength(30)] public string Capped { get; set; } = "";
        [StringLength(12)] public string Sized { get; set; } = "";
    }

    [Fact]
    public void Varchar_por_padrao_nvarchar_com_Unicode_e_tamanho_das_anotacoes() {
        DbStringOf("Plain").IsAnsi.Should().BeTrue();
        DbStringOf("Plain").Length.Should().Be(-1);
        DbStringOf("Wide").IsAnsi.Should().BeFalse();
        DbStringOf("Capped").Length.Should().Be(30);
        DbStringOf("Sized").Length.Should().Be(12);
        SqlServerStrings.Parameter(typeof(Sample).GetProperty("Plain")!, "x", unicodeDefault: true).IsAnsi.Should().BeFalse();
    }

    private static Dapper.DbString DbStringOf(string property) =>
        SqlServerStrings.Parameter(typeof(Sample).GetProperty(property)!, "x", unicodeDefault: false);
}
