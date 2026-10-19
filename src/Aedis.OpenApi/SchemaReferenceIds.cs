using Aedis.Hypermedia.Abstractions;

namespace Aedis.OpenApi;

/// <summary>
///     Component schema ids used by the document: <c>Resource&lt;T&gt;</c> → <c>{T}Resource</c>,
///     <c>ResourceCollection&lt;T&gt;</c> → <c>{T}ResourceCollection</c>, other generics
///     <c>{Base}{Args}</c>, non-generic types by name. Shared by the generator's
///     <c>CreateSchemaReferenceId</c> and by the synthesized QUERY operations, so both resolve the same ids.
/// </summary>
internal static class SchemaReferenceIds
{
    public static string For(Type type) {
        if (!type.IsGenericType)
            return type.Name;

        var definition = type.GetGenericTypeDefinition();
        var baseName = definition.Name.Split('`')[0];
        var arguments = string.Concat(type.GetGenericArguments().Select(For));

        return definition == typeof(Resource<>) || definition == typeof(ResourceCollection<>)
            ? $"{arguments}{baseName}"
            : $"{baseName}{arguments}";
    }
}
