namespace Aedis.OpenApi.Abstractions;

/// <summary>
///     Risk level of a write operation. <see cref="High" /> and <see cref="Critical" /> mean that an agent
///     executing the operation on behalf of a user needs human approval first.
/// </summary>
public enum RiskLevel
{
    /// <summary>Small, reversible effect (e.g. sending a test webhook).</summary>
    Low,

    /// <summary>Visible to the customer but recoverable (e.g. re-sending notifications).</summary>
    Medium,

    /// <summary>Moves money, guarantees or the destination of events/accounts (e.g. changing a webhook URL).</summary>
    High,

    /// <summary>Irreversible or wide-impact (e.g. closing a contract and releasing its guarantees).</summary>
    Critical
}

/// <summary>
///     Declares the risk of an operation. The <c>Aedis.OpenApi</c> plugin emits it as
///     <c>x-aedis-risk: { "level", "reason" }</c>, so portals show it in the contract and agents use it as the
///     base risk of the action (High/Critical require human approval). Checks that depend on the submitted
///     value stay in the executor's own risk engine. The action attribute wins over the controller's.
/// </summary>
/// <example>
///     <code>
///     [HttpPatch("{id}")]
///     [OperationRisk(RiskLevel.High, "Changing the URL redirects the customer's events.")]
///     public Task&lt;IActionResult&gt; Update(Guid id, UpdateWebhookRequest request) { ... }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class OperationRiskAttribute : Attribute
{
    /// <summary>Declares the risk of the operation.</summary>
    /// <param name="level">Risk level.</param>
    /// <param name="reason">Why the operation carries that risk, in business language.</param>
    public OperationRiskAttribute(RiskLevel level, string reason = "") {
        Level = level;
        Reason = reason;
    }

    /// <summary>Risk level.</summary>
    public RiskLevel Level { get; }

    /// <summary>Reason, in business language; empty when not given.</summary>
    public string Reason { get; }
}
