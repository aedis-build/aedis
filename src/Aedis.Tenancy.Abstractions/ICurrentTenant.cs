namespace Aedis.Tenancy.Abstractions;

/// <summary>
///     Tenant (empresa cliente) do fluxo de execução atual — requisição HTTP, mensagem ou job. Contrato de
///     leitura para domínio, aplicação e repositórios. Sem tenant, <see cref="Id" /> é <c>null</c> e todo
///     acesso a dado escopado por tenant deve usar <see cref="RequiredId" />, que falha fechado.
/// </summary>
/// <remarks>
///     A implementação padrão é ambiente (<c>AsyncLocal</c>) e <em>singleton</em>: o valor segue o fluxo
///     assíncrono, não o escopo de DI, então pode ser injetada em serviços singleton (ex.: a fábrica de sessões
///     do banco).
/// </remarks>
public interface ICurrentTenant
{
    /// <summary>Identificador do tenant atual, ou <c>null</c> quando o fluxo não tem tenant.</summary>
    string? Id { get; }

    /// <summary>Indica se o fluxo atual tem tenant definido.</summary>
    bool HasTenant => Id is not null;

    /// <summary>
    ///     Identificador do tenant atual para acesso a dado escopado. Lança <see cref="TenantRequiredException" />
    ///     quando não há tenant — nunca devolve um valor que libere dados de todos os tenants (fail-closed).
    /// </summary>
    /// <example><code>uow.QueryAsync&lt;Pedido&gt;("SELECT * FROM pedidos WHERE tenant_id = @tenant", new { tenant = tenant.RequiredId })</code></example>
    string RequiredId => Id ?? throw new TenantRequiredException();
}
