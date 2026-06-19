using System.ComponentModel.DataAnnotations;

namespace Aedis.Scheduling.Hangfire;

/// <summary>
///     Opções do scheduler Hangfire do Aedis. Lidas da seção <c>Hangfire</c>. O <see cref="SchemaName" />
///     isola as tabelas do Hangfire em um schema dedicado do PostgreSQL.
/// </summary>
public sealed class HangfireOptions
{
    /// <summary>Nome da seção de configuração de onde as opções são lidas (<c>Hangfire</c>).</summary>
    public const string SectionName = "Hangfire";

    /// <summary>Connection string do banco dedicado ao Hangfire (storage dos jobs).</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Schema do PostgreSQL onde o Hangfire cria suas tabelas. Padrão <c>hangfire</c>.</summary>
    public string SchemaName { get; set; } = "hangfire";

    /// <summary>Número de threads do Hangfire Server para processar jobs. Padrão 2.</summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>Path do dashboard (sem barra inicial). Padrão <c>jobs</c>.</summary>
    public string DashboardPath { get; set; } = "jobs";

    /// <summary>Habilita o dashboard em <c>/{DashboardPath}</c>. Padrão true.</summary>
    public bool EnableDashboard { get; set; } = true;
}
