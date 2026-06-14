using System.Text.Json.Serialization;

namespace Aedis.Events;

/// <summary>
///     Representa um evento no formato CloudEvents v1.0 (CNCF) com extensões Aedis.
///     Padrão corporativo para webhooks e integrações HTTP.
/// </summary>
/// <remarks>
///     <para>
///         CloudEvents é uma especificação para descrever dados de eventos de forma comum.
///         Esta implementação segue a especificação CNCF CloudEvents v1.0 com extensões específicas Aedis.
///     </para>
///     <para>
///         Campos obrigatórios conforme especificação CloudEvents v1.0:
///     </para>
///     <list type="bullet">
///         <item>
///             <description><c>specversion</c> - Versão da especificação (valor fixo: "1.0")</description>
///         </item>
///         <item>
///             <description><c>id</c> - Identificador único do evento (UUID)</description>
///         </item>
///         <item>
///             <description><c>source</c> - Origem do evento (ex: "/payhop/notifications")</description>
///         </item>
///         <item>
///             <description><c>type</c> - Tipo do evento (ex: "com.payhop.order.created")</description>
///         </item>
///         <item>
///             <description><c>time</c> - Timestamp UTC do evento</description>
///         </item>
///         <item>
///             <description><c>datacontenttype</c> - Tipo de conteúdo (valor fixo: "application/json")</description>
///         </item>
///     </list>
///     <para>
///         Campos opcionais:
///     </para>
///     <list type="bullet">
///         <item>
///             <description><c>subject</c> - Identifica o alvo do evento</description>
///         </item>
///         <item>
///             <description><c>data</c> - Payload do evento (objeto JSON)</description>
///         </item>
///     </list>
///     <para>
///         Extensões Aedis:
///     </para>
///     <list type="bullet">
///         <item>
///             <description><c>test</c> - Identifica se é evento de teste (true = teste, false/null = real)</description>
///         </item>
///     </list>
/// </remarks>
public class ResourceCloudEvent
{
    /// <summary>
    ///     Versão da especificação CloudEvents. Valor fixo: "1.0".
    /// </summary>
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; set; } = "1.0";

    /// <summary>
    ///     Identificador único do evento (UUID).
    ///     Usado para deduplicação, auditoria e rastreabilidade.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>
    ///     Origem do evento (serviço ou módulo).
    ///     Recomendação: "/payhop/&lt;serviço-ou-domínio&gt;"
    ///     Exemplos: "/payhop/notifications", "/payhop/orders", "/payhop/payments"
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = default!;

    /// <summary>
    ///     Tipo do evento (equivalente ao antigo eventType).
    ///     Segue convenção: "com.payhop.&lt;domínio&gt;.&lt;evento&gt;"
    ///     Exemplos: "com.payhop.order.created", "com.payhop.payment.approved"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    /// <summary>
    ///     Identifica o alvo do evento.
    ///     No contexto de notificações Aedis: "partner:{partnerId}:correlation:{correlationId}"
    /// </summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>
    ///     Timestamp UTC do evento (quando o evento ocorreu).
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    /// <summary>
    ///     Tipo de conteúdo dentro de data. Valor fixo: "application/json".
    /// </summary>
    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; set; } = "application/json";

    /// <summary>
    ///     Extensão Aedis: identifica se o evento é somente teste.
    ///     Valores:
    ///     - true: Webhook de teste
    ///     - false: Evento real
    ///     - null: Não especificado (tratar como real)
    ///     Esta propriedade não é serializada quando null (reduz tráfego).
    /// </summary>
    [JsonPropertyName("test")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Test { get; set; }

    /// <summary>
    ///     Payload do evento (objeto JSON).
    ///     Contém apenas o payload original enviado pela API produtora do evento.
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}