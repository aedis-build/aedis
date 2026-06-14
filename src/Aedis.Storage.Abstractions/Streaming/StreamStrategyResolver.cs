using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Seleciona a estratégia de stream/memória adequada ao <see cref="StreamMode" /> do contexto.
///     Depende apenas das abstrações de strategy (não da implementação), mantendo o pacote agnóstico.
/// </summary>
public sealed class StreamStrategyResolver(IEnumerable<IStrategy<StreamContext>> strategies)
    : IStrategyResolver<StreamContext>
{
    private readonly IReadOnlyList<IStrategy<StreamContext>> _strategies = strategies.ToList();

    public async Task ExecuteAsync(StreamContext context, CancellationToken cancellationToken = default) {
        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(context))
                       ?? throw new NotSupportedException($"StreamMode '{context.Mode}' não suportado.");

        await strategy.ExecuteAsync(context, cancellationToken);
    }
}
