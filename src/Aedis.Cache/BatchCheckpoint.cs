using Aedis.Cache.Abstractions;

namespace Aedis.Cache;

/// <summary>
///     Checkpoint de um lote: a linha já processada (<see cref="Checkpoint" />) e o handle de liderança
///     que mantém a exclusividade do lote. Descartar o checkpoint libera o lock — por isso deve ser
///     registrado no <c>IDisposableRegistry</c> ou descartado ao fim do processamento.
/// </summary>
public sealed class BatchCheckpoint(int checkpoint, IAsyncDisposable lockHandle) : IBatchCheckpoint
{
    /// <inheritdoc />
    public int Checkpoint { get; } = checkpoint;

    /// <summary>Libera o lock de liderança do lote ao descartar o checkpoint.</summary>
    public ValueTask DisposeAsync() => lockHandle.DisposeAsync();
}
