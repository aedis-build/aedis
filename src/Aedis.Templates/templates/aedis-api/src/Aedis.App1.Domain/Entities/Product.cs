using Aedis.Domain.Entities;

namespace Aedis.App1.Domain.Entities;

/// <summary>
///     Agregado de exemplo. Herda de <see cref="AuditableAggregateRoot{TId}" />, então ganha identidade
///     (<c>Id</c>), colunas de auditoria (<c>CreatedAt/CreatedBy/UpdatedAt/UpdatedBy</c>) e soft-delete
///     (<c>IsDeleted/DeletedAt/DeletedBy</c>) carimbados pelo repositório. Troque <c>Product</c> pelo seu
///     próprio agregado e mantenha a regra de transição de estado dentro da entidade.
/// </summary>
public class Product : AuditableAggregateRoot<Guid> {
    /// <summary>
    ///     Código de negócio do produto. Funciona como chave natural e é imutável após a criação.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    ///     Nome do produto.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Preço do produto.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    ///     Cria um novo produto com identidade gerada. Use esta fábrica em vez do construtor para deixar
    ///     explícita a criação no domínio.
    /// </summary>
    /// <param name="code">Código de negócio (chave natural).</param>
    /// <param name="name">Nome do produto.</param>
    /// <param name="price">Preço do produto.</param>
    public static Product Create(string code, string name, decimal price) {
        return new Product {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Price = price
        };
    }

    /// <summary>
    ///     Aplica uma alteração de dados editáveis do produto, preservando a chave natural <see cref="Code" />.
    /// </summary>
    /// <param name="name">Novo nome.</param>
    /// <param name="price">Novo preço.</param>
    public void Update(string name, decimal price) {
        Name = name;
        Price = price;
    }
}
