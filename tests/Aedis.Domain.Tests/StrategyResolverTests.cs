using Aedis.Domain.Strategy;
using Aedis.Domain.Strategy.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Domain.Tests;

/// <summary>
///     <see cref="StrategyResolver{TContext}" /> é a base herdável: executa por contexto, devolve a estratégia por
///     contexto ou por chave, e falha listando as disponíveis.
/// </summary>
public sealed class StrategyResolverTests
{
    private sealed record Envio(string Canal);

    private sealed class Email : IKeyedStrategy<string, Envio>
    {
        public int Enviados { get; private set; }
        public string Key => "email";
        public bool CanHandle(Envio context) => context.Canal == "email";

        public Task ExecuteAsync(Envio context, CancellationToken cancellationToken = default) {
            Enviados++;
            return Task.CompletedTask;
        }
    }

    private sealed class Sms : IStrategy<Envio>
    {
        public bool CanHandle(Envio context) => context.Canal == "sms";
        public Task ExecuteAsync(Envio context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EnvioResolver(IEnumerable<IStrategy<Envio>> strategies) : StrategyResolver<Envio>(strategies)
    {
        public int Quantas => Strategies.Count();
    }

    [Fact]
    public async Task Executa_e_resolve_por_contexto_e_por_chave() {
        var email = new Email();
        var resolver = new EnvioResolver([new Sms(), email]);

        await resolver.ExecuteAsync(new Envio("email"));

        email.Enviados.Should().Be(1);
        resolver.Quantas.Should().Be(2);
        resolver.GetStrategy(new Envio("sms")).Should().BeOfType<Sms>();
        resolver.GetStrategy("email").Should().BeSameAs(email);
    }

    [Fact]
    public async Task Sem_estrategia_falha_listando_as_disponiveis() {
        var resolver = new StrategyResolver<Envio>([new Sms()]);

        var exec = () => resolver.ExecuteAsync(new Envio("fax"));
        await exec.Should().ThrowAsync<NotSupportedException>().WithMessage("*Available strategies: Sms*");
        var porContexto = () => resolver.GetStrategy(new Envio("fax"));
        porContexto.Should().Throw<NotSupportedException>().WithMessage("*Sms*");
        var porChave = () => resolver.GetStrategy("fax");
        porChave.Should().Throw<NotSupportedException>().WithMessage("*key 'fax'*");
        var nulo = () => new StrategyResolver<Envio>(null!);
        nulo.Should().Throw<ArgumentNullException>();
    }
}
