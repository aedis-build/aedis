using Aedis.Database.Abstractions;
using Aedis.Domain.Strategy;

namespace Aedis.Database.Postgres.Naming;

/// <summary>
///     Resolve a <see cref="INamingStrategy" /> conforme a <see cref="NamingConvention" /> do
///     <see cref="NamingContext" />. Usado para converter nomes de tabela/coluna nas operações de
///     persistência (inclusive na montagem das colunas do bulk COPY).
/// </summary>
public sealed class NamingStrategyResolver : StrategyResolver<NamingContext>
{
    /// <summary>
    ///     Cria o resolver a partir das <see cref="INamingStrategy" /> registradas (uma por convenção),
    ///     tipicamente injetadas pelo contêiner de DI.
    /// </summary>
    /// <param name="strategies">Estratégias de nomes disponíveis para resolução.</param>
    public NamingStrategyResolver(IEnumerable<INamingStrategy> strategies) : base(strategies) { }

    /// <summary>
    ///     Retorna a <see cref="INamingStrategy" /> que sabe lidar com o contexto (via
    ///     <c>CanHandle</c>), já tipada para dispensar cast. Lança
    ///     <see cref="InvalidOperationException" /> quando nenhuma estratégia atende à convenção.
    /// </summary>
    public new INamingStrategy GetStrategy(NamingContext context) {
        var strategy = Strategies.Cast<INamingStrategy>().FirstOrDefault(s => s.CanHandle(context))
                       ?? throw new InvalidOperationException(
                           $"Nenhuma estratégia de nomes registrada para a convenção '{context.Convention}'.");
        return strategy;
    }
}
