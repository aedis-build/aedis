using Aedis.Commands;
using Aedis.Commands.Abstractions;
using Aedis.Commands.Decorators;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Commands.Tests;

/// <summary>
///     Comandos: o executor resolve o handler pelo tipo do comando num escopo próprio e falha com mensagem clara
///     quando não há handler; os decoradores de log e de retry envolvem o handler sem mudar o resultado; o registro
///     por DI expõe o executor como singleton e os handlers como transientes.
/// </summary>
public sealed class CommandExecutorTests
{
    public sealed record Soma(int A, int B) : ICommand<int>;

    public sealed class SomaHandler : ICommandHandler<Soma, int>
    {
        public Task<int> HandleAsync(Soma command, CancellationToken cancellationToken = default) => Task.FromResult(command.A + command.B);
    }

    public sealed record SemHandler : ICommand<string>;

    private static IServiceProvider Provider() => new ServiceCollection().AddLogging()
        .AddAedisCommands().AddAedisCommandHandler<Soma, int, SomaHandler>().BuildServiceProvider();

    [Fact]
    public async Task Executor_resolve_o_handler_pelo_tipo_do_comando() {
        var executor = Provider().GetRequiredService<ICommandExecutor>();

        (await executor.ExecuteAsync(new Soma(2, 3))).Should().Be(5);
    }

    [Fact]
    public async Task Sem_handler_registrado_falha_dizendo_qual_registrar() {
        var executor = Provider().GetRequiredService<ICommandExecutor>();

        var act = () => executor.ExecuteAsync(new SemHandler());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ICommandHandler<SemHandler, String>*");
    }

    [Fact]
    public void Executor_e_singleton_e_handler_transiente() {
        var services = new ServiceCollection().AddLogging().AddAedisCommands().AddAedisCommands()
            .AddAedisCommandHandler<Soma, int, SomaHandler>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(ICommandExecutor)).Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(ICommandHandler<Soma, int>)).Which.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void Executor_exige_fabrica_de_escopo_e_logger() {
        var act1 = () => new CommandExecutor(null!, NullLogger<CommandExecutor>.Instance);
        var act2 = () => new CommandExecutor(Substitute.For<IServiceScopeFactory>(), null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Decorador_de_log_devolve_o_resultado_e_propaga_a_falha() {
        var ok = new LoggingCommandHandlerDecorator<Soma, int>(new SomaHandler(), NullLogger<LoggingCommandHandlerDecorator<Soma, int>>.Instance);
        var inner = Substitute.For<ICommandHandler<Soma, int>>();
        inner.HandleAsync(Arg.Any<Soma>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var falha = new LoggingCommandHandlerDecorator<Soma, int>(inner, NullLogger<LoggingCommandHandlerDecorator<Soma, int>>.Instance);

        (await ok.HandleAsync(new Soma(1, 1))).Should().Be(2);
        var act = () => falha.HandleAsync(new Soma(1, 1));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task Retry_tenta_de_novo_ate_o_limite_e_depois_propaga() {
        var inner = Substitute.For<ICommandHandler<Soma, int>>();
        var attempts = 0;
        inner.HandleAsync(Arg.Any<Soma>(), Arg.Any<CancellationToken>()).Returns(_ => ++attempts < 3 ? throw new InvalidOperationException("instável") : Task.FromResult(42));
        var retry = new RetryCommandHandlerDecorator<Soma, int>(inner, NullLogger<RetryCommandHandlerDecorator<Soma, int>>.Instance, maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));

        (await retry.HandleAsync(new Soma(0, 0))).Should().Be(42);
        attempts.Should().Be(3);

        var sempreFalha = Substitute.For<ICommandHandler<Soma, int>>();
        sempreFalha.HandleAsync(Arg.Any<Soma>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("sempre"));
        var retry2 = new RetryCommandHandlerDecorator<Soma, int>(sempreFalha, NullLogger<RetryCommandHandlerDecorator<Soma, int>>.Instance, maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(1));
        var act = () => retry2.HandleAsync(new Soma(0, 0));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("sempre");
        await sempreFalha.Received(2).HandleAsync(Arg.Any<Soma>(), Arg.Any<CancellationToken>());
    }
}
