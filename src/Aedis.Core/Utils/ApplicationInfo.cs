using System.Reflection;

namespace Aedis.Core.Utils;

public static class ApplicationInfo
{
    private static string? _overrideName;

    public static string Name =>
        _overrideName ?? Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName).ToLowerInvariant();

    public static string DisplayName {
        get {
            var name = Name;

            return string.Join(" ", name
                .Split(new[] { '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
        }
    }

    public static string Version {
        get {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "1.0.0";
        }
    }

    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>Agora no fuso padrão (<c>TZ_DEFAULT</c>, ou Brasil se ausente).</summary>
    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GetDefaultTimeZone());

    /// <summary>Data de hoje no fuso padrão (<c>TZ_DEFAULT</c>, ou Brasil se ausente).</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Data de hoje em UTC.</summary>
    public static DateOnly Date => DateOnly.FromDateTime(UtcNow.Date);

    public static void SetName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Application name cannot be null or whitespace.", nameof(name));

        _overrideName = name.ToLowerInvariant();
    }

    public static void ResetName() {
        _overrideName = null;
    }

    /// <summary>
    ///     Fuso horário padrão da aplicação. Lê a variável de ambiente <c>TZ_DEFAULT</c>
    ///     (ex.: "America/Sao_Paulo", "UTC", "Europe/Lisbon"); se ausente, vazia ou inválida,
    ///     usa o Brasil (America/Sao_Paulo) como padrão.
    /// </summary>
    public static TimeZoneInfo GetDefaultTimeZone() {
        var configured = Environment.GetEnvironmentVariable("TZ_DEFAULT");

        if (!string.IsNullOrWhiteSpace(configured)) {
            try {
                return TimeZoneInfo.FindSystemTimeZoneById(configured);
            }
            catch (TimeZoneNotFoundException) {
                // valor inválido → cai no default
            }
            catch (InvalidTimeZoneException) {
                // dados de fuso corrompidos → cai no default
            }
        }

        return BrazilDefault();
    }

    private static TimeZoneInfo BrazilDefault() {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}