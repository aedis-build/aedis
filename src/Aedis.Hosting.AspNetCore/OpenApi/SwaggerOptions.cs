namespace Aedis.Hosting.AspNetCore.OpenApi;

/// <summary>
///     Configura a documentação OpenAPI/Swagger, vinculada à seção <c>Swagger</c>. No Aedis o Swagger é
///     <strong>opt-in</strong> (ligado por <c>EnableSwagger</c> no host) — esta classe apenas parametriza a
///     geração e a UI quando habilitado. Mantenha-o desligado em produção ou proteja-o no ingress.
/// </summary>
public sealed class SwaggerOptions
{
    /// <summary>Nome da seção de configuração que vincula estas opções.</summary>
    public const string SectionName = "Swagger";

    /// <summary>Nome do documento OpenAPI (também usado na rota do JSON). Default <c>v1</c>.</summary>
    public string DocumentName { get; set; } = "v1";

    /// <summary>Título exibido na UI. Quando vazio, deriva do nome da aplicação.</summary>
    public string? Title { get; set; }

    /// <summary>Versão exibida no documento. Default <c>v1</c>.</summary>
    public string Version { get; set; } = "v1";

    /// <summary>Descrição opcional exibida na UI.</summary>
    public string? Description { get; set; }

    /// <summary>Prefixo de rota da UI e do JSON (ex.: <c>swagger</c> → <c>/swagger</c>). Default <c>swagger</c>.</summary>
    public string RoutePrefix { get; set; } = "swagger";

    /// <summary>
    ///     Quando <c>true</c> (default), declara o esquema de segurança <c>Bearer</c> (JWT) no documento,
    ///     permitindo autorizar requisições pela UI. Mantenha alinhado ao estado de autenticação do host.
    /// </summary>
    public bool IncludeBearerSecurity { get; set; } = true;
}
