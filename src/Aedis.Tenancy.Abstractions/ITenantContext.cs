namespace Aedis.Tenancy.Abstractions;

/// <summary>
///     <see cref="ICurrentTenant" /> que também permite definir o tenant explicitamente — para trabalho fora
///     de HTTP (worker, consumer, mailbox de ator, job) ou para o middleware que resolve o tenant da requisição.
/// </summary>
public interface ITenantContext : ICurrentTenant
{
    /// <summary>
    ///     Define <paramref name="tenantId" /> como tenant do fluxo assíncrono atual até o descarte do retorno,
    ///     quando o tenant anterior (ou nenhum) é restaurado. Escopos podem ser aninhados.
    /// </summary>
    /// <param name="tenantId">Identificador do tenant; vazio ou só espaços é rejeitado.</param>
    /// <returns>Escopo a descartar ao fim do trabalho do tenant.</returns>
    /// <example><code>using (tenantContext.BeginScope(mensagem.TenantId)) await ProcessarAsync(mensagem, ct);</code></example>
    IDisposable BeginScope(string tenantId);
}
