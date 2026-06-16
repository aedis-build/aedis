using Aedis.Database.Abstractions;

namespace Aedis.Database.Postgres;

/// <summary>
///     Opções do provider PostgreSQL do Aedis. Lidas da seção <c>Database</c> da configuração. Suporta
///     endpoint de escrita (primário) e leitura (réplicas) separados; sem eles, usa
///     <see cref="ConnectionString" /> para ambos.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Connection string única (usada quando write/read não são informados separadamente).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Endpoint de escrita (primário). Tem precedência sobre <see cref="ConnectionString" /> na sessão de escrita.</summary>
    public string? WriteConnectionString { get; set; }

    /// <summary>Endpoints de leitura (réplicas), rotacionados em round-robin nas sessões de leitura.</summary>
    public string[]? ReadConnectionStrings { get; set; }

    /// <summary>Convenção de nomes para tabelas/colunas. Padrão snake_case (idiomático no PostgreSQL).</summary>
    public NamingConvention NamingConvention { get; set; } = NamingConvention.SnakeCase;

    public int WritePoolSize { get; set; } = 50;
    public int ReadPoolSize { get; set; } = 150;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>TTL do cache do health check — o banco é sondado no máximo uma vez por intervalo.</summary>
    public TimeSpan HealthCheckCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Tamanho de cada chunk em <c>BulkInsertChunkedAsync</c>. Otimizado para Aurora PostgreSQL:
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
