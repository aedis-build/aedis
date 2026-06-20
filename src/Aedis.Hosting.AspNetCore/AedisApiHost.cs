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
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aedis.Hosting.AspNetCore;

/// <summary>
///     Classe-base de um host de API REST seguro por construção. A aplicação herda, sobrescreve no mínimo
///     <see cref="ConfigureServices" /> e chama <see cref="RunAsync" /> no <c>Main</c>. O host compõe por
///     default toda a camada preparatória — logging estruturado (Serilog), telemetria (OTLP), diagnósticos
///     e health checks, a camada de segurança HTTP (<c>Aedis.Security.Web</c>), tratamento de erros em
///     ProblemDetails, validação 422 e o portão de autenticação fail-closed. O Swagger é opt-in
///     (<see cref="EnableSwagger" />).
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

    /// <summary>Ajusta a geração do documento Swagger quando <see cref="EnableSwagger" /> é <c>true</c>.</summary>
    protected virtual void ConfigureSwagger(SwaggerGenOptions options) { }

    /// <summary>Insere middlewares próprios da aplicação, após a autenticação/autorização e antes dos endpoints.</summary>
    protected virtual void ConfigureMiddleware(WebApplication app) { }

    /// <summary>Quando <c>true</c> (default), liga autenticação + portão fail-closed. Veja <see cref="ConfigureAuthentication" />.</summary>
    protected virtual bool EnableAuthentication => true;

    /// <summary>Quando <c>true</c> (default), liga a telemetria OTLP (métricas/traces).</summary>
    protected virtual bool EnableTelemetry => true;

    /// <summary>Quando <c>true</c>, expõe o Swagger. Default <c>false</c> — opt-in, mantendo a superfície mínima.</summary>
    protected virtual bool EnableSwagger => false;

    /// <summary>
    ///     Constrói, configura e executa a aplicação web até o cancelamento. Cria o logger raiz, compõe a
    ///     infraestrutura segura por default, monta o pipeline e bloqueia em <c>RunAsync</c>. Erros fatais de
    ///     inicialização são logados e propagados; o logger é drenado no encerramento.
    /// </summary>
    /// <param name="args">Argumentos de linha de comando recebidos pelo <c>Main</c>.</param>
    /// <param name="cancellationToken">Token que sinaliza o encerramento gracioso do host.</param>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default) {
        var builder = WebApplication.CreateBuilder(args);
        Log.Logger = AedisSerilog.CreateLogger(builder.Configuration);

        try {
            builder.ConfigureAedisKestrelHardening();
            ConfigureInfrastructure(builder);
            ConfigureSecurity(builder);
            ConfigureServices(builder.Configuration, builder.Services);

            var app = builder.Build();
            BuildPipeline(app);

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

    private void ConfigureInfrastructure(WebApplicationBuilder builder) {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddAedisSerilog(configuration);

        if (EnableTelemetry)
            services.AddAedisTelemetry(configuration, ConfigureMetricsExporters);

        services.AddAedisDiagnostics();
        AddCustomHealthChecks(services.AddHealthChecks());

        services.AddResponseCompression(options => options.EnableForHttps = true);

        services.AddAedisProblemDetails();
        services.AddControllers();
        services.AddAedisApiValidation();

        if (EnableSwagger)
            services.AddAedisSwagger(configuration, ApplicationInfo.DisplayName, ConfigureSwagger);
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
        app.UseResponseCompression();
        app.UseAedisWebSecurity();

        if (EnableAuthentication)
            app.UseAuthentication();

        app.UseAuthorization();

        ConfigureMiddleware(app);

        if (EnableSwagger)
            app.UseAedisSwagger(app.Configuration);

        app.MapAedisHealthChecks();
        app.MapControllers();
    }
}
