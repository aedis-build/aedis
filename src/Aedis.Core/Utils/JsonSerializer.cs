using MessagePack;

namespace Aedis.Core.Utils;

public static class JsonSerializer
{
    public static byte[]? Serialize<T>(T obj) {
        if (obj?.Equals(default(T)) ?? true)
            throw new ArgumentNullException(nameof(obj));
        return MessagePackSerializer.Serialize(obj);
    }

    public static T Deserialize<T>(byte[] data) {
        if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        return MessagePackSerializer.Deserialize<T>(data);
    }

    public static T Deserialize<T>(ReadOnlyMemory<byte> data) {
        return MessagePackSerializer.Deserialize<T>(data);
    }

    public static object Deserialize(ReadOnlyMemory<byte> data, Type type) {
        return MessagePackSerializer.Deserialize(type, data) ?? throw new InvalidOperationException();
    }
}

public class JsonValidationResult<T>(bool isValid, T? result) : IEquatable<JsonValidationResult<T>>
{
    public bool IsValid { get; } = isValid;
    public T? Result { get; } = result;

    public bool Equals(JsonValidationResult<T>? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsValid == other.IsValid && EqualityComparer<T>.Default.Equals(Result, other.Result);
    }

    public override bool Equals(object? obj) {
        return Equals(obj as JsonValidationResult<T>);
    }

    public override int GetHashCode() {
        return HashCode.Combine(IsValid, Result);
    }
}