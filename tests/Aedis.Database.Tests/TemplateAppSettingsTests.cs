using System.Text.Json;
using Aedis.Database.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.Tests;

/// <summary>
///     Os <c>appsettings*.json</c> dos templates só podem trazer em <c>Database:NamingConvention</c> um valor que
///     o binder converte para <see cref="NamingConvention" /> — senão o app gerado quebra ao ler as opções.
/// </summary>
public sealed class TemplateAppSettingsTests
{
    [Fact]
    public void NamingConvention_dos_templates_e_valor_valido_do_enum() {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Aedis.slnx")))
            root = root.Parent ?? throw new InvalidOperationException("Raiz do repositório (Aedis.slnx) não encontrada.");

        var files = Directory.GetFiles(Path.Combine(root.FullName, "src", "Aedis.Templates", "templates"),
            "appsettings*.json", SearchOption.AllDirectories);

        var values = files
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .Where(json => json.TryGetProperty("Database", out var db) && db.TryGetProperty("NamingConvention", out _))
            .Select(json => json.GetProperty("Database").GetProperty("NamingConvention").GetString())
            .ToList();

        values.Should().NotBeEmpty();
        values.Where(v => !Enum.TryParse<NamingConvention>(v, true, out _)).Should().BeEmpty();
    }
}
