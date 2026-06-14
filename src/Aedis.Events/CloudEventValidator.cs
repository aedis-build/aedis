using System.Text.RegularExpressions;

namespace Aedis.Events;

/// <summary>
///     Validador para ResourceCloudEvent, garantindo conformidade com CloudEvents v1.0.
/// </summary>
public static class CloudEventValidator
{
    private static readonly Regex SourcePattern =
        new(@"^/payhop/[a-z0-9-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TypePattern = new(@"^com\.payhop\.[a-z0-9]+(?:\.[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     Valida um ResourceCloudEvent, verificando campos obrigatórios e formatos.
    /// </summary>
    /// <param name="cloudEvent">Evento a ser validado</param>
    /// <returns>Resultado da validação com lista de erros (vazia se válido)</returns>
    public static ValidationResult Validate(ResourceCloudEvent cloudEvent) {
        if (cloudEvent == null) return ValidationResult.Failure("CloudEvent cannot be null");

        var errors = new List<string>();

        // Validar specversion
        if (string.IsNullOrWhiteSpace(cloudEvent.SpecVersion))
            errors.Add("specversion is required");
        else if (cloudEvent.SpecVersion != "1.0")
            errors.Add($"specversion must be '1.0', got '{cloudEvent.SpecVersion}'");

        // Validar id (Guid não pode ser Guid.Empty)
        if (cloudEvent.Id == Guid.Empty) errors.Add("id is required and cannot be Guid.Empty");

        // Validar source
        if (string.IsNullOrWhiteSpace(cloudEvent.Source))
            errors.Add("source is required");
        else if (!SourcePattern.IsMatch(cloudEvent.Source))
            errors.Add($"source must match pattern '/payhop/<service>', got '{cloudEvent.Source}'");

        // Validar type
        if (string.IsNullOrWhiteSpace(cloudEvent.Type))
            errors.Add("type is required");
        else if (!TypePattern.IsMatch(cloudEvent.Type))
            errors.Add($"type must match pattern 'com.payhop.<domain>.<event>', got '{cloudEvent.Type}'");

        // Validar time
        if (cloudEvent.Time == default) errors.Add("time is required");

        // Validar datacontenttype
        if (string.IsNullOrWhiteSpace(cloudEvent.DataContentType))
            errors.Add("datacontenttype is required");
        else if (cloudEvent.DataContentType != "application/json")
            errors.Add($"datacontenttype must be 'application/json', got '{cloudEvent.DataContentType}'");

        // subject e data são opcionais, não validamos aqui

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(string.Join("; ", errors));
    }
}

/// <summary>
///     Resultado de validação de um ResourceCloudEvent.
/// </summary>
public class ValidationResult
{
    private ValidationResult(bool isValid, string? errorMessage) {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    ///     Indica se a validação foi bem-sucedida.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    ///     Mensagem de erro (null se válido).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    ///     Cria um resultado de validação bem-sucedido.
    /// </summary>
    public static ValidationResult Success() {
        return new ValidationResult(true, null);
    }

    /// <summary>
    ///     Cria um resultado de validação com erro.
    /// </summary>
    public static ValidationResult Failure(string errorMessage) {
        return new ValidationResult(false, errorMessage ?? "Validation failed");
    }

    /// <summary>
    ///     Lança uma exceção se a validação falhou.
    /// </summary>
    /// <exception cref="ArgumentException">Se a validação falhou</exception>
    public void ThrowIfInvalid() {
        if (!IsValid)
            throw new ArgumentException(ErrorMessage ?? "CloudEvent validation failed", nameof(ResourceCloudEvent));
    }
}