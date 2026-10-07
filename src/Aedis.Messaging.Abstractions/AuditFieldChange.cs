namespace Aedis.Messaging.Abstractions;

/// <summary>
///     Mudança de um campo registrada numa <see cref="IAuditableMessage" />. Os valores trafegam como texto
///     já formatado para a trilha; campos sensíveis devem chegar aqui mascarados pelo produtor.
/// </summary>
/// <param name="Field">Nome do campo alterado.</param>
/// <param name="OldValue">Valor anterior, ou <c>null</c> quando o campo não existia.</param>
/// <param name="NewValue">Valor novo, ou <c>null</c> quando o campo foi removido.</param>
public sealed record AuditFieldChange(string Field, string? OldValue, string? NewValue);
