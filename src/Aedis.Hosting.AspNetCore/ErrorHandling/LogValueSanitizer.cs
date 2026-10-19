namespace Aedis.Hosting.AspNetCore.ErrorHandling;

/// <summary>
///     Neutralizes request-controlled values before they reach a log line. A path such as
///     <c>/a%0Ab</c> arrives decoded with a line break inside it; written as-is to a text sink it forges a second,
///     fake log entry (CWE-117). Line breaks become their visible escapes and other control characters are dropped.
/// </summary>
public static class LogValueSanitizer
{
    /// <summary>Returns <paramref name="value" /> safe for a single log line; null or empty stays empty.</summary>
    /// <param name="value">Value taken from the request (path, method, header).</param>
    public static string Sanitize(string? value) {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var safe = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return safe.Any(char.IsControl) ? new string(safe.Where(c => !char.IsControl(c)).ToArray()) : safe;
    }
}
