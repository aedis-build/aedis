using System.ComponentModel.DataAnnotations;

namespace Aedis.OpenApi.Abstractions;

/// <summary>
///     Configures the OpenAPI document and its UI, bound to the <c>OpenApi</c> section. Documentation is a
///     plugin: it only exists when the application references <c>Aedis.OpenApi</c> (document) and
///     <c>Aedis.OpenApi.Scalar</c> (UI) and maps them — the template does so only in Development.
/// </summary>
public sealed class AedisOpenApiOptions
{
    /// <summary>Configuration section name bound to these options.</summary>
    public const string SectionName = "OpenApi";

    /// <summary>OpenAPI document name (also part of the JSON route). Default <c>v1</c>.</summary>
    [Required]
    public string DocumentName { get; set; } = "v1";

    /// <summary>Title shown in the document and the UI. When empty, falls back to the entry assembly name.</summary>
    public string? Title { get; set; }

    /// <summary>API version shown in the document. Default <c>v1</c>.</summary>
    [Required]
    public string Version { get; set; } = "v1";

    /// <summary>Optional description shown in the document.</summary>
    public string? Description { get; set; }

    /// <summary>Route prefix of the UI and the JSON (e.g. <c>openapi</c> → <c>/openapi</c>, <c>/openapi/v1.json</c>). Default <c>openapi</c>.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9._~-]+(/[A-Za-z0-9._~-]+)*$", ErrorMessage = "RoutePrefix must be a path without leading/trailing slashes.")]
    public string RoutePrefix { get; set; } = "openapi";

    /// <summary>
    ///     When <c>true</c> (default), declares the <c>Bearer</c> (JWT) security scheme in the document so the UI
    ///     can authorize requests. Keep it aligned with the host's authentication state.
    /// </summary>
    public bool IncludeBearerSecurity { get; set; } = true;
}
