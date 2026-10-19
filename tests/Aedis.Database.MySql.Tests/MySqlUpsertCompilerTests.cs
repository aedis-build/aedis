using Aedis.Database.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Database.MySql.Tests;

/// <summary>
///     Compilação determinística da <see cref="UpsertSpec" /> para o dialeto MySQL: sem guarda, tudo cabe
///     no sufixo <c>ON DUPLICATE KEY UPDATE</c> com o alias <c>new</c>; com guarda, a atualização vira um
///     <c>UPDATE … WHERE guarda</c> (parâmetros no Save, <c>JOIN</c> na staging do bulk) e o sufixo é no-op;
///     spec sem nada a atualizar também é no-op.
/// </summary>
public sealed class MySqlUpsertCompilerTests
{
    private static readonly string[] Properties = ["Id", "Value", "ObservedAt", "CreatedAt", "UpdatedAt", "IsDeleted"];

    private static string Col(string property) => string.Concat(property.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    [Fact]
    public void Sem_guarda_atualiza_pelo_alias_new_com_valor_do_servidor_e_preservadas_fora() {
        var spec = UpsertSpec.OnKey("Id").SetServerUtcNow("UpdatedAt").Preserve("CreatedAt");

        MySqlUpsertCompiler.OnDuplicateKeyUpdate(spec, "metrics", Properties, Col).Should().Be(
            "ON DUPLICATE KEY UPDATE value = new.value, observed_at = new.observed_at, updated_at = UTC_TIMESTAMP(6), is_deleted = new.is_deleted");
        MySqlUpsertCompiler.GuardedUpdate(spec, "metrics", Properties, Col, p => "@" + p, null).Should().BeNull();
    }

    [Fact]
    public void DoNothing_ou_nada_a_atualizar_gera_sufixo_noop_na_chave() {
        var doNothing = UpsertSpec.OnKey("Id").DoNothingOnConflict();
        var allPreserved = UpsertSpec.OnKey("Id").Preserve("Value").When(g => g.Newer("Value"));

        MySqlUpsertCompiler.OnDuplicateKeyUpdate(doNothing, "t", ["Id", "Value"], Col).Should().Be("ON DUPLICATE KEY UPDATE id = t.id");
        MySqlUpsertCompiler.GuardedUpdate(doNothing, "t", ["Id", "Value"], Col, p => "@" + p, null).Should().BeNull();
        MySqlUpsertCompiler.OnDuplicateKeyUpdate(allPreserved, "t", ["Id", "Value"], Col).Should().Be("ON DUPLICATE KEY UPDATE id = t.id");
        MySqlUpsertCompiler.GuardedUpdate(allPreserved, "t", ["Id", "Value"], Col, p => "@" + p, null).Should().BeNull();
    }

    [Fact]
    public void Com_guarda_o_update_e_separado_e_o_sufixo_e_noop() {
        var spec = UpsertSpec.OnKey("Id").Preserve("CreatedAt").SetServerUtcNow("UpdatedAt")
            .When(g => g.Newer("ObservedAt").OrGreater("Value").AndNotDeleted().AndExistingTrue("IsDeleted"));

        MySqlUpsertCompiler.OnDuplicateKeyUpdate(spec, "metrics", Properties, Col).Should().Be("ON DUPLICATE KEY UPDATE id = metrics.id");

        MySqlUpsertCompiler.GuardedUpdate(spec, "metrics", Properties, Col, p => "@" + p, null).Should().Be(
            "UPDATE metrics t SET t.value = @Value, t.observed_at = @ObservedAt, t.updated_at = UTC_TIMESTAMP(6), t.is_deleted = @IsDeleted"
            + " WHERE t.id = @Id AND ((t.observed_at IS NULL OR @ObservedAt >= t.observed_at) OR (t.value IS NULL OR @Value > t.value))"
            + " AND t.is_deleted = false AND t.is_deleted = true");

        MySqlUpsertCompiler.GuardedUpdate(spec, "metrics", Properties, Col, p => "s." + Col(p), "stg").Should().Be(
            "UPDATE metrics t JOIN stg s ON t.id = s.id SET t.value = s.value, t.observed_at = s.observed_at, t.updated_at = UTC_TIMESTAMP(6), t.is_deleted = s.is_deleted"
            + " WHERE ((t.observed_at IS NULL OR s.observed_at >= t.observed_at) OR (t.value IS NULL OR s.value > t.value))"
            + " AND t.is_deleted = false AND t.is_deleted = true");
    }

    [Fact]
    public void Guarda_so_booleana_e_chave_composta() {
        var spec = UpsertSpec.OnKey("Id", "Value").When(g => g.AndNotDeleted());

        MySqlUpsertCompiler.GuardedUpdate(spec, "m", Properties, Col, p => "@" + p, null).Should().Be(
            "UPDATE m t SET t.observed_at = @ObservedAt, t.created_at = @CreatedAt, t.updated_at = @UpdatedAt, t.is_deleted = @IsDeleted"
            + " WHERE t.id = @Id AND t.value = @Value AND t.is_deleted = false");
    }
}
