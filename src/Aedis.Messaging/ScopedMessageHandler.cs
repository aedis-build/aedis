using Aedis.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.Messaging;

/// <summary>
///     Adapta um <see cref="IMessageHandler{T}" /> registrado como <c>scoped</c> (ou <c>transient</c>) ao
///     consumer <c>singleton</c>: abre um escopo de DI por mensagem e resolve o handler real dentro dele.
///     Evita dependência cativa (um <c>DbContext</c> ou unidade de trabalho presos num singleton) e garante
///     serviços frescos a cada mensagem. Registrado automaticamente por <c>AddAedisMessageConsumer</c>.
/// </summary>
/// <typeparam name="T">Tipo da mensagem consumida.</typeparam>
public sealed class ScopedMessageHandler<T> : IMessageHandler<T> where T : class, IMessage
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Cria o adaptador sobre a fábrica de escopos do contêiner.</summary>
    public ScopedMessageHandler(IServiceScopeFactory scopeFactory) {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public async Task HandleAsync(T message, CancellationToken cancellationToken) {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<T>>();
        await handler.HandleAsync(message, cancellationToken);
    }
}
