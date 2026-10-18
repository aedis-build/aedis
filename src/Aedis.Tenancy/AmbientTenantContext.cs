using Aedis.Tenancy.Abstractions;

namespace Aedis.Tenancy;

/// <summary>
///     <see cref="ITenantContext" /> ambiente: o tenant vive num <see cref="AsyncLocal{T}" /> e segue o fluxo
///     assíncrono (inclusive <c>await</c> e <c>Task.Run</c>), isolado entre requisições e mensagens
///     concorrentes. Registrar como <em>singleton</em>.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    private readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string? Id => _current.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(string tenantId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var previous = _current.Value;
        _current.Value = tenantId;
        return new Scope(this, previous);
    }

    private sealed class Scope(AmbientTenantContext owner, string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose() {
            if (_disposed) return;
            owner._current.Value = previous;
            _disposed = true;
        }
    }
}
