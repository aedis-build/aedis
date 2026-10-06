using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aedis.Hosting.AspNetCore.Validation;

/// <summary>
///     Converte o <see cref="ModelStateDictionary" /> no mapa <c>errors</c> do ProblemDetails 422 sem vazar
///     estrutura interna. O <c>System.Text.Json</c> compõe a mensagem de falha de desserialização com o nome do
///     tipo CLR de destino (<c>could not be converted to System.Nullable`1[...]</c>); aqui ela vira uma frase
///     neutra que referencia só o campo. Quando o corpo inteiro falhou (chave raiz <c>$</c>), o MVC ainda emite um
///     <c>"The X field is required."</c> espúrio para o parâmetro da action, que é descartado. Mensagens de
///     validação legítimas passam intactas.
/// </summary>
internal static class ModelStateSanitizer
{
    private const string NeutralMessage = "The value provided for '{0}' is not valid.";
    private const string RootBodyField = "body";

    /// <summary>Produz o mapa campo → mensagens já saneado.</summary>
    /// <param name="modelState">Estado do modelo da requisição inválida.</param>
    /// <returns>Dicionário por campo, só com entradas que tinham erros.</returns>
    internal static IReadOnlyDictionary<string, string[]> SanitizeModelState(ModelStateDictionary modelState) {
        var bodyFailed = modelState.Keys.Any(IsRootBodyKey);
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var (key, entry) in modelState) {
            if (entry is null || entry.Errors.Count == 0) continue;

            var field = ToFieldName(key);
            var messages = entry.Errors
                .Select(error => error.ErrorMessage ?? string.Empty)
                .Where(message => !(bodyFailed && !IsBodyPropertyKey(key) && IsSpuriousRequired(message)))
                .Select(message => IsLeakedMessage(message)
                    ? string.Format(CultureInfo.InvariantCulture, NeutralMessage, field)
                    : message)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (messages.Length > 0)
                result[field] = messages;
        }

        return result;
    }

    /// <summary>
    ///     Identifica mensagem que vaza o tipo CLR ou vem de <c>JsonException</c> sem texto: contém
    ///     <c>could not be converted</c>, contém <c>System.</c>, ou está vazia.
    /// </summary>
    internal static bool IsLeakedMessage(string? message) =>
        string.IsNullOrWhiteSpace(message) ||
        message.Contains("could not be converted", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("System.", StringComparison.Ordinal);

    private static bool IsSpuriousRequired(string message) =>
        message.EndsWith("field is required.", StringComparison.Ordinal);

    private static bool IsRootBodyKey(string key) => key.Length == 0 || key == "$";

    private static bool IsBodyPropertyKey(string key) => key.StartsWith("$.", StringComparison.Ordinal);

    private static string ToFieldName(string key) {
        if (IsRootBodyKey(key)) return RootBodyField;
        return IsBodyPropertyKey(key) ? key[2..] : key;
    }
}
