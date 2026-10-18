namespace Aedis.Database.Postgres.Tenancy;

/// <summary>
///     Ponte entre o tenant atual e o PostgreSQL. Toda sessão aberta pela <see cref="UnitOfWorkFactory" />
///     dentro de um escopo de tenant grava o tenant na variável de sessão <see cref="SettingName" /> com
///     <c>set_config(…, true)</c> — local à transação da sessão, então não vaza para a próxima requisição que
///     reusar a conexão do pool. Políticas de Row-Level Security leem a variável e isolam os dados de cada
///     tenant inclusive em SQL escrito à mão (ver <c>docs/TENANCY.md</c>).
/// </summary>
public static class PostgresTenancy
{
    /// <summary>Variável de sessão com o tenant atual (<c>aedis.tenant_id</c>); vazia ou ausente quando não há tenant.</summary>
    public const string SettingName = "aedis.tenant_id";
}
