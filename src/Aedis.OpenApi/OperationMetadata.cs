using System.Text.Json.Nodes;
using Aedis.OpenApi.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.OpenApi;

/// <summary>
///     Authorization, risk and validation metadata of an operation, in the shape of the
///     <c>x-aedis-roles</c>, <c>x-aedis-anonymous</c>, <c>x-aedis-risk</c> and <c>x-aedis-rules</c> extensions.
///     Shared by the operation transformers and by the QUERY document transformer, which synthesizes its
///     operations outside the generator and therefore applies the same extensions by hand.
/// </summary>
internal static class OperationMetadata
{
    public const string Roles = "x-aedis-roles";
    public const string Anonymous = "x-aedis-anonymous";
    public const string Risk = "x-aedis-risk";
    public const string Rules = "x-aedis-rules";

    /// <summary>
    ///     Roles required by the endpoint: those of <c>[Authorize(Roles)]</c> plus the role requirements of the
    ///     policies resolved through <see cref="IAuthorizationPolicyProvider" /> (the real roles, not the policy
    ///     name). Anonymous when <c>[AllowAnonymous]</c> is present; empty when only authentication is required.
    /// </summary>
    public static async Task<(bool Anonymous, IReadOnlyList<string> Roles)> AuthorizationAsync(ApiDescription api, IServiceProvider services) {
        var metadata = api.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
            return (true, []);

        var provider = services.GetService<IAuthorizationPolicyProvider>();
        var roles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var data in metadata.OfType<IAuthorizeData>()) {
            roles.UnionWith((data.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (provider is null || string.IsNullOrWhiteSpace(data.Policy))
                continue;

            var policy = await provider.GetPolicyAsync(data.Policy);
            foreach (var requirement in policy?.Requirements.OfType<RolesAuthorizationRequirement>() ?? [])
                roles.UnionWith(requirement.AllowedRoles);
        }

        return (false, roles.ToList());
    }

    /// <summary>Risk declared with <see cref="OperationRiskAttribute" />; the action's (listed last) wins over the controller's.</summary>
    public static JsonObject? RiskOf(ApiDescription api) {
        if (api.ActionDescriptor.EndpointMetadata.OfType<OperationRiskAttribute>().LastOrDefault() is not { } risk)
            return null;

        var json = new JsonObject { ["level"] = risk.Level.ToString().ToUpperInvariant() };
        if (!string.IsNullOrWhiteSpace(risk.Reason))
            json["reason"] = risk.Reason;
        return json;
    }

    /// <summary>Writes the three extensions on a synthesized (QUERY) operation.</summary>
    public static async Task ApplyAsync(JsonObject operation, ApiDescription api, IServiceProvider services) {
        var (anonymous, roles) = await AuthorizationAsync(api, services);
        if (anonymous)
            operation[Anonymous] = true;
        else if (roles.Count > 0)
            operation[Roles] = new JsonArray(roles.Select(role => (JsonNode)role).ToArray());

        if (RiskOf(api) is { } risk)
            operation[Risk] = risk;

        if (ValidationRules.Of(api, services) is { } rules)
            operation[Rules] = rules;
    }
}
