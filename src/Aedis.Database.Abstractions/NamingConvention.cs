namespace Aedis.Database.Abstractions;

/// <summary>
///     Convenção de nomes aplicada ao mapear entidades/propriedades para tabelas/colunas. Selecione-a em
///     <c>DatabaseOptions.NamingConvention</c>; o padrão é <see cref="SnakeCase" />, idiomático no PostgreSQL.
/// </summary>
public enum NamingConvention
{
    /// <summary>Minúsculas separadas por underscore (ex.: <c>created_at</c>). Idiomático no PostgreSQL.</summary>
    SnakeCase = 0,

    /// <summary>Primeira letra de cada palavra em maiúscula (ex.: <c>CreatedAt</c>).</summary>
    PascalCase = 1,

    /// <summary>Como PascalCase, mas com a primeira letra minúscula (ex.: <c>createdAt</c>).</summary>
    CamelCase = 2
}