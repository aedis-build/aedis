using System.Text.Json.Serialization;

namespace Aedis.Hypermedia.Abstractions;

/// <summary>
///     Contrato dos recursos que carregam links de hipermídia, serializados como <c>_links</c> no estilo HAL.
/// </summary>
public interface IHasLinks {
    /// <summary>
    ///     Links de hipermídia do recurso, indexados pela relação (<c>self</c>, <c>next</c>, etc.).
    /// </summary>
    IDictionary<string, Link> Links { get; }
}

/// <summary>
///     Base dos recursos de hipermídia. Mantém o dicionário de links serializado como <c>_links</c>, as ações
///     autodescritivas opcionais em <c>_templates</c>, e oferece <see cref="AddLink" />/<see cref="AddTemplate" />
///     para anexá-los. Use os tipos concretos <see cref="Resource{T}" /> (item único) ou
///     <see cref="ResourceCollection{T}" /> (coleção paginada).
/// </summary>
public abstract class Resource : IHasLinks, IHasTemplates {
    /// <summary>
    ///     Links de hipermídia do recurso, serializados sob <c>_links</c> e posicionados após o corpo de dados.
    /// </summary>
    [JsonPropertyName("_links")]
    [JsonPropertyOrder(100)]
    public IDictionary<string, Link> Links { get; } = new Dictionary<string, Link>();

    /// <summary>
    ///     Ações autodescritivas do recurso, serializadas sob <c>_templates</c> após os links. Fica <c>null</c>
    ///     (e é omitida) quando o recurso não anuncia nenhuma.
    /// </summary>
    [JsonPropertyName("_templates")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, HalFormsTemplate>? Templates { get; private set; }

    /// <summary>
    ///     Anexa um link ao recurso. Sobrescreve um link já existente para a mesma relação.
    /// </summary>
    /// <param name="rel">Relação semântica (não pode ser vazia).</param>
    /// <param name="href">Destino do link (não pode ser vazio).</param>
    /// <param name="method">Verbo HTTP do link. Padrão <c>GET</c>.</param>
    /// <param name="templated">Indica se <paramref name="href" /> é um modelo com placeholders.</param>
    /// <exception cref="ArgumentException">Quando <paramref name="rel" /> ou <paramref name="href" /> é vazio.</exception>
    public void AddLink(string rel, string href, string method = "GET", bool templated = false) {
        if (string.IsNullOrWhiteSpace(rel)) {
            throw new ArgumentException("A relação do link não pode ser vazia.", nameof(rel));
        }

        if (string.IsNullOrWhiteSpace(href)) {
            throw new ArgumentException("O destino do link não pode ser vazio.", nameof(href));
        }

        Links[rel] = new Link(href, rel, method, templated);
    }

    /// <summary>
    ///     Anexa uma ação autodescritiva a <c>_templates</c>; o dicionário nasce sob demanda. Sobrescreve um
    ///     template já existente com o mesmo nome.
    /// </summary>
    /// <param name="name">Nome do template (normalmente o <c>rel</c> do link correspondente).</param>
    /// <param name="template">Descrição da ação: método, destino e corpo pronto.</param>
    public void AddTemplate(string name, HalFormsTemplate template) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("O nome do template não pode ser vazio.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(template);
        Templates ??= new Dictionary<string, HalFormsTemplate>();
        Templates[name] = template;
    }
}

/// <summary>
///     Recurso de hipermídia de item único. O payload de negócio fica sob <c>data</c> e os links sob
///     <c>_links</c>, mantendo o envelope estável independentemente do formato do modelo.
/// </summary>
/// <typeparam name="T">Tipo do modelo de resposta exposto sob <c>data</c>.</typeparam>
public sealed class Resource<T> : Resource {
    /// <summary>
    ///     Cria um recurso encapsulando o modelo de resposta.
    /// </summary>
    /// <param name="data">Modelo de negócio a expor. Não pode ser nulo.</param>
    /// <exception cref="ArgumentNullException">Quando <paramref name="data" /> é nulo.</exception>
    public Resource(T data) {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>
    ///     Modelo de negócio do recurso, serializado sob <c>data</c>.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonPropertyOrder(0)]
    public T Data { get; }
}

/// <summary>
///     Recurso de hipermídia de coleção paginada. Cada item é um <see cref="Resource{T}" /> — ou seja, já
///     carrega seus próprios <c>_links</c> — sob <c>items</c>; os metadados de paginação
///     (<c>totalCount</c>/<c>page</c>/<c>pageSize</c>) ficam no mesmo nível e os links de navegação da coleção
///     (<c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c>) sob <c>_links</c>.
/// </summary>
/// <typeparam name="T">Tipo de cada item da coleção.</typeparam>
public sealed class ResourceCollection<T> : Resource {
    /// <summary>
    ///     Cria uma coleção paginada de recursos.
    /// </summary>
    /// <param name="items">Itens da página, já encapsulados com seus links. Não pode ser nulo.</param>
    /// <param name="totalCount">Total de itens em todas as páginas, quando conhecido.</param>
    /// <param name="page">Número da página atual (1-based), quando aplicável.</param>
    /// <param name="pageSize">Quantidade de itens por página, quando aplicável.</param>
    /// <exception cref="ArgumentNullException">Quando <paramref name="items" /> é nulo.</exception>
    public ResourceCollection(IEnumerable<Resource<T>> items, int? totalCount = null, int? page = null, int? pageSize = null) {
        ArgumentNullException.ThrowIfNull(items);
        Items = items.ToList();
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    ///     Itens da página atual, cada um com seu próprio <c>_links</c>, serializados sob <c>items</c>.
    /// </summary>
    [JsonPropertyName("items")]
    [JsonPropertyOrder(0)]
    public IReadOnlyList<Resource<T>> Items { get; }

    /// <summary>
    ///     Total de itens em todas as páginas, quando conhecido.
    /// </summary>
    [JsonPropertyName("totalCount")]
    [JsonPropertyOrder(1)]
    public int? TotalCount { get; }

    /// <summary>
    ///     Número da página atual (1-based), quando aplicável.
    /// </summary>
    [JsonPropertyName("page")]
    [JsonPropertyOrder(2)]
    public int? Page { get; }

    /// <summary>
    ///     Quantidade de itens por página, quando aplicável.
    /// </summary>
    [JsonPropertyName("pageSize")]
    [JsonPropertyOrder(3)]
    public int? PageSize { get; }
}
