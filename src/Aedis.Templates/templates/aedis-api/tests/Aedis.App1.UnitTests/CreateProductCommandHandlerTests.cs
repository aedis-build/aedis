using Aedis.App1.Application.Abstractions;
using Aedis.App1.Application.Products.Commands;
using Aedis.App1.Application.Products.Commands.Handlers;
using Aedis.App1.Domain.Entities;
using Aedis.Exceptions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Aedis.App1.UnitTests;

/// <summary>
///     Testa o handler de criação: persiste quando o código é novo e rejeita com conflito (409) quando o
///     código já existe.
/// </summary>
public sealed class CreateProductCommandHandlerTests {
    private readonly IProductRepository _repository = Substitute.For<IProductRepository>();

    [Fact]
    public async Task Cria_o_produto_quando_o_codigo_e_novo() {
        _repository.GetByCodeAsync("ABC", Arg.Any<CancellationToken>()).Returns((Product?)null);
        _repository.SaveAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Product>()));
        var handler = new CreateProductCommandHandler(_repository);

        var product = await handler.HandleAsync(new CreateProductCommand("ABC", "Widget", 9.9m));

        product.Code.Should().Be("ABC");
        product.Name.Should().Be("Widget");
        await _repository.Received(1).SaveAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejeita_com_conflito_quando_o_codigo_ja_existe() {
        _repository.GetByCodeAsync("ABC", Arg.Any<CancellationToken>())
            .Returns(Product.Create("ABC", "Existente", 1m));
        var handler = new CreateProductCommandHandler(_repository);

        var act = () => handler.HandleAsync(new CreateProductCommand("ABC", "Widget", 9.9m));

        var exception = (await act.Should().ThrowAsync<BusinessException>()).Which;
        exception.ViolationType.Should().Be(ViolationType.ConflictError);
        exception.EffectiveStatusCode.Should().Be(409);
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }
}
