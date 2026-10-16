using Aedis.Domain.ChainOfResponsibility;
using Aedis.Domain.ChainOfResponsibility.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aedis.Domain.Tests;

/// <summary>
///     Cadeia de responsabilidade: o builder liga os handlers na ordem; cada handler processa quando pode e passa
///     adiante; o executor devolve nulo (ou um default) quando ninguém responde, propaga erros e cancelamento; o
///     composto agrega as respostas; o registro por DI monta o executor com a cadeia.
/// </summary>
public sealed class ChainOfResponsibilityTests
{
    private sealed class Pedido(decimal valor) { public decimal Valor { get; } = valor; }

    private sealed class Desconto(decimal limite, string nome) : HandlerBase<Pedido, string>
    {
        protected override Task<bool> CanHandleAsync(Pedido request, CancellationToken cancellationToken) => Task.FromResult(request.Valor >= limite);
        protected override Task<string> ProcessAsync(Pedido request, CancellationToken cancellationToken) => Task.FromResult(nome);
    }

    private sealed class Explode : HandlerBase<Pedido, string>
    {
        protected override Task<bool> CanHandleAsync(Pedido request, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
        protected override Task<string> ProcessAsync(Pedido request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Normalizador : HandlerBase<string, string>
    {
        protected override Task<bool> CanHandleAsync(string request, CancellationToken cancellationToken) => Task.FromResult(true);
        protected override Task<string> ProcessAsync(string request, CancellationToken cancellationToken) => Task.FromResult(request.Trim().ToUpperInvariant());
    }

    private sealed class Agregador : CompositeHandler<Pedido, string>
    {
        public Agregador(params IHandler<Pedido, string>[] handlers) {
            foreach (var h in handlers) AddHandler(h);
        }

        protected override string CombineResponses(IReadOnlyList<string> responses) => string.Join("+", responses);
    }

    private static IHandler<Pedido, string> Cadeia() => ChainBuilder<Pedido, string>.Create()
        .AddHandler(new Desconto(1000, "ouro")).AddHandler(() => new Desconto(100, "prata")).Build()!;

    [Fact]
    public async Task Primeiro_handler_capaz_responde_e_o_resto_e_ignorado() {
        var executor = new ChainExecutor<Pedido, string>(Cadeia(), NullLogger<ChainExecutor<Pedido, string>>.Instance);

        (await executor.ExecuteAsync(new Pedido(5000))).Should().Be("ouro");
        (await executor.ExecuteAsync(new Pedido(200))).Should().Be("prata");
    }

    [Fact]
    public async Task Sem_handler_capaz_devolve_nulo_ou_o_default() {
        var executor = new ChainExecutor<Pedido, string>(Cadeia());

        (await executor.ExecuteAsync(new Pedido(1))).Should().BeNull();
        (await executor.ExecuteWithDefaultAsync(new Pedido(1), "nenhum")).Should().Be("nenhum");
        (await executor.ExecuteWithDefaultAsync(new Pedido(1), () => "fabrica")).Should().Be("fabrica");
        (await executor.ExecuteWithDefaultAsync(new Pedido(5000), "nenhum")).Should().Be("ouro");
    }

    [Fact]
    public async Task Handler_que_transforma_a_requisicao_passa_a_versao_nova_adiante() {
        var chain = ChainBuilder<string, string>.Create().AddHandler(new Normalizador()).Build()!;

        (await chain.HandleAsync("  abc ")).Should().Be("ABC", "o último handler devolve a requisição transformada quando ela é a resposta");
    }

    [Fact]
    public async Task Erros_e_cancelamento_propagam() {
        var executor = new ChainExecutor<Pedido, string>(new Explode(), NullLogger<ChainExecutor<Pedido, string>>.Instance);
        var act = () => executor.ExecuteAsync(new Pedido(1));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelado = () => new ChainExecutor<Pedido, string>(Cadeia(), NullLogger<ChainExecutor<Pedido, string>>.Instance).ExecuteAsync(new Pedido(1), cts.Token);
        await cancelado.Should().ThrowAsync<OperationCanceledException>();

        var nulos = () => executor.ExecuteAsync(null!);
        await nulos.Should().ThrowAsync<ArgumentNullException>();
        var semFabrica = () => executor.ExecuteWithDefaultAsync(new Pedido(1), (Func<string>)null!);
        await semFabrica.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Builder_vazio_devolve_nulo_e_rejeita_handler_nulo() {
        ChainBuilder<Pedido, string>.Create().Build().Should().BeNull();
        var act1 = () => ChainBuilder<Pedido, string>.Create().AddHandler((IHandler<Pedido, string>)null!);
        var act2 = () => ChainBuilder<Pedido, string>.Create().AddHandler((Func<IHandler<Pedido, string>>)null!);
        var act3 = () => new ChainExecutor<Pedido, string>(null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Composto_agrega_as_respostas_dos_filhos() {
        var composto = new Agregador(new Desconto(100, "a"), new Desconto(1000, "b"), new Desconto(99999, "c"));

        (await composto.HandleAsync(new Pedido(5000))).Should().Be("a+b");
        (await composto.HandleAsync(new Pedido(1))).Should().BeNull("nenhum filho respondeu");
    }

    [Fact]
    public async Task Registro_por_DI_monta_o_executor_com_a_cadeia_e_rejeita_cadeia_nula() {
        var provider = new ServiceCollection().AddLogging()
            .AddAedisChainExecutor<Pedido, string>(_ => Cadeia())
            .BuildServiceProvider();

        (await provider.GetRequiredService<IChainExecutor<Pedido, string>>().ExecuteAsync(new Pedido(5000))).Should().Be("ouro");

        var quebrado = new ServiceCollection().AddLogging().AddAedisChainExecutor<Pedido, string>(_ => null!, ServiceLifetime.Singleton).BuildServiceProvider();
        var act = () => quebrado.GetRequiredService<IChainExecutor<Pedido, string>>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Chain builder returned null*");

        var nulo = () => new ServiceCollection().AddAedisChainExecutor<Pedido, string>(null!);
        nulo.Should().Throw<ArgumentNullException>();
    }
}
