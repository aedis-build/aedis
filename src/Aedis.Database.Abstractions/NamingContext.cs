using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

/// <summary>
///     Descreve o que converter/validar (a <see cref="NamingOperation" />), o nome de entrada e a
///     <see cref="NamingConvention" /> alvo. É o argumento passado às <see cref="INamingStrategy" />. Prefira
///     os métodos de fábrica (<see cref="ForTable" />, <see cref="ForColumn" /> etc.) a construir o contexto
///     manualmente.
/// </summary>
public class NamingContext
{
    /// <summary>Convenção de nomes alvo da conversão/validação.</summary>
    public NamingConvention Convention { get; init; }

    /// <summary>Nome de entrada a converter ou validar (entidade, propriedade, tabela…).</summary>
    public string Input { get; init; } = string.Empty;

    /// <summary>Tipo de identificador sendo tratado (tabela, coluna, índice, constraint, validação).</summary>
    public NamingOperation Operation { get; init; }

    /// <summary>Parâmetros extras da operação (ex.: colunas que compõem um índice ou constraint).</summary>
    public string[]? AdditionalParameters { get; init; }

    /// <summary>Cria um contexto para converter o nome de uma entidade em nome de tabela.</summary>
    public static NamingContext ForTable(NamingConvention convention, string entityName) {
        return new NamingContext { Convention = convention, Input = entityName, Operation = NamingOperation.TableName };
    }

    /// <summary>Cria um contexto para converter o nome de uma propriedade em nome de coluna.</summary>
    public static NamingContext ForColumn(NamingConvention convention, string propertyName) {
        return new NamingContext
            { Convention = convention, Input = propertyName, Operation = NamingOperation.ColumnName };
    }

    /// <summary>Cria um contexto para montar o nome de um índice a partir da tabela e das colunas.</summary>
    public static NamingContext ForIndex(NamingConvention convention, string tableName, params string[] columnNames) {
        return new NamingContext {
            Convention = convention, Input = tableName, Operation = NamingOperation.IndexName,
            AdditionalParameters = columnNames
        };
    }

    /// <summary>Cria um contexto para montar o nome de uma constraint a partir de prefixo, tabela e colunas.</summary>
    public static NamingContext ForConstraint(NamingConvention convention, string prefix, string tableName,
        params string[] columnNames) {
        return new NamingContext {
            Convention = convention, Input = $"{prefix}|{tableName}", Operation = NamingOperation.ConstraintName,
            AdditionalParameters = columnNames
        };
    }

    /// <summary>Cria um contexto para validar um nome contra a convenção, na operação informada.</summary>
    public static NamingContext ForValidation(NamingConvention convention, string name, NamingOperation operation) {
        return new NamingContext { Convention = convention, Input = name, Operation = operation };
    }
}

/// <summary>Tipo de identificador tratado por uma <see cref="INamingStrategy" />.</summary>
public enum NamingOperation
{
    /// <summary>Nome de tabela.</summary>
    TableName,

    /// <summary>Nome de coluna.</summary>
    ColumnName,

    /// <summary>Nome de índice.</summary>
    IndexName,

    /// <summary>Nome de constraint.</summary>
    ConstraintName,

    /// <summary>Validação de um nome sem conversão.</summary>
    Validation
}