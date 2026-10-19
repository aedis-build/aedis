using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Aedis.OpenApi;

/// <summary>
///     Reads the application's XML documentation (<c>{assembly}.xml</c> in <see cref="AppContext.BaseDirectory" />,
///     as produced by <c>GenerateDocumentationFile</c>) and resolves members by their standard doc-comment ids
///     (<c>T:</c>, <c>M:</c>, <c>P:</c>). Each file is loaded once per assembly; a missing or malformed file means
///     no documentation.
/// </summary>
internal static class XmlDocumentation
{
    private static readonly IReadOnlyDictionary<string, XElement> None = new Dictionary<string, XElement>();
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, XElement>> Files = new();
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex GenericArity = new(@"`\d+", RegexOptions.Compiled);

    public static string? Summary(MemberInfo member) => Text(Member(member)?.Element("summary"));

    public static string? Remarks(MemberInfo member) => Text(Member(member)?.Element("remarks"));

    public static string? Param(MemberInfo member, string name) =>
        Text(Member(member)?.Elements("param").FirstOrDefault(p => string.Equals((string?)p.Attribute("name"), name, StringComparison.Ordinal)));

    internal static IReadOnlyDictionary<string, XElement> Load(string path) {
        if (!File.Exists(path))
            return None;

        try {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Descendants("member")
                .Where(member => member.Attribute("name") is not null)
                .GroupBy(member => (string)member.Attribute("name")!)
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch (XmlException) {
            return None;
        }
    }

    internal static string IdOf(MemberInfo member) => member switch {
        Type type => "T:" + TypeId(type),
        MethodInfo method => "M:" + TypeId(method.DeclaringType!) + "." + method.Name
                             + (method.IsGenericMethod ? "``" + method.GetGenericArguments().Length : string.Empty)
                             + Parameters(Definition(method).GetParameters()),
        PropertyInfo property => "P:" + TypeId(property.DeclaringType!) + "." + property.Name + Parameters(property.GetIndexParameters()),
        _ => string.Empty
    };

    private static XElement? Member(MemberInfo member) =>
        Files.GetOrAdd(member.Module.Assembly, assembly => Load(Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml")))
            .GetValueOrDefault(IdOf(member));

    private static MethodInfo Definition(MethodInfo method) =>
        method.DeclaringType is { IsGenericType: true, IsGenericTypeDefinition: false } declaring
            ? (MethodInfo)MethodBase.GetMethodFromHandle(method.MethodHandle, declaring.GetGenericTypeDefinition().TypeHandle)!
            : method;

    private static string TypeId(Type type) => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName!.Replace('+', '.');

    private static string Parameters(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? string.Empty : "(" + string.Join(",", parameters.Select(p => ParameterType(p.ParameterType))) + ")";

    private static string ParameterType(Type type) {
        if (type.IsGenericParameter)
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        if (type.IsByRef)
            return ParameterType(type.GetElementType()!) + "@";
        if (type.IsArray)
            return ParameterType(type.GetElementType()!) + (type.GetArrayRank() == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank())) + "]");
        if (!type.IsGenericType)
            return type.FullName!.Replace('+', '.');

        // shortcut: arguments of a generic nested in another generic are all appended to the innermost name; exact for every other case.
        var name = GenericArity.Replace(type.GetGenericTypeDefinition().FullName!, string.Empty).Replace('+', '.');
        return name + "{" + string.Join(",", type.GetGenericArguments().Select(ParameterType)) + "}";
    }

    private static string? Text(XElement? element) {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        Append(builder, element);
        var text = Whitespace.Replace(builder.ToString(), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static void Append(StringBuilder builder, XElement element) {
        foreach (var node in element.Nodes()) {
            if (node is XText text) {
                builder.Append(text.Value);
                continue;
            }

            if (node is not XElement child)
                continue;

            var inline = child.Name.LocalName switch {
                "see" or "seealso" when child.IsEmpty => (string?)child.Attribute("langword") ?? CrefName((string?)child.Attribute("cref")),
                "paramref" or "typeparamref" => (string?)child.Attribute("name"),
                _ => null
            };

            if (inline is not null)
                builder.Append(inline);
            else
                Append(builder, child);
        }
    }

    private static string? CrefName(string? cref) {
        if (cref is null)
            return null;

        var signature = cref.Split('(')[0];
        return signature[(signature.LastIndexOfAny([':', '.']) + 1)..];
    }
}
