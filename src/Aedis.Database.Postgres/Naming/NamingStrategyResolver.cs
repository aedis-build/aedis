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
    public NamingStrategyResolver(IEnumerable<INamingStrategy> strategies) : base(strategies) { }

    public new INamingStrategy GetStrategy(NamingContext context) {
        var strategy = Strategies.Cast<INamingStrategy>().FirstOrDefault(s => s.CanHandle(context))
                       ?? throw new InvalidOperationException(
                           $"Nenhuma estratégia de nomes registrada para a convenção '{context.Convention}'.");
        return strategy;
    }
}
