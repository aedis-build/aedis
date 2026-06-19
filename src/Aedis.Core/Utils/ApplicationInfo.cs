using System.Reflection;

namespace Aedis.Core.Utils;

/// <summary>
///     Metadados da aplicação em execução (nome, versão) e relógio sensível ao fuso horário configurado.
///     Use como fonte única para nome/versão em logs e telemetria e para obter datas/horas no fuso padrão
///     da plataforma. O nome é derivado do executável, mas pode ser sobrescrito por <see cref="SetName" />.
/// </summary>
public static class ApplicationInfo
{
    private static string? _overrideName;

    /// <summary>
    ///     Nome da aplicação em minúsculas. Usa o valor definido por <see cref="SetName" /> quando
    ///     presente; caso contrário, deriva do nome do executável (sem extensão).
    /// </summary>
    public static string Name =>
        _overrideName ?? Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName).ToLowerInvariant();

    /// <summary>
    ///     Nome amigável para exibição, derivado de <see cref="Name" />: separa por <c>-</c>, <c>.</c> e
    ///     <c>_</c> e capitaliza cada palavra (ex.: <c>order-service</c> → <c>Order Service</c>).
    /// </summary>
    public static string DisplayName {
        get {
            var name = Name;

            return string.Join(" ", name
                .Split(new[] { '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
        }
    }

    /// <summary>
    ///     Versão da aplicação no formato <c>Major.Minor.Build</c>, lida do assembly de entrada (ou do
    ///     assembly atual como fallback). Retorna <c>"1.0.0"</c> quando a versão não está disponível.
    /// </summary>
    public static string Version {
        get {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "1.0.0";
        }
    }

    /// <summary>Instante atual em UTC.</summary>
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>Agora no fuso padrão (<c>TZ_DEFAULT</c>, ou Brasil se ausente).</summary>
    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GetDefaultTimeZone());

    /// <summary>Data de hoje no fuso padrão (<c>TZ_DEFAULT</c>, ou Brasil se ausente).</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Data de hoje em UTC.</summary>
    public static DateOnly Date => DateOnly.FromDateTime(UtcNow.Date);

    /// <summary>
    ///     Sobrescreve o nome da aplicação (normalizado para minúsculas), substituindo o valor derivado do
    ///     executável. Útil em testes ou hosts onde o nome do processo não reflete a aplicação.
    /// </summary>
    /// <param name="name">Novo nome; não pode ser nulo ou em branco.</param>
    /// <exception cref="ArgumentException">Se <paramref name="name" /> for nulo ou em branco.</exception>
    public static void SetName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Application name cannot be null or whitespace.", nameof(name));

        _overrideName = name.ToLowerInvariant();
    }

    /// <summary>Remove o nome sobrescrito por <see cref="SetName" />, voltando ao valor derivado do executável.</summary>
    public static void ResetName() {
        _overrideName = null;
    }

    /// <summary>
    ///     Fuso horário padrão da aplicação. Lê a variável de ambiente <c>TZ_DEFAULT</c>
    ///     (ex.: "America/Sao_Paulo", "UTC", "Europe/Lisbon"); se ausente, vazia, inválida
    ///     (<see cref="TimeZoneNotFoundException" />) ou com dados de fuso corrompidos
    ///     (<see cref="InvalidTimeZoneException" />), usa o Brasil (America/Sao_Paulo) como padrão.
    /// </summary>
    public static TimeZoneInfo GetDefaultTimeZone() {
        var configured = Environment.GetEnvironmentVariable("TZ_DEFAULT");

        if (!string.IsNullOrWhiteSpace(configured)) {
            try {
                return TimeZoneInfo.FindSystemTimeZoneById(configured);
            }
            catch (TimeZoneNotFoundException) {
            }
            catch (InvalidTimeZoneException) {
            }
        }

        return BrazilDefault();
    }

    /// <summary>
    ///     Resolve o fuso do Brasil tolerando a diferença entre plataformas: tenta o ID IANA
    ///     <c>America/Sao_Paulo</c> e, em sistemas Windows sem ICU, cai no ID Windows
    ///     <c>E. South America Standard Time</c>.
    /// </summary>
    private static TimeZoneInfo BrazilDefault() {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}