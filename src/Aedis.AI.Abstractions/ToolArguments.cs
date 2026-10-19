using System.Text.Json;

namespace Aedis.AI.Abstractions;

/// <summary>
///     Leitura validada dos argumentos de uma <see cref="ToolCall" />. O modelo pode mandar qualquer coisa; estes
///     métodos são a validação na fronteira de confiança e transformam entrada inválida em
///     <see cref="InvalidToolCallException" />, que a aplicação devolve à sessão como <see cref="ToolResult" /> de erro.
/// </summary>
/// <example>
///     <code>
///     var role = call.GetString("role");
///     var note = call.GetOptionalString("note");
///     </code>
/// </example>
public static class ToolArguments
{
    /// <summary>Texto obrigatório; lança <see cref="InvalidToolCallException" /> se faltar, vier vazio ou não for texto.</summary>
    public static string GetString(this ToolCall call, string field) =>
        call.GetOptionalString(field) ?? throw new InvalidToolCallException($"Campo obrigatório '{field}' ausente em {call.Name}.");

    /// <summary>Texto opcional; <c>null</c> se faltar, vier vazio ou não for texto.</summary>
    public static string? GetOptionalString(this ToolCall call, string field) =>
        call.Input.ValueKind == JsonValueKind.Object
        && call.Input.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
