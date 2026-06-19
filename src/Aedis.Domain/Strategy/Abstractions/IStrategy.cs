namespace Aedis.Domain.Strategy.Abstractions;

/// <summary>
///     Estratégia do padrão Strategy: encapsula um algoritmo selecionável em tempo de execução conforme o
///     contexto. Implemente <see cref="CanHandle" /> para indicar se trata o contexto e
///     <see cref="ExecuteAsync" /> para a lógica; o resolver escolhe a estratégia adequada via
///     <see cref="CanHandle" /> (seleção O(n)).
/// </summary>
/// <typeparam name="TContext">Tipo do contexto avaliado e processado.</typeparam>
public interface IStrategy<in TContext>
{
    /// <summary>Indica se esta estratégia é capaz de tratar o contexto informado.</summary>
    bool CanHandle(TContext context);

    /// <summary>Executa o algoritmo da estratégia sobre o contexto.</summary>
    Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}