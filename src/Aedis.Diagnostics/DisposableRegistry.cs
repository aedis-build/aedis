using System.Collections.Concurrent;
using Aedis.Hosting.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aedis.Diagnostics;

/// <summary>
///     Registro thread-safe dos recursos descartáveis criados por <em>esta</em> instância da aplicação
///     (suporta <see cref="IDisposable" /> e <see cref="IAsyncDisposable" />). Usado no desligamento
///     gracioso para garantir que tudo o que esta instância detém — em especial <em>locks distribuídos</em>
///     (handles de liderança) — seja liberado antes do processo terminar.
///     <para>
///         Padrão: Registry. Escopo: Singleton (uma instância por processo/pod). Uso típico:
///         registre o recurso ao criá-lo (<c>registry.Register(handle)</c>) e descarte todos no
///         desligamento (<c>await registry.DisposeAllAsync()</c>). O descarte é feito em paralelo e
///         tolera falhas individuais (uma falha não impede o descarte dos demais).
///     </para>
/// </summary>
public sealed class DisposableRegistry : IDisposableRegistry
{
    private readonly ConcurrentBag<IAsyncDisposable> _asyncDisposables = new();
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _keyedDisposables = new();
    private readonly ILogger<DisposableRegistry>? _logger;
    private readonly ConcurrentBag<IDisposable> _syncDisposables = new();

    /// <summary>
    ///     Cria o registro vazio. O <paramref name="logger" /> é opcional e, quando fornecido, emite traços de
    ///     diagnóstico (Debug) a cada registro/desregistro e ao descartar os recursos.
    /// </summary>
    /// <param name="logger">Logger opcional para diagnóstico do ciclo de vida dos recursos.</param>
    public DisposableRegistry(ILogger<DisposableRegistry>? logger = null) {
        _logger = logger;
    }

    /// <summary>Total de descartáveis registrados no momento (diagnóstico/teste).</summary>
    public int Count => _syncDisposables.Count + _asyncDisposables.Count + _keyedDisposables.Count;

    /// <summary>Registra um recurso síncrono para descarte no desligamento. Thread-safe.</summary>
    public void Register(IDisposable disposable) {
        if (disposable is null) return;

        _syncDisposables.Add(disposable);
        _logger?.LogDebug("Registrado descartável síncrono: {Type}", disposable.GetType().Name);
    }

    /// <summary>Registra um recurso assíncrono para descarte no desligamento. Thread-safe.</summary>
    public void Register(IAsyncDisposable disposable) {
        if (disposable is null) return;

        _asyncDisposables.Add(disposable);
        _logger?.LogDebug("Registrado descartável assíncrono: {Type}", disposable.GetType().Name);
    }

    /// <summary>
    ///     Registra um recurso assíncrono sob uma chave única e devolve um wrapper que se
    ///     <em>auto-desregistra</em> ao ser descartado — evita vazamento quando o recurso é liberado
    ///     antes do desligamento (ex.: o handle de liderança liberado pelo handler). Thread-safe.
    /// </summary>
    /// <param name="key">Chave única do recurso (ex.: CorrelationId, chave do lock).</param>
    /// <param name="disposable">Recurso a rastrear.</param>
    /// <returns>Wrapper que descarta o recurso e o remove do registro.</returns>
    public IAsyncDisposable Register(string key, IAsyncDisposable disposable) {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou em branco.", nameof(key));

        ArgumentNullException.ThrowIfNull(disposable);

        _keyedDisposables.AddOrUpdate(key, disposable, (_, old) => {
            _logger?.LogWarning(
                "Substituindo descartável existente para a chave {Key}. Antigo: {OldType}, novo: {NewType}",
                key, old.GetType().Name, disposable.GetType().Name);
            return disposable;
        });

        _logger?.LogDebug("Registrado descartável com chave {Key}: {Type}", key, disposable.GetType().Name);

        return new AutoUnregisterWrapper(key, disposable, this);
    }

    /// <summary>
    ///     Descarta todos os recursos registrados (síncronos, assíncronos e por chave) em paralelo.
    ///     Deve ser chamado no desligamento gracioso. Falhas individuais são registradas e ignoradas.
    /// </summary>
    public async Task DisposeAllAsync(CancellationToken cancellationToken = default) {
        var syncList = _syncDisposables.ToArray();
        var asyncList = _asyncDisposables.ToArray();
        var keyedList = _keyedDisposables.Values.ToArray();
        var totalCount = syncList.Length + asyncList.Length + keyedList.Length;

        if (totalCount == 0) {
            _logger?.LogDebug("Nenhum recurso descartável para limpar.");
            return;
        }

        _logger?.LogDebug("Descartando {Count} recursos registrados ({Sync} sync, {Async} async, {Keyed} por chave)...",
            totalCount, syncList.Length, asyncList.Length, keyedList.Length);

        var syncTasks = syncList.Select((disposable, index) => Task.Run(() => {
            try {
                disposable.Dispose();
                _logger?.LogDebug("Descartado recurso síncrono {Index}/{Total}: {Type}",
                    index + 1, syncList.Length, disposable.GetType().Name);
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Falha ao descartar recurso síncrono {Type}", disposable.GetType().Name);
            }
        }, cancellationToken));

        var asyncTasks = asyncList.Select(async (disposable, index) => {
            try {
                await disposable.DisposeAsync();
                _logger?.LogDebug("Descartado recurso assíncrono {Index}/{Total}: {Type}",
                    index + 1, asyncList.Length, disposable.GetType().Name);
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Falha ao descartar recurso assíncrono {Type}", disposable.GetType().Name);
            }
        });

        var keyedTasks = keyedList.Select(async (disposable, index) => {
            try {
                await disposable.DisposeAsync();
                _logger?.LogDebug("Descartado recurso por chave {Index}/{Total}: {Type}",
                    index + 1, keyedList.Length, disposable.GetType().Name);
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Falha ao descartar recurso por chave {Type}", disposable.GetType().Name);
            }
        });

        await Task.WhenAll(syncTasks.Concat(asyncTasks).Concat(keyedTasks));

        _logger?.LogDebug("Concluído o descarte de {Count} recursos.", totalCount);

        _syncDisposables.Clear();
        _asyncDisposables.Clear();
        _keyedDisposables.Clear();
    }

    internal void Unregister(string key) {
        if (_keyedDisposables.TryRemove(key, out var disposable))
            _logger?.LogDebug("Desregistrado descartável com chave {Key}: {Type}", key, disposable.GetType().Name);
    }

    /// <summary>
    ///     Descarta o recurso interno uma única vez (idempotente via <see cref="Interlocked" />) e o
    ///     remove do registro ao final, mesmo que o descarte interno lance.
    /// </summary>
    private sealed class AutoUnregisterWrapper : IAsyncDisposable
    {
        private readonly IAsyncDisposable _inner;
        private readonly string _key;
        private readonly DisposableRegistry _registry;
        private int _disposed;

        public AutoUnregisterWrapper(string key, IAsyncDisposable inner, DisposableRegistry registry) {
            _key = key;
            _inner = inner;
            _registry = registry;
        }

        public async ValueTask DisposeAsync() {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            try {
                await _inner.DisposeAsync();
            }
            finally {
                _registry.Unregister(_key);
            }
        }
    }
}
