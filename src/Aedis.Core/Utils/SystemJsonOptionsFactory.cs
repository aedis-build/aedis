using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aedis.Core.Utils;

/// <summary>
///     Factory for creating standardized System.Text.Json options across the framework.
///     Provides consistent JSON serialization settings for HTTP APIs, storage, and logging.
/// </summary>
public static class SystemJsonOptionsFactory
{
    /// <summary>
    ///     Creates JsonSerializerOptions with framework-standard configuration for HTTP APIs.
    ///     Compact format (WriteIndented = false) for network efficiency.
    /// </summary>
    /// <returns>Configured JsonSerializerOptions instance</returns>
    public static JsonSerializerOptions Create() {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }

    /// <summary>
    ///     Creates JsonSerializerOptions for storage and debugging.
    ///     Indented format (WriteIndented = true) for better human readability.
    ///     Use for: S3 persistence, file storage, audit logs, debugging.
    /// </summary>
    /// <returns>Configured JsonSerializerOptions instance with indentation</returns>
    public static JsonSerializerOptions CreateForStorage() {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }
}