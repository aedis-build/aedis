namespace Aedis.Core.Utils;

/// <summary>
///     Metadados da aplicação em execução (nome) e relógio UTC.
///     Use como fonte única para o nome em logs e telemetria. O nome é derivado do executável,
///     mas pode ser sobrescrito por <see cref="SetName" />.
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
    public static string DisplayName =>
        string.Join(" ", Name
            .Split(['-', '.', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>Instante atual em UTC.</summary>
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>
    ///     Sobrescreve o nome da aplicação (normalizado para minúsculas). Útil quando o executável
    ///     não representa o nome lógico do serviço (ex.: hosts genéricos).
    /// </summary>
    /// <exception cref="ArgumentException">Quando <paramref name="name" /> é nulo ou vazio.</exception>
    public static void SetName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Application name cannot be null or whitespace.", nameof(name));

        _overrideName = name.ToLowerInvariant();
    }

    /// <summary>Remove a sobrescrita feita por <see cref="SetName" />, voltando ao nome do executável.</summary>
    public static void ResetName() {
        _overrideName = null;
    }
}
