using Aedis.App1.Application.Notifications.Events;
using Aedis.App1.Application.Notifications.Handlers;
using Aedis.Messaging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro da camada de aplicação: os handlers de mensagem. Mantém o <c>composition root</c> do worker
///     enxuto — ele só chama <c>AddApplication</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions {
    /// <summary>
    ///     Registra os handlers de mensagem da aplicação (escopo por mensagem).
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services) {
        services.AddScoped<IMessageHandler<NotificationRequestedEvent>, NotificationRequestedEventHandler>();
        return services;
    }
}
