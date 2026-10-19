using Aedis.Hosting.AspNetCore.ServerSentEvents;
using Aedis.Core.Utils;
using Aedis.Observability.Serilog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using Serilog;

namespace Aedis.Hosting.AspNetCore;

/// <summary>
///     Classe-base de um host de API REST seguro por construção. A aplicação herda, sobrescreve no mínimo
///     <see cref="ConfigureServices" /> e chama <see cref="RunAsync" /> no <c>Main</c>. O host compõe por
///     default toda a camada preparatória — logging estruturado (Serilog), telemetria (OTLP), diagnósticos
///     e health checks, a camada de segurança HTTP (<c>Aedis.Security.Web</c>), tratamento de erros em
///     ProblemDetails, validação 422 e o portão de autenticação fail-closed. Documentação OpenAPI e hipermídia
///     são plugins opt-in (<c>Aedis.OpenApi</c> + <c>Aedis.OpenApi.Scalar</c>, <c>Aedis.Hypermedia.AspNetCore</c>),
///     registrados pela aplicação em <see cref="ConfigureServices" />/<see cref="ConfigureMiddleware" />.
/// </summary>
/// <remarks>
///     Padrão Template Method: os pontos <c>protected virtual</c> permitem customizar sem reescrever o
///     bootstrap. O portão de segurança recusa subir com autenticação desabilitada fora de Development
///     (fail-closed), em linha com OWASP A01/A07.
/// </remarks>
public abstract class AedisApiHost
{
    /// <summary>
    ///     Registra os serviços específicos da aplicação (repositórios, casos de uso, clientes). Chamado após
    ///     o host registrar toda a infraestrutura padrão, podendo sobrescrevê-la se necessário.
    /// </summary>
    protected abstract void ConfigureServices(IConfiguration configuration, IServiceCollection services);

    /// <summary>
    ///     Registra o esquema de autenticação (ex.: <c>AddAedisKeycloakAuth</c>). Chamado apenas quando
    ///     <see cref="EnableAuthentication" /> é <c>true</c>. Se nada for registrado, a política de fallback
    ///     exige usuário autenticado e toda requisição é negada (fail-closed).
    /// </summary>
    protected virtual void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Define políticas de autorização nomeadas usadas pelos controllers.</summary>
    protected virtual void ConfigureAuthorization(AuthorizationOptions options) { }

    /// <summary>Adiciona health checks específicos da aplicação além dos padrão (self/uptime/shutdown).</summary>
    protected virtual void AddCustomHealthChecks(IHealthChecksBuilder builder) { }

    /// <summary>Adiciona exportadores de métricas extras (ex.: Prometheus) ao pipeline de telemetria.</summary>
    protected virtual void ConfigureMetricsExporters(MeterProviderBuilder builder) { }

    /// <summary>Insere middlewares próprios da aplicação, após a autenticação/autorização e antes dos endpoints.</summary>
    protected virtual void ConfigureMiddleware(WebApplication app) { }

    /// <summary>Quando <c>true</c> (default), liga autenticação + portão fail-closed. Veja <see cref="ConfigureAuthentication" />.</summary>
    protected virtual bool EnableAuthentication => true;

    /// <summary>Quando <c>true</c> (default), liga a telemetria OTLP (métricas/traces).</summary>
    protected virtual bool EnableTelemetry => true;

    /// <summary>
    ///     Quando <c>true</c>, liga a multi-tenancy: registra <c>AddAedisTenancy</c> (tenant da claim
    ///     <c>Tenancy:ClaimType</c>, trocável por outro <c>ITenantResolver</c>) e resolve o tenant de cada
    ///     requisição logo após a autorização. Default <c>false</c>.
    /// </summary>
    protected virtual bool EnableTenancy => false;

    /// <summary>
    ///     Costura para fontes de configuração adicionais, executada antes de a infraestrutura ler a configuração:
    ///     é aqui que um app liga segredos do cofre como configuração (<c>AddAedisSecrets</c>) ou um provedor
    ///     remoto. Os providers padrão (appsettings, ambiente, linha de comando) já estão em
    ///     <paramref name="configuration" />.
    /// </summary>
    /// <param name="configuration">Builder de configuração do host.</param>
    /// <param name="environment">Ambiente de hospedagem (nome, raiz de conteúdo).</param>
    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder configuration, IHostEnvironment environment) { }

    /// <summary>
    ///     Constrói, configura e executa a aplicação web até o cancelamento. Cria o logger raiz, compõe a
    ///     infraestrutura segura por default, monta o pipeline e bloqueia em <c>RunAsync</c>. Erros fatais de
    ///     inicialização são logados e propagados; o logger é drenado no encerramento.
    /// </summary>
    /// <param name="args">Argumentos de linha de comando recebidos pelo <c>Main</c>.</param>
    /// <param name="cancellationToken">Token que sinaliza o encerramento gracioso do host.</param>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default) {
        try {
            var app = BuildApplication(args);
            await app.RunAsync(cancellationToken);
        }
        catch (Exception exception) {
            Log.Fatal(exception, "Falha fatal ao inicializar o host da API.");
            throw;
        }
        finally {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    ///     Constrói o <see cref="WebApplication" /> totalmente configurado (infra, segurança, pipeline) sem
    ///     executá-lo. Costura de teste: <paramref name="configureBuilder" /> roda logo após a criação do
    ///     builder — antes da configuração de segurança — permitindo aos testes ajustar ambiente, configuração
    ///     e servidor (ex.: <c>UseTestServer</c>). Em produção, <see cref="RunAsync" /> o invoca e executa.
    /// </summary>
    internal WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder = null) {
        var builder = WebApplication.CreateBuilder(args);
        Log.Logger = AedisSerilog.CreateLogger(builder.Configuration);

        configureBuilder?.Invoke(builder);
        ConfigureAppConfiguration(builder.Configuration, builder.Environment);

        builder.ConfigureAedisKestrelHardening();
        ConfigureInfrastructure(builder);
        ConfigureSecurity(builder);
        ConfigureServices(builder.Configuration, builder.Services);

        var app = builder.Build();
        BuildPipeline(app);

        return app;
    }

    private void ConfigureInfrastructure(WebApplicationBuilder builder) {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddAedisSerilog(configuration);
        services.AddAedisBusinessEvents(configuration);

        if (EnableTelemetry)
            services.AddAedisTelemetry(configuration, ConfigureMetricsExporters);

        services.AddAedisDiagnostics();
        AddCustomHealthChecks(services.AddHealthChecks());

        services.AddResponseCompression(options => options.EnableForHttps = true);
        services.AddOptions<ServerSentEventsOptions>()
            .BindConfiguration(ServerSentEventsOptions.SectionName)
            .Validate(sse => sse.HeartbeatInterval > TimeSpan.Zero && sse.MaxConnectionDuration > TimeSpan.Zero,
                "ServerSentEventsOptions.HeartbeatInterval e MaxConnectionDuration devem ser positivos.")
            .ValidateOnStart();

        services.AddAedisProblemDetails();
        services.AddAedisCommands();
        services.AddControllers()
            .AddJsonOptions(options => SystemJsonOptionsFactory.Configure(options.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(options => SystemJsonOptionsFactory.Configure(options.SerializerOptions));
        services.AddAedisApiValidation();

        if (EnableTenancy)
            services.AddAedisTenancy(configuration);
    }

    private void ConfigureSecurity(WebApplicationBuilder builder) {
        var services = builder.Services;

        services.AddAedisWebSecurity(builder.Configuration);

        if (EnableAuthentication) {
            ConfigureAuthentication(services, builder.Configuration);
            services.AddAuthorization(options => {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                ConfigureAuthorization(options);
            });
            return;
        }

        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Autenticação desabilitada fora de Development: o host recusa subir inseguro (fail-closed). " +
                "Habilite EnableAuthentication e configure um provedor, ou execute em ambiente de desenvolvimento.");

        services.AddAuthorization(options => {
            var allowAll = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
            options.FallbackPolicy = allowAll;
            options.DefaultPolicy = allowAll;
            ConfigureAuthorization(options);
        });
    }

    private void BuildPipeline(WebApplication app) {
        app.UseAedisExceptionHandling();
        app.UseAedisRequestLogging(app.Configuration);
        app.UseResponseCompression();
        app.UseAedisWebSecurity();

        if (EnableAuthentication)
            app.UseAuthentication();

        app.UseAuthorization();

        if (EnableTenancy)
            app.UseAedisTenancy();

        ConfigureMiddleware(app);

        app.MapAedisHealthChecks();
        app.MapControllers();
    }
}
