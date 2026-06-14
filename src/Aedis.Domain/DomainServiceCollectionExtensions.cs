using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain;

// Nota: a persistência de estado de saga em banco (ISagaStateStore → DatabaseSagaStateStore)
// é glue de infraestrutura e será fornecida por um pacote Tier 3 (ver MIGRATION.md).
// Em Tier 1 o Saga opera apenas em memória por padrão.

/// <summary>
///     Opções de configuração para Saga Pattern
/// </summary>
public class SagaOptions
{
    /// <summary>
    ///     Timeout padrão para execução de sagas
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Habilita logs detalhados de execução
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;
}

/// <summary>
///     Builder para configuração fluente de Saga
/// </summary>
public class SagaBuilder
{
    public SagaBuilder(IServiceCollection services, SagaOptions options) {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public SagaOptions Options { get; }

    /// <summary>
    ///     Registra uma step específica no DI
    /// </summary>
    public SagaBuilder AddStep<TStep, TContext>()
        where TStep : class, ISagaStep<TContext>
        where TContext : ISagaContext {
        Services.TryAddScoped<TStep>();
        return this;
    }

    /// <summary>
    ///     Configura timeout customizado
    /// </summary>
    public SagaBuilder WithTimeout(TimeSpan timeout) {
        Options.DefaultTimeout = timeout;
        return this;
    }
}