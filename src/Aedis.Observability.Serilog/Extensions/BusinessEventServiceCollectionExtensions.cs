using Aedis.Observability.Abstractions;
using Aedis.Observability.Serilog;
using Aedis.Observability.Serilog.BusinessEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registro de DI dos eventos de negócio do Aedis.</summary>
public static class BusinessEventServiceCollectionExtensions
{
    /// <summary>
    ///     Registra <see cref="IBusinessEventLogger" /> lendo a seção <c>Observability:BusinessEvents</c> e a
    ///     política de redaction de <c>Logging:Redaction</c> (usada para mascarar os atributos entregues aos
    ///     sinks). Os hosts do Aedis já chamam; use direto em testes ou hosts próprios. Sinks adicionais
    ///     (ex.: a trilha de auditoria de <c>Aedis.Messaging</c>) registram-se como <see cref="IBusinessEventSink" />.
    /// </summary>
    public static IServiceCollection AddAedisBusinessEvents(this IServiceCollection services, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BusinessEventOptions>().Bind(configuration.GetSection(BusinessEventOptions.SectionName));
        services.TryAddSingleton(RedactionOptions.FromConfiguration(configuration));
        services.TryAddSingleton<IBusinessEventLogger, BusinessEventLogger>();
        return services;
    }
}
