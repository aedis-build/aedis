namespace Aedis.Cache.Abstractions;

/// <summary>
///     Ponto de retomada de um lote: a linha já processada (<see cref="Checkpoint" />) acoplada ao handle de
///     liderança que mantém a exclusividade. Descartá-lo libera o lock — registre-o no
///     <c>IDisposableRegistry</c> ou descarte ao fim do processamento.
/// </summary>
public interface IBatchCheckpoint : IAsyncDisposable
{
    /// <summary>Última linha processada do lote; comece a partir dela ao retomar (0 quando ainda não há progresso).</summary>
    int Checkpoint { get; }
}