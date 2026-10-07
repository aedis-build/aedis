using Aedis.Core.Utils;
using Aedis.Signing;
using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Registro de DI do módulo de assinatura (seção <c>Signing</c>): chave ECDSA P-256 resolvida na subida
///     por hosted service, <see cref="IHttpMessageSigner" /> (RFC 9421), <see cref="IJwksProvider" /> e health
///     check <c>signing</c> (tag <c>ready</c>). O provider embutido é a chave em processo; pacotes de provider
///     registram outros pelo <see cref="SigningBuilder" />. A rota JWKS é mapeada pela aplicação
///     (<c>MapAedisSigningJwks</c>, em <c>Aedis.Signing.AspNetCore</c>).
/// </summary>
public static class SigningServiceCollectionExtensions
{
    /// <summary>Nome do health check registrado (tag <c>ready</c>).</summary>
    public const string HealthCheckName = "signing";

    /// <summary>
    ///     Registra o módulo de assinatura lendo a seção <c>Signing</c>. Com <c>Signing:Enabled=false</c> expõe
    ///     um signer desligado e não registra bootstrap nem health check.
    /// </summary>
    public static SigningBuilder AddAedisSigning(this IServiceCollection services, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SigningOptions>()
            .Bind(configuration.GetSection(SigningOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SigningOptions>, SigningOptionsValidator>());

        services.PostConfigure<SigningOptions>(o =>
            o.ApplicationName = string.IsNullOrWhiteSpace(o.ApplicationName) ? DeriveApplicationName(ApplicationInfo.Name) : o.ApplicationName.Trim());

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<SigningOptions>>().Value);
        services.TryAddSingleton<SigningKeyState>();
        services.TryAddSingleton<InProcessSigningKeyProvider>();

        services.TryAddSingleton<ISigningKeyProvider>(sp => ResolveKeyProvider(sp, sp.GetRequiredService<SigningOptions>().Provider));
        services.TryAddSingleton<ISignatureProvider>(sp => ResolveSignatureProvider(sp, sp.GetRequiredService<SigningOptions>().Provider));

        services.TryAddSingleton<IHttpMessageSigner>(sp => {
            var options = sp.GetRequiredService<SigningOptions>();
            if (!options.Enabled) return DisabledHttpMessageSigner.Instance;

            return new HttpMessageSigner(
                sp.GetRequiredService<ISigningKeyProvider>(),
                sp.GetRequiredService<ISignatureProvider>(),
                options,
                sp.GetRequiredService<SigningKeyState>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetService<TimeProvider>());
        });

        services.TryAddSingleton<IJwksProvider, JwksProvider>();

        var enabled = configuration.GetValue<bool?>($"{SigningOptions.SectionName}:Enabled") ?? true;
        if (enabled) {
            services.AddHostedService<SigningKeyStartupService>();
            services.AddHealthChecks()
                .AddCheck<SigningKeyHealthCheck>(HealthCheckName, tags: ["ready"], timeout: TimeSpan.FromSeconds(10));
        }

        return new SigningBuilder(services);
    }

    /// <summary>
    ///     <c>loja.pedidos.api</c> → <c>loja-pedidos</c>: remove o sufixo de host (API e worker do mesmo serviço
    ///     compartilham a chave) e normaliza separadores.
    /// </summary>
    public static string DeriveApplicationName(string applicationName) {
        if (string.IsNullOrWhiteSpace(applicationName)) return "aedis";

        var parts = applicationName.Trim().ToLowerInvariant().Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).ToList();
        string[] hostSuffixes = ["api", "worker", "web", "jobs", "job", "consumer", "host", "service"];
        if (parts.Count > 1 && hostSuffixes.Contains(parts[^1])) parts.RemoveAt(parts.Count - 1);

        return parts.Count == 0 ? "aedis" : string.Join("-", parts);
    }

    private static ISigningKeyProvider ResolveKeyProvider(IServiceProvider sp, string provider) =>
        string.Equals(provider, SigningKeyProviders.InProcess, StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<InProcessSigningKeyProvider>()
            : sp.GetKeyedService<ISigningKeyProvider>(provider)
              ?? throw new InvalidOperationException(
                  $"Provider de chave de assinatura '{provider}' não registrado. Referencie o pacote do provider e chame a extensão dele no SigningBuilder.");

    private static ISignatureProvider ResolveSignatureProvider(IServiceProvider sp, string provider) =>
        string.Equals(provider, SigningKeyProviders.InProcess, StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<InProcessSigningKeyProvider>()
            : sp.GetKeyedService<ISignatureProvider>(provider)
              ?? throw new InvalidOperationException(
                  $"Provedor de assinatura '{provider}' não registrado. Referencie o pacote do provider e chame a extensão dele no SigningBuilder.");
}
