namespace Aedis.Hosting.Abstractions;

/// <summary>
///     Registro de recursos descartáveis criados por esta instância da aplicação, usado no
///     desligamento gracioso para garantir o descarte correto de todos os recursos.
///     Singleton por aplicação/pod; thread-safe para acesso concorrente.
/// </summary>
public interface IDisposableRegistry
{
    /// <summary>Quantidade de descartáveis atualmente registrados (diagnóstico/teste).</summary>
    int Count { get; }

    /// <summary>Registra um recurso descartável síncrono para limpeza no shutdown.</summary>
    void Register(IDisposable disposable);

    /// <summary>Registra um recurso descartável assíncrono para limpeza no shutdown.</summary>
    void Register(IAsyncDisposable disposable);

    /// <summary>
    ///     Registra um descartável assíncrono com uma chave única; retorna um wrapper que, ao ser
    ///     descartado, também o remove do registro (evita vazamento de memória em recursos por requisição).
    /// </summary>
    /// <param name="key">Chave única do recurso (ex.: CorrelationId).</param>
    /// <param name="disposable">Recurso a rastrear para descarte.</param>
    /// <returns>Wrapper que descarta o recurso e o desregistra automaticamente.</returns>
    IAsyncDisposable Register(string key, IAsyncDisposable disposable);

    /// <summary>Descarta todos os recursos registrados (sync e async). Chamado no shutdown gracioso.</summary>
    Task DisposeAllAsync(CancellationToken cancellationToken = default);
}
