namespace Aedis.Messaging.IbmMq;

/// <summary>
///     Teto duro de conexões (canais) que um processo abre num queue manager, contando publisher e consumers.
///     A reserva acontece na <strong>alocação</strong>, não no empréstimo: o publisher reserva o seu naco ao
///     subir e cada fila reserva os seus workers ao assinar. É o que torna o teto uma garantia — workers ficam
///     com a conexão presa durante o GET em espera, então um pool compartilhado faria o publish esperar por
///     eles. Quando não cabe, <see cref="Reserve" /> lança com a aritmética na mensagem: degradar em silêncio
///     (menos workers, fila sem consumer) esconderia o que precisa estar visível no deploy.
/// </summary>
public sealed class IbmMqConnectionBudget
{
    private readonly object _lock = new();
    private int _granted;

    /// <summary>Cria o teto com <paramref name="total" /> conexões (mínimo 1).</summary>
    public IbmMqConnectionBudget(int total) => Total = Math.Max(1, total);

    /// <summary>Teto configurado.</summary>
    public int Total { get; }

    /// <summary>Quanto já foi reservado (publisher + workers de todos os consumers ativos).</summary>
    public int Granted {
        get {
            lock (_lock) return _granted;
        }
    }

    /// <summary>Quanto ainda cabe.</summary>
    public int Available {
        get {
            lock (_lock) return Total - _granted;
        }
    }

    /// <summary>
    ///     Reserva <paramref name="desired" /> conexões (mínimo 1) para <paramref name="owner" /> — o publisher
    ///     ou uma fila — e devolve a quantidade reservada.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não cabe no que resta do teto.</exception>
    public int Reserve(string owner, int desired) {
        var requested = Math.Max(1, desired);

        lock (_lock) {
            var available = Total - _granted;
            if (requested > available)
                throw new InvalidOperationException(
                    $"IBM MQ: {owner} pediu {requested} conexão(ões) e só há {available} livre(s) no teto de {Total} " +
                    $"(IBMMQ:MaxConnections); já reservadas: {_granted}. Cada conexão é um canal no queue manager, " +
                    "então o teto é por processo e proposital: ajuste IBMMQ:PublisherPoolSize e " +
                    "IBMMQ:ConsumerConcurrency/QueueConcurrency para que Σ(concorrência de cada fila) + PublisherPoolSize " +
                    "caiba em MaxConnections, ou aumente MaxConnections sabendo que isso multiplica por réplica.");

            _granted += requested;
            return requested;
        }
    }

    /// <summary>Devolve <paramref name="amount" /> conexões ao teto — um consumer que para libera os workers dele.</summary>
    public void Release(int amount) {
        if (amount <= 0) return;

        lock (_lock) _granted = Math.Max(0, _granted - amount);
    }
}
