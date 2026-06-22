using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.App1.Worker.Messaging;

/// <summary>
///     Adapta um handler <c>scoped</c> ao consumidor <c>singleton</c>: cria um escopo de DI por mensagem e
///     resolve o <see cref="IMessageHandler{T}" /> real dentro dele. Evita o problema de dependência cativa
///     (singleton segurando serviços scoped) e garante <c>DbContext</c>/repositórios frescos por mensagem.
/// </summary>
/// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
public sealed class ScopedMessageHandler<T> : IMessageHandler<T> where T : class, IMessage {
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     Cria o adaptador com a fábrica de escopos.
    /// </summary>
    /// <param name="scopeFactory">Fábrica de escopos de DI.</param>
    public ScopedMessageHandler(IServiceScopeFactory scopeFactory) {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task HandleAsync(T message, CancellationToken cancellationToken) {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<T>>();
        await handler.HandleAsync(message, cancellationToken);
    }
}
