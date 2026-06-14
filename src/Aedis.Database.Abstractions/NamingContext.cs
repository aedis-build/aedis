using Aedis.Database.Abstractions;

namespace Aedis.Database.Abstractions;

public class NamingContext
{
    public NamingConvention Convention { get; init; }
    public string Input { get; init; } = string.Empty;
    public NamingOperation Operation { get; init; }
    public string[]? AdditionalParameters { get; init; }

    public static NamingContext ForTable(NamingConvention convention, string entityName) {
        return new NamingContext { Convention = convention, Input = entityName, Operation = NamingOperation.TableName };
    }

    public static NamingContext ForColumn(NamingConvention convention, string propertyName) {
        return new NamingContext
            { Convention = convention, Input = propertyName, Operation = NamingOperation.ColumnName };
    }

    public static NamingContext ForIndex(NamingConvention convention, string tableName, params string[] columnNames) {
        return new NamingContext {
            Convention = convention, Input = tableName, Operation = NamingOperation.IndexName,
            AdditionalParameters = columnNames
        };
    }

    public static NamingContext ForConstraint(NamingConvention convention, string prefix, string tableName,
        params string[] columnNames) {
        return new NamingContext {
            Convention = convention, Input = $"{prefix}|{tableName}", Operation = NamingOperation.ConstraintName,
            AdditionalParameters = columnNames
        };
    }

    public static NamingContext ForValidation(NamingConvention convention, string name, NamingOperation operation) {
        return new NamingContext { Convention = convention, Input = name, Operation = operation };
    }
}

public enum NamingOperation
{
    TableName,
    ColumnName,
    IndexName,
    ConstraintName,
    Validation
}