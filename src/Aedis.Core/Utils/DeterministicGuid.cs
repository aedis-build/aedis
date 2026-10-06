using UUIDNext;

namespace Aedis.Core.Utils;

/// <summary>
///     Deterministic, name-based GUIDs (RFC 4122 version 5): the same namespace and name always produce the
///     same value, which makes them the right choice for idempotent identifiers derived from business keys —
///     child rows of a bulk operation, natural keys mapped to surrogate ids, or de-duplication keys — instead of
///     composite primary keys.
/// </summary>
public static class DeterministicGuid
{
    private const char PartSeparator = (char)0x1F;

    /// <summary>Well-known namespaces from RFC 4122 Appendix C.</summary>
    public static class Namespaces
    {
        /// <summary>Namespace for fully-qualified domain names.</summary>
        public static readonly Guid Dns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>Namespace for URLs.</summary>
        public static readonly Guid Url = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>Namespace for ISO OIDs.</summary>
        public static readonly Guid Oid = new("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>Namespace for X.500 distinguished names.</summary>
        public static readonly Guid X500 = new("6ba7b814-9dad-11d1-80b4-00c04fd430c8");
    }

    /// <summary>Creates the version-5 GUID for <paramref name="name" /> inside <paramref name="namespaceId" />.</summary>
    /// <param name="namespaceId">Namespace that scopes the name; different namespaces never collide for the same name.</param>
    /// <param name="name">Name to hash; must not be <c>null</c>.</param>
    /// <returns>A stable GUID for the pair.</returns>
    public static Guid Create(Guid namespaceId, string name) {
        ArgumentNullException.ThrowIfNull(name);
        return Uuid.NewNameBased(namespaceId, name);
    }

    /// <summary>
    ///     Creates the version-5 GUID for a composite name. Parts are joined with a control separator so that
    ///     <c>("ab", "c")</c> and <c>("a", "bc")</c> never produce the same value; order is significant.
    /// </summary>
    /// <param name="namespaceId">Namespace that scopes the composite name.</param>
    /// <param name="parts">Ordered parts of the name; none may be <c>null</c>.</param>
    /// <returns>A stable GUID for the composite name.</returns>
    public static Guid Create(Guid namespaceId, params string[] parts) {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
            throw new ArgumentException("At least one part is required.", nameof(parts));

        foreach (var part in parts)
            ArgumentNullException.ThrowIfNull(part, nameof(parts));

        return Uuid.NewNameBased(namespaceId, string.Join(PartSeparator, parts));
    }
}
