namespace Aedis.AI.ManagedAgents;

/// <summary>
///     Opções do provider Managed Agents (seção <c>AI:ManagedAgents</c>). Sem <see cref="ApiKey" /> o SDK da
///     Anthropic usa a variável <c>ANTHROPIC_API_KEY</c> (ou o perfil do <c>ant auth login</c>); em produção, prefira
///     injetar a chave por um provider de segredos em vez de deixá-la no <c>appsettings</c>.
/// </summary>
public sealed class ManagedAgentsOptions
{
    /// <summary>Nome da seção de configuração (<c>AI:ManagedAgents</c>).</summary>
    public const string SectionName = "AI:ManagedAgents";

    /// <summary>Chave da API da Anthropic; vazio cai para a resolução padrão do SDK.</summary>
    public string? ApiKey { get; set; }
}
