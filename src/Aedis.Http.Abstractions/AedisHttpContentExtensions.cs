using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Converte um <see cref="AedisHttpContent" /> agnóstico no <c>HttpContent</c> da BCL — a moeda comum que
///     tanto o provider nativo quanto o Flurl usam para enviar o corpo. Mantém a tradução em um único lugar,
///     sem duplicação entre providers.
/// </summary>
public static class AedisHttpContentExtensions
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Materializa o conteúdo agnóstico em um <c>HttpContent</c> da BCL pronto para envio.</summary>
    public static HttpContent ToHttpContent(this AedisHttpContent content) => content switch {
        AedisJsonContent json => new StringContent(JsonSerializer.Serialize(json.Value, WebJson), Encoding.UTF8, "application/json"),
        AedisFormContent form => new FormUrlEncodedContent(form.Fields),
        AedisStringContent text => new StringContent(text.Value, Encoding.UTF8, text.ContentType),
        AedisBytesContent bytes => CreateBytesContent(bytes),
        _ => throw new NotSupportedException($"Tipo de conteúdo não suportado: {content.GetType().Name}.")
    };

    private static ByteArrayContent CreateBytesContent(AedisBytesContent bytes) {
        var content = new ByteArrayContent(bytes.Data);
        content.Headers.ContentType = new MediaTypeHeaderValue(bytes.ContentType);
        return content;
    }
}
