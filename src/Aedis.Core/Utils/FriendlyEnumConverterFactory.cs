using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aedis.Core.Utils;

/// <summary>
///     JSON converter factory for enums whose invalid-input error is readable and vendor-neutral:
///     <c>"Invalid value 'x'. Accepted values: a, b, c."</c> instead of the runtime default that leaks the
///     CLR type name (<c>System.Nullable`1[...]</c>). Member names are accepted case-insensitively, numeric
///     values are still accepted, and names are written through the configured naming policy.
///     Nullable enums are handled by the serializer wrapping the underlying converter.
/// </summary>
public sealed class FriendlyEnumConverterFactory : JsonConverterFactory
{
    private readonly JsonNamingPolicy? _namingPolicy;

    /// <summary>Creates the factory, optionally applying a naming policy to member names.</summary>
    /// <param name="namingPolicy">Policy used to read and write member names; <c>null</c> keeps the declared names.</param>
    public FriendlyEnumConverterFactory(JsonNamingPolicy? namingPolicy = null) {
        _namingPolicy = namingPolicy;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
        var converterType = typeof(FriendlyEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType, _namingPolicy)!;
    }
}

/// <summary>
///     Enum converter that pairs with <see cref="FriendlyEnumConverterFactory" />: reads names (any casing) and
///     numbers, writes names through the naming policy, and reports invalid input with the list of accepted
///     values only.
/// </summary>
/// <typeparam name="TEnum">Enum type being converted.</typeparam>
internal sealed class FriendlyEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private readonly JsonNamingPolicy? _namingPolicy;
    private readonly string _acceptedValues;
    private readonly bool _isFlags;

    /// <summary>Precomputes the accepted-values list through the naming policy.</summary>
    public FriendlyEnumConverter(JsonNamingPolicy? namingPolicy) {
        _namingPolicy = namingPolicy;
        _isFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);
        _acceptedValues = string.Join(", ", Enum.GetNames<TEnum>().Select(Format));
    }

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number)) {
            var fromNumber = (TEnum)Enum.ToObject(typeof(TEnum), number);
            if (IsAccepted(fromNumber))
                return fromNumber;

            throw Invalid(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (reader.TokenType == JsonTokenType.String) {
            var raw = reader.GetString() ?? string.Empty;
            if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) && IsAccepted(parsed))
                return parsed;

            throw Invalid(raw);
        }

        throw Invalid(reader.TokenType.ToString());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) {
        writer.WriteStringValue(Format(value.ToString()));
    }

    private bool IsAccepted(TEnum value) => _isFlags || Enum.IsDefined(value);

    private string Format(string name) => _namingPolicy?.ConvertName(name) ?? name;

    private JsonException Invalid(string raw) =>
        new($"Invalid value '{raw}'. Accepted values: {_acceptedValues}.");
}
