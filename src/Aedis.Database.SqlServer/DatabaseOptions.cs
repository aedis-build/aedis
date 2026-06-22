using Aedis.Database.Abstractions;

namespace Aedis.Database.SqlServer;

/// <summary>
///     Opções do provider SQL Server do Aedis. Lidas da seção <c>Database</c> da configuração. Suporta
///     endpoint de escrita (primário) e leitura (réplicas) separados; sem eles, usa
///     <see cref="ConnectionString" /> para ambos.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Nome da seção de configuração de onde estas opções são vinculadas (<c>"Database"</c>).</summary>
    public const string SectionName = "Database";

    /// <summary>Connection string única (usada quando write/read não são informados separadamente).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Endpoint de escrita (primário). Tem precedência sobre <see cref="ConnectionString" /> na sessão de escrita.</summary>
    public string? WriteConnectionString { get; set; }

    /// <summary>Endpoints de leitura (réplicas), rotacionados em round-robin nas sessões de leitura.</summary>
    public string[]? ReadConnectionStrings { get; set; }

    /// <summary>Convenção de nomes para tabelas/colunas. Padrão snake_case.</summary>
    public NamingConvention NamingConvention { get; set; } = NamingConvention.SnakeCase;

    /// <summary>Tamanho máximo do pool de conexões do endpoint de escrita (primário). Padrão 50.</summary>
    public int WritePoolSize { get; set; } = 50;

    /// <summary>Tamanho máximo do pool de conexões dos endpoints de leitura (réplicas). Padrão 150.</summary>
    public int ReadPoolSize { get; set; } = 150;

    /// <summary>Tempo máximo de espera para abrir uma conexão antes de falhar. Padrão 30 segundos.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tempo máximo de execução de um comando SQL antes de falhar. Padrão 60 segundos.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>TTL do cache do health check — o banco é sondado no máximo uma vez por intervalo.</summary>
    public TimeSpan HealthCheckCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Tamanho de cada chunk em <c>BulkInsertChunkedAsync</c>. Otimizado para cargas grandes:
    ///     cria a tabela temporária uma única vez e a reutiliza com TRUNCATE entre chunks, reduzindo
    ///     overhead de catálogo e I/O distribuído. Padrão 2.000 linhas.
    /// </summary>
    public int BulkInsertChunkSize { get; set; } = 2_000;

    /// <summary>
    ///     Ator gravado em CreatedBy/UpdatedBy/DeletedBy quando o <c>IAuditContext</c> não tem usuário
    ///     logado (<c>CurrentActor == null</c>). Dá visibilidade de "ação não atribuída a um usuário
    ///     autenticado" em vez de deixar a coluna nula/vazia. Padrão <c>"system"</c> (use ex.: "anonymous").
    /// </summary>
    public string DefaultAuditActor { get; set; } = "system";
}
