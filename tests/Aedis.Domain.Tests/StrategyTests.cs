using Aedis.Domain.Strategy;
using Aedis.Domain.Strategy.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aedis.Domain.Tests;

/// <summary>
///     Estratégias: o resolver por contexto executa a primeira que pode tratar e falha listando as disponíveis; o
///     resolver por chave escolhe pela chave extraída do contexto e falha listando as chaves.
/// </summary>
public sealed class StrategyTests
{
    private sealed record Pagamento(string Meio, decimal Valor);

    private sealed class Pix : IKeyedStrategy<string, Pagamento>
    {
        public List<decimal> Processados { get; } = [];
        public string Key => "pix";
        public bool CanHandle(Pagamento context) => context.Meio == "pix";

        public Task ExecuteAsync(Pagamento context, CancellationToken cancellationToken = default) {
            Processados.Add(context.Valor);
            return Task.CompletedTask;
        }
    }

    private sealed class Cartao : IKeyedStrategy<string, Pagamento>
    {
        public string Key => "cartao";
        public bool CanHandle(Pagamento context) => context.Meio == "cartao";
        public Task ExecuteAsync(Pagamento context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Resolver_por_contexto_executa_a_primeira_estrategia_capaz() {
        var pix = new Pix();
        var resolver = new ContextStrategyResolver<Pagamento>([new Cartao(), pix], NullLogger<ContextStrategyResolver<Pagamento, IStrategy<Pagamento>>>.Instance);

        await resolver.ExecuteAsync(new Pagamento("pix", 10));

        pix.Processados.Should().Equal(10);
        resolver.GetStrategy(new Pagamento("cartao", 1)).Should().BeOfType<Cartao>();
        resolver.GetStrategy(new Pagamento("boleto", 1)).Should().BeNull();
    }

    [Fact]
    public async Task Resolver_por_contexto_sem_estrategia_lista_as_disponiveis() {
        var resolver = new ContextStrategyResolver<Pagamento, IStrategy<Pagamento>>([new Cartao()]);

        var act = () => resolver.ExecuteAsync(new Pagamento("boleto", 1));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Available strategies: Cartao*");
        var nulo = () => new ContextStrategyResolver<Pagamento, IStrategy<Pagamento>>(null!);
        nulo.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Resolver_por_chave_escolhe_pela_chave_do_contexto() {
        var pix = new Pix();
        var resolver = new KeyedStrategyResolver<string, Pagamento>([pix, new Cartao()], p => p.Meio, NullLogger<KeyedStrategyResolver<string, Pagamento, IKeyedStrategy<string, Pagamento>>>.Instance);

        await resolver.ExecuteAsync(new Pagamento("pix", 7));

        pix.Processados.Should().Equal(7);
        resolver.GetStrategy("cartao").Should().BeOfType<Cartao>();
    }

    [Fact]
    public async Task Resolver_por_chave_sem_estrategia_lista_as_chaves() {
        var resolver = new KeyedStrategyResolver<string, Pagamento, IKeyedStrategy<string, Pagamento>>([new Pix(), new Cartao()], p => p.Meio);

        var act = () => resolver.ExecuteAsync(new Pagamento("boleto", 1));
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Available keys: pix, cartao*");
        var get = () => resolver.GetStrategy("boleto");
        get.Should().Throw<NotSupportedException>();
        var nulo = () => new KeyedStrategyResolver<string, Pagamento>([new Pix()], null!);
        nulo.Should().Throw<ArgumentNullException>();
    }
}
