using Microsoft.Extensions.Configuration;

namespace Aedis.Observability.Serilog;

/// <summary>
///     Configuração da ofuscação de logs do Aedis. Ligada por padrão (secure-by-default). Os campos sensíveis
///     são identificados pelo nome da propriedade (normalizado: minúsculas, sem separadores), em dois grupos:
///     <see cref="SecretKeys" /> (segredos — sempre máscara total) e <see cref="PiiKeys" /> (dados pessoais —
///     estratégia configurável). Ajuste pela seção <c>Logging:Redaction</c>.
/// </summary>
public sealed class RedactionOptions {
    /// <summary>Liga/desliga a ofuscação. Padrão: ligada.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Texto que substitui valores totalmente mascarados. Padrão <c>***</c>.</summary>
    public string Placeholder { get; set; } = "***";

    /// <summary>Quantidade de caracteres finais preservados na estratégia <see cref="RedactionStrategy.Partial" />.</summary>
    public int KeepLast { get; set; } = 4;

    /// <summary>Estratégia para segredos. Padrão <see cref="RedactionStrategy.Mask" /> (não use parcial em segredos).</summary>
    public RedactionStrategy SecretStrategy { get; set; } = RedactionStrategy.Mask;

    /// <summary>Estratégia para PII. Padrão <see cref="RedactionStrategy.Partial" />.</summary>
    public RedactionStrategy PiiStrategy { get; set; } = RedactionStrategy.Partial;

    /// <summary>Chave usada pela estratégia <see cref="RedactionStrategy.Hash" /> (HMAC). Sem ela, Hash recai em máscara total.</summary>
    public string? HashKey { get; set; }

    /// <summary>Nomes (normalizados) tratados como segredo. Inclui os padrão; adicione mais via configuração.</summary>
    public HashSet<string> SecretKeys { get; } = [
        "authorization", "proxyauthorization", "password", "passwd", "pwd", "secret", "clientsecret",
        "token", "accesstoken", "refreshtoken", "idtoken", "bearertoken", "apikey", "xapikey",
        "connectionstring", "cookie", "setcookie", "sessionid", "privatekey", "otp", "pin"
    ];

    /// <summary>Nomes (normalizados) tratados como PII não-ambíguo. Inclui os padrão; adicione mais via configuração.</summary>
    public HashSet<string> PiiKeys { get; } = [
        "cpf", "cnpj", "rg", "email", "phone", "telefone", "celular", "creditcard", "cardnumber",
        "pan", "cvv", "cvc", "passport", "passaporte"
    ];

    /// <summary>
    ///     Normaliza um nome de campo para comparação (minúsculas, sem caracteres não alfanuméricos).
    /// </summary>
    /// <param name="name">Nome do campo.</param>
    public static string Normalize(string name) {
        if (string.IsNullOrEmpty(name)) {
            return string.Empty;
        }

        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        var length = 0;
        foreach (var c in name) {
            if (char.IsLetterOrDigit(c)) {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    ///     Lê as opções da seção <c>Logging:Redaction</c>, mesclando com os padrões. Aceita
    ///     <c>Enabled</c>, <c>Placeholder</c>, <c>KeepLast</c>, <c>SecretStrategy</c>, <c>PiiStrategy</c>,
    ///     <c>HashKey</c>, <c>AdditionalSecretKeys</c> e <c>AdditionalPiiKeys</c>.
    /// </summary>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static RedactionOptions FromConfiguration(IConfiguration configuration) {
        var options = new RedactionOptions();
        var section = configuration.GetSection("Logging:Redaction");
        if (!section.Exists()) {
            return options;
        }

        if (bool.TryParse(section["Enabled"], out var enabled)) {
            options.Enabled = enabled;
        }

        if (!string.IsNullOrEmpty(section["Placeholder"])) {
            options.Placeholder = section["Placeholder"]!;
        }

        if (int.TryParse(section["KeepLast"], out var keepLast) && keepLast >= 0) {
            options.KeepLast = keepLast;
        }

        if (Enum.TryParse<RedactionStrategy>(section["SecretStrategy"], true, out var secretStrategy) && secretStrategy != RedactionStrategy.Inherit) {
            options.SecretStrategy = secretStrategy;
        }

        if (Enum.TryParse<RedactionStrategy>(section["PiiStrategy"], true, out var piiStrategy) && piiStrategy != RedactionStrategy.Inherit) {
            options.PiiStrategy = piiStrategy;
        }

        if (!string.IsNullOrEmpty(section["HashKey"])) {
            options.HashKey = section["HashKey"];
        }

        foreach (var child in section.GetSection("AdditionalSecretKeys").GetChildren()) {
            if (!string.IsNullOrEmpty(child.Value)) {
                options.SecretKeys.Add(Normalize(child.Value));
            }
        }

        foreach (var child in section.GetSection("AdditionalPiiKeys").GetChildren()) {
            if (!string.IsNullOrEmpty(child.Value)) {
                options.PiiKeys.Add(Normalize(child.Value));
            }
        }

        return options;
    }
}
