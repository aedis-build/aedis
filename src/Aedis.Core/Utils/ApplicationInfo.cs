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
    public static DateTimeOffset BrasiliaNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GetDefaultTimeZone());
    public static DateOnly BrasiliaToday => DateOnly.FromDateTime(BrasiliaNow.DateTime);
    public static DateOnly Date => DateOnly.FromDateTime(UtcNow.Date);

    public static void SetName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Application name cannot be null or whitespace.", nameof(name));

        _overrideName = name.ToLowerInvariant();
    }

    public static void ResetName() {
        _overrideName = null;
    }

    public static TimeZoneInfo GetDefaultTimeZone() {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}