namespace Aedis.Http.Abstractions;

/// <summary>
///     Corpo de uma requisição HTTP de forma agnóstica de provider. O cliente concreto (nativo, Flurl)
///     traduz cada subtipo para o conteúdo equivalente da sua stack. Use as fábricas estáticas para criar
///     os casos comuns (JSON, formulário, texto, bytes).
/// </summary>
public abstract class AedisHttpContent
{
    /// <summary>Cria um corpo JSON serializando <paramref name="value" /> (System.Text.Json).</summary>
    public static AedisHttpContent Json(object value) => new AedisJsonContent(value);

    /// <summary>Cria um corpo <c>application/x-www-form-urlencoded</c> a partir dos pares informados.</summary>
    public static AedisHttpContent Form(IReadOnlyCollection<KeyValuePair<string, string>> fields) => new AedisFormContent(fields);

    /// <summary>Cria um corpo textual com o <paramref name="contentType" /> indicado (default <c>text/plain</c>).</summary>
    public static AedisHttpContent Text(string text, string contentType = "text/plain") => new AedisStringContent(text, contentType);

    /// <summary>Cria um corpo binário com o <paramref name="contentType" /> indicado.</summary>
    public static AedisHttpContent Bytes(byte[] data, string contentType = "application/octet-stream") => new AedisBytesContent(data, contentType);
}

/// <summary>Corpo serializado como JSON a partir de um objeto.</summary>
public sealed class AedisJsonContent(object value) : AedisHttpContent
{
    /// <summary>Objeto a ser serializado em JSON no envio.</summary>
    public object Value { get; } = value;
}

/// <summary>Corpo <c>application/x-www-form-urlencoded</c>.</summary>
public sealed class AedisFormContent(IReadOnlyCollection<KeyValuePair<string, string>> fields) : AedisHttpContent
{
    /// <summary>Campos do formulário a codificar no corpo.</summary>
    public IReadOnlyCollection<KeyValuePair<string, string>> Fields { get; } = fields;
}

/// <summary>Corpo textual com content-type explícito.</summary>
public sealed class AedisStringContent(string text, string contentType) : AedisHttpContent
{
    /// <summary>Texto do corpo.</summary>
    public string Value { get; } = text;

    /// <summary>Content-type a declarar.</summary>
    public string ContentType { get; } = contentType;
}

/// <summary>Corpo binário (bytes brutos) com content-type explícito.</summary>
public sealed class AedisBytesContent(byte[] data, string contentType) : AedisHttpContent
{
    /// <summary>Bytes do corpo.</summary>
    public byte[] Data { get; } = data;

    /// <summary>Content-type a declarar.</summary>
    public string ContentType { get; } = contentType;
}
