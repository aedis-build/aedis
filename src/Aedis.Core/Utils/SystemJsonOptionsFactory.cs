using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aedis.Core.Utils;

/// <summary>
///     Factory for the framework-standard System.Text.Json settings shared by HTTP APIs, storage and logging:
///     camelCase names, nulls omitted, relaxed escaping, case-insensitive reads and the
///     <see cref="FriendlyEnumConverterFactory" /> so invalid enum input never leaks CLR type names.
/// </summary>
public static class SystemJsonOptionsFactory
{
    /// <summary>
    ///     Creates options for HTTP APIs: compact output (<c>WriteIndented = false</c>) for network efficiency.
    /// </summary>
    /// <returns>A new, fully configured <see cref="JsonSerializerOptions" /> instance.</returns>
    public static JsonSerializerOptions Create() {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }

    /// <summary>
    ///     Creates options for storage and debugging: indented output for human readability.
    ///     Use for object storage, file persistence, audit logs and debugging.
    /// </summary>
    /// <returns>A new, fully configured <see cref="JsonSerializerOptions" /> instance with indentation.</returns>
    public static JsonSerializerOptions CreateForStorage() {
        var options = Create();
        options.WriteIndented = true;
        return options;
    }

    /// <summary>
    ///     Applies the framework-standard settings to an existing instance. This is the entry point for hosts,
    ///     whose MVC and minimal-API options objects cannot be replaced, only mutated in place. Safe to call
    ///     more than once: the enum converter is added only when absent.
    /// </summary>
    /// <param name="options">The options instance to configure.</param>
    public static void Configure(JsonSerializerOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.PropertyNameCaseInsensitive = true;

        if (!options.Converters.Any(converter => converter is FriendlyEnumConverterFactory))
            options.Converters.Add(new FriendlyEnumConverterFactory(JsonNamingPolicy.CamelCase));
    }
}
