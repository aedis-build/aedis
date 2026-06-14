namespace Aedis.Domain.Saga.Abstractions;

/// <summary>
///     Factory para criação de instâncias de Saga
/// </summary>
public interface ISagaFactory
{
    /// <summary>
    ///     Cria uma nova instância de saga com ID opcional
    /// </summary>
    ISaga<TContext> CreateSaga<TContext>(Guid? sagaId = null) where TContext : ISagaContext;
}