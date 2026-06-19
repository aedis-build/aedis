using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Aedis.Domain.Saga.Abstractions;

namespace Aedis.Domain;

/// <summary>
///     Opções de configuração do Saga Pattern (timeout padrão, verbosidade de log), consumidas pelo
///     <see cref="SagaBuilder" /> ao registrar sagas no container de DI.
/// </summary>
/// <remarks>
///     A persistência de estado de saga em banco (<c>ISagaStateStore → DatabaseSagaStateStore</c>) é glue de
///     infraestrutura e será fornecida por um pacote Tier 3 (ver MIGRATION.md). Em Tier 1 o Saga opera apenas
///     em memória por padrão.
/// </remarks>
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
///     Builder fluente que registra steps de saga no container e ajusta as <see cref="SagaOptions" />.
///     Obtido durante a configuração do DI; encadeie <see cref="AddStep{TStep, TContext}" /> e
///     <see cref="WithTimeout" /> para compor a saga.
/// </summary>
public class SagaBuilder
{
    /// <summary>
    ///     Inicializa o builder com a coleção de serviços alvo e as opções a serem ajustadas; ambas
    ///     obrigatórias.
    /// </summary>
    public SagaBuilder(IServiceCollection services, SagaOptions options) {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Coleção de serviços onde as steps de saga são registradas.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Opções de saga sendo configuradas por este builder.</summary>
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