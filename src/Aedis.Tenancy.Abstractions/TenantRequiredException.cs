using Aedis.Exceptions;

namespace Aedis.Tenancy.Abstractions;

/// <summary>
///     Lançada quando um acesso a dado escopado por tenant acontece sem tenant no fluxo atual (fail-closed).
///     Especializa <see cref="ForbiddenException" />: no host HTTP vira 403 ProblemDetails; em worker indica
///     erro de programação — o trabalho não abriu um escopo com <see cref="ITenantContext.BeginScope" />.
/// </summary>
public sealed class TenantRequiredException()
    : ForbiddenException("Operação exige um tenant e o fluxo atual não tem nenhum.", requiredPermission: "tenant");
