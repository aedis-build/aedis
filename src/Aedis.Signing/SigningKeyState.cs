using Aedis.Signing.Abstractions;

namespace Aedis.Signing;

/// <summary>
///     Estado compartilhado do bootstrap da chave: quem assina aguarda aqui; o health check <c>ready</c> lê
///     daqui. Singleton; os providers de chave publicam nele via <see cref="SetReady" />.
/// </summary>
public sealed class SigningKeyState
{
    private readonly TaskCompletionSource<SigningKeyHandle> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Chave resolvida, ou <c>null</c> enquanto o bootstrap não terminou (ou falhou).</summary>
    public SigningKeyHandle? Handle { get; private set; }

    /// <summary>Falha permanente do bootstrap (host abortado ou abortando).</summary>
    public Exception? Fault { get; private set; }

    /// <summary>Chave resolvida no boot mas rejeitada depois (desabilitada/excluída em runtime).</summary>
    public SigningException? Degraded { get; private set; }

    /// <summary>Verdadeiro quando há chave e ela não foi degradada.</summary>
    public bool IsReady => Handle is not null && Degraded is null;

    /// <summary>Aguarda a chave (ou a falha) do bootstrap.</summary>
    public Task<SigningKeyHandle> WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);

    /// <summary>Chave já resolvida, sem esperar.</summary>
    public bool TryGet(out SigningKeyHandle? handle) {
        handle = Handle;
        return handle is not null;
    }

    /// <summary>Publica a chave resolvida e libera quem aguardava.</summary>
    public void SetReady(SigningKeyHandle handle) {
        ArgumentNullException.ThrowIfNull(handle);
        Handle = handle;
        Degraded = null;
        _ready.TrySetResult(handle);
    }

    /// <summary>Registra a falha permanente do bootstrap e propaga a quem aguardava.</summary>
    public void SetFaulted(Exception exception) {
        ArgumentNullException.ThrowIfNull(exception);
        Fault = exception;
        _ready.TrySetException(exception);
    }

    /// <summary>Marca degradação detectada em tempo de uso (readiness cai; a chave em memória fica para diagnóstico).</summary>
    public void MarkDegraded(SigningException exception) => Degraded = exception;
}
