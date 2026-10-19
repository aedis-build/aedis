namespace Aedis.Payments.Abstractions;

/// <summary>
///     Dados para criar o cliente pagador de um tenant. <paramref name="Account" /> escolhe a conta do provedor (ex.:
///     conta Brasil para empresas brasileiras, conta global para o resto); o tenant vai como metadado para auditoria.
/// </summary>
/// <param name="Account">Nome da conta configurada no provider.</param>
/// <param name="TenantId">Id do tenant dono do cliente (gravado como metadado <c>aedis_tenant_id</c>).</param>
/// <param name="Email">E-mail de cobrança (recibos do provedor).</param>
/// <param name="Name">Nome ou razão social exibido nas faturas.</param>
public sealed record NewCustomer(string Account, string TenantId, string Email, string? Name = null);
