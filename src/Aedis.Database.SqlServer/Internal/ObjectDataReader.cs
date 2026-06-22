using System.Data;
using System.Reflection;

namespace Aedis.Database.SqlServer.Internal;

/// <summary>
///     <see cref="IDataReader" /> que projeta uma sequência de entidades sob demanda (streaming), sem
///     materializar tudo em memória — é o que dá ao <c>SqlBulkCopy</c> a mesma eficiência de memória do
///     <c>COPY</c> do PostgreSQL em cargas de dezenas de milhões. Converte enums para texto maiúsculo e
///     <see cref="DateOnly" />/<see cref="TimeOnly" /> para os tipos que o SQL Server aceita, em paridade com
///     o caminho de escrita single-row.
/// </summary>
internal sealed class ObjectDataReader<T> : IDataReader where T : class {
    private readonly PropertyInfo[] _properties;
    private readonly IEnumerator<T> _enumerator;
    private bool _closed;

    public ObjectDataReader(PropertyInfo[] properties, IEnumerable<T> source) {
        _properties = properties;
        _enumerator = source.GetEnumerator();
    }

    public int FieldCount => _properties.Length;

    public bool Read() => _enumerator.MoveNext();

    public object GetValue(int i) {
        var value = _properties[i].GetValue(_enumerator.Current);
        if (value is null) {
            return DBNull.Value;
        }

        var type = Nullable.GetUnderlyingType(_properties[i].PropertyType) ?? _properties[i].PropertyType;
        return value switch {
            _ when type.IsEnum => value.ToString()!.ToUpperInvariant(),
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            TimeOnly timeOnly => timeOnly.ToTimeSpan(),
            _ => value
        };
    }

    public bool IsDBNull(int i) => _properties[i].GetValue(_enumerator.Current) is null;

    public string GetName(int i) => _properties[i].Name;

    public int GetOrdinal(string name) {
        for (var i = 0; i < _properties.Length; i++) {
            if (string.Equals(_properties[i].Name, name, StringComparison.Ordinal)) {
                return i;
            }
        }

        throw new IndexOutOfRangeException(name);
    }

    public Type GetFieldType(int i) {
        var type = Nullable.GetUnderlyingType(_properties[i].PropertyType) ?? _properties[i].PropertyType;
        if (type.IsEnum) {
            return typeof(string);
        }

        if (type == typeof(DateOnly)) {
            return typeof(DateTime);
        }

        return type == typeof(TimeOnly) ? typeof(TimeSpan) : type;
    }

    public string GetDataTypeName(int i) => GetFieldType(i).Name;

    public int GetValues(object[] values) {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) {
            values[i] = GetValue(i);
        }

        return count;
    }

    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));

    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public char GetChar(int i) => (char)GetValue(i);
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public IDataReader GetData(int i) => throw new NotSupportedException();

    public int Depth => 0;
    public int RecordsAffected => -1;
    public bool IsClosed => _closed;
    public bool NextResult() => false;
    public DataTable? GetSchemaTable() => null;

    public void Close() {
        if (_closed) {
            return;
        }

        _closed = true;
        _enumerator.Dispose();
    }

    public void Dispose() => Close();
}
