using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Aedis.OpenApi;

/// <summary>
///     FluentValidation rules of an operation, in the shape of the <c>x-aedis-rules</c> extension: one entry per
///     rule component of each field of the validated body or query/route object, with the message the API
///     returns in the 422.
///     <code>
///     { "field": "endDate", "source": "body", "type": "GreaterThanOrEqual",
///       "message": "End date must be greater than or equal to start date",
///       "parameters": { "comparedTo": "startDate" }, "conditional": true }
///     </code>
///     Child validators (<c>SetValidator</c>) are nested with dotted paths and <c>[]</c> for collections, up to
///     four levels. Only the default rule set is published: automatic validation runs no other.
/// </summary>
internal static class ValidationRules
{
    private const int MaxDepth = 4;
    private static readonly Regex Placeholder = new(@"\{[A-Za-z]+\}", RegexOptions.Compiled);

    /// <summary>
    ///     Rules of the validated types of the operation: the body and the complex query/route objects
    ///     (<c>[FromQuery] Filter filter</c>). <c>null</c> when none has an <see cref="IValidator{T}" />.
    /// </summary>
    public static JsonArray? Of(ApiDescription api, IServiceProvider services) {
        var targets = Targets(api);
        if (targets.Count == 0)
            return null;

        using var scope = services.CreateScope();
        var rules = new JsonArray();
        foreach (var (type, source) in targets)
            if (Resolve(scope.ServiceProvider, type) is { } validator)
                Append(rules, validator, source, prefix: null, depth: 0, scope.ServiceProvider);

        return rules.Count == 0 ? null : rules;
    }

    private static List<(Type Type, string Source)> Targets(ApiDescription api) {
        var targets = new List<(Type, string)>();
        foreach (var parameter in api.ParameterDescriptions) {
            // a query/route object is flattened (one description per property): the validated type is the container
            var type = parameter.ModelMetadata?.ContainerType ?? parameter.Type;
            if (type is null || IsSimple(type) || targets.Any(t => t.Item1 == type))
                continue;

            var source = parameter.Source == BindingSource.Body ? "body" : parameter.Source == BindingSource.Path ? "path" : "query";
            targets.Add((type, source));
        }

        return targets;
    }

    private static bool IsSimple(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
               || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }

    private static IValidator? Resolve(IServiceProvider services, Type type) =>
        services.GetService(typeof(IValidator<>).MakeGenericType(type)) as IValidator;

    private static void Append(JsonArray rules, IValidator validator, string source, string? prefix, int depth, IServiceProvider services) {
        if (depth > MaxDepth)
            return;

        foreach (var rule in validator.CreateDescriptor().Rules) {
            if (!InDefaultRuleSet(rule))
                continue;

            var field = Join(prefix, FieldName(rule), IsCollection(rule));
            foreach (var component in rule.Components) {
                if (component.Validator is IChildValidatorAdaptor child) {
                    if (ChildValidator(child, services) is { } nested)
                        Append(rules, nested, source, field, depth + 1, services);
                    continue;
                }

                rules.Add(Entry(rule, component, field, source));
            }
        }
    }

    private static JsonObject Entry(IValidationRule rule, IRuleComponent component, string? field, string source) {
        var validator = component.Validator;
        var entry = new JsonObject();
        if (field is not null)
            entry["field"] = field;
        entry["source"] = source;
        entry["type"] = Kind(validator);

        var parameters = Parameters(validator);
        entry["message"] = Message(rule, component, parameters);
        if (parameters.Count > 0)
            entry["parameters"] = parameters;
        if (rule.HasCondition || rule.HasAsyncCondition || component.HasCondition || component.HasAsyncCondition)
            entry["conditional"] = true;
        return entry;
    }

    private static string Kind(IPropertyValidator validator) {
        var name = validator.Name;
        if (name.EndsWith("Validator", StringComparison.Ordinal))
            name = name[..^"Validator".Length];
        return name is "Predicate" or "AsyncPredicate" ? "Must" : name;
    }

    private static JsonObject Parameters(IPropertyValidator validator) {
        var parameters = new JsonObject();
        switch (validator) {
            case ILengthValidator length:
                if (length.Min > 0) parameters["min"] = length.Min;
                if (length.Max is > 0 and < int.MaxValue) parameters["max"] = length.Max;
                break;
            case IComparisonValidator comparison:
                if (comparison.MemberToCompare is { } member) parameters["comparedTo"] = JsonName(member);
                else if (Scalar(comparison.ValueToCompare) is { } value) parameters["value"] = value;
                break;
            case IBetweenValidator between:
                if (Scalar(between.From) is { } from) parameters["from"] = from;
                if (Scalar(between.To) is { } to) parameters["to"] = to;
                break;
            case IRegularExpressionValidator regex:
                parameters["pattern"] = regex.Expression;
                break;
        }

        return parameters;
    }

    /// <summary>
    ///     The 422 message without a request: the one from <c>WithMessage(string)</c> or the validator's default,
    ///     with the known placeholders resolved. <c>WithMessage(lambda)</c> depends on the request and falls
    ///     back to the default message; placeholders that depend on the submitted value are dropped.
    /// </summary>
    private static string Message(IValidationRule rule, IRuleComponent component, JsonObject parameters) {
        string template;
        try {
            template = component.GetUnformattedErrorMessage();
        }
        catch {
            template = component.Validator.GetDefaultMessageTemplate(component.ErrorCode);
        }

        var name = DisplayName(rule);
        var message = template
            .Replace("{PropertyName}", name)
            .Replace("{PropertyPath}", name)
            .Replace("{MinLength}", parameters["min"]?.ToString() ?? "")
            .Replace("{MaxLength}", parameters["max"]?.ToString() ?? "")
            .Replace("{ComparisonValue}", (parameters["value"] ?? parameters["comparedTo"])?.ToString() ?? "")
            .Replace("{ComparisonProperty}", parameters["comparedTo"]?.ToString() ?? "")
            .Replace("{From}", parameters["from"]?.ToString() ?? "")
            .Replace("{To}", parameters["to"]?.ToString() ?? "");

        return Placeholder.Replace(message, "").Replace("  ", " ").Trim();
    }

    private static string DisplayName(IValidationRule rule) {
        try {
            return rule.GetDisplayName(null!) ?? rule.PropertyName ?? "the field";
        }
        catch {
            return rule.PropertyName ?? "the field";
        }
    }

    private static bool InDefaultRuleSet(IValidationRule rule) =>
        rule.RuleSets is not { Length: > 0 } sets
        || sets.Any(s => string.Equals(s, RulesetValidatorSelector.DefaultRuleSetName, StringComparison.OrdinalIgnoreCase));

    private static bool IsCollection(IValidationRule rule) =>
        rule.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionRule<,>));

    private static string? FieldName(IValidationRule rule) =>
        rule.Member is { } member ? JsonName(member)
        : string.IsNullOrEmpty(rule.PropertyName) ? null
        : JsonNamingPolicy.CamelCase.ConvertName(rule.PropertyName);

    private static string? Join(string? prefix, string? field, bool collection) {
        if (field is not null && collection)
            field += "[]";
        if (prefix is null)
            return field;
        return field is null ? prefix : $"{prefix}.{field}";
    }

    private static string JsonName(MemberInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name);

    private static JsonNode? Scalar(object? value) => value switch {
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        decimal d => d,
        double d => d,
        DateOnly d => d.ToString("yyyy-MM-dd"),
        DateTime d => d.ToString("O"),
        Enum e => e.ToString(),
        _ => null
    };

    /// <summary>
    ///     Child validator of <c>SetValidator</c>: the declared type when it has a parameterless constructor,
    ///     otherwise resolved from the container by the validated type.
    /// </summary>
    private static IValidator? ChildValidator(IChildValidatorAdaptor child, IServiceProvider services) {
        var type = child.ValidatorType;
        if (type.GetConstructor(Type.EmptyTypes) is not null)
            return Activator.CreateInstance(type) as IValidator;

        var validated = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            ?.GetGenericArguments()[0];
        return validated is null ? null : Resolve(services, validated);
    }
}
