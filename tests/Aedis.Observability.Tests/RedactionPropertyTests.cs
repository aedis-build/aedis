using Aedis.Observability.Serilog;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace Aedis.Observability.Tests;

/// <summary>
///     Propriedades (FsCheck) da ofuscação: entradas arbitrárias nunca derrubam o normalizador nem o redator de
///     query string, e os invariantes valem para qualquer valor — não só para os exemplos dos testes unitários.
/// </summary>
public sealed class RedactionPropertyTests {
    private static readonly RedactionOptions Options = new();

    [Property]
    public bool Normalize_e_idempotente_e_so_devolve_letras_e_digitos(string name) {
        var normalized = RedactionOptions.Normalize(name);

        return RedactionOptions.Normalize(normalized) == normalized
               && normalized.All(char.IsLetterOrDigit)
               && normalized.Length <= (name?.Length ?? 0);
    }

    [Property]
    public bool Redact_nunca_lanca_e_preserva_a_quantidade_de_parametros(string queryString) {
        var redacted = QueryStringRedactor.Redact(queryString, Options);

        return string.IsNullOrEmpty(queryString)
            ? redacted.Length == 0
            : redacted.Count(c => c == '&') == queryString.Count(c => c == '&') && redacted.StartsWith('?') == queryString.StartsWith('?');
    }

    [Property]
    public bool Redact_mascara_o_sensivel_e_preserva_o_valor_dos_demais(NonEmptyString value) {
        var safe = new string(value.Get.Where(char.IsLetterOrDigit).ToArray());

        return QueryStringRedactor.Redact($"?page={safe}&access_token={safe}&cpf={safe}", Options)
               == $"?page={safe}&access_token={Options.Placeholder}&cpf={Options.Placeholder}";
    }
}
