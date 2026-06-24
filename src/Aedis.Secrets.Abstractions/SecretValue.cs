namespace Aedis.Secrets.Abstractions;

/// <summary>
///     Segredo lido de um cofre, com metadados opcionais. <see cref="Version" /> e <see cref="RotatedAt" />
///     são preenchidos pelos providers que os expõem (ex.: <c>VersionId</c> do AWS Secrets Manager); ficam
///     <c>null</c> nos demais.
/// </summary>
/// <param name="Name">Nome/identificador do segredo no cofre.</param>
/// <param name="Value">Valor textual do segredo.</param>
/// <param name="Version">Identificador de versão do segredo, quando o provider o expõe.</param>
/// <param name="RotatedAt">Momento da última rotação/alteração, quando o provider o expõe.</param>
public sealed record SecretValue(string Name, string Value, string? Version, DateTimeOffset? RotatedAt);
