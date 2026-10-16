using Aedis.Domain;
using Aedis.Domain.Saga;
using Aedis.Domain.Saga.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Aedis.Domain.Tests;

/// <summary>
///     Saga orquestrada: passos executam em ordem e são registrados no store; um passo que falha (ou lança)
///     compensa os anteriores em ordem inversa, marca o store e lança <see cref="SagaExecutionException" />;
///     compensação que falha é registrada sem interromper as demais; completar duas vezes é inofensivo; dispor sem
///     completar aciona a compensação automática; o contexto carrega identidade e metadados.
/// </summary>
public sealed class SagaTests
{
    private sealed class Contexto() : SagaContext("pedido");

    private sealed class Passo(string nome, bool ok = true, bool lanca = false, bool compensacaoFalha = false) : ISagaStep<Contexto>
    {
        public List<string> Log { get; } = [];
        public string StepName => nome;

        public Task<SagaStepResult> ExecuteAsync(Contexto context, CancellationToken ct = default) {
            Log.Add("exec");
            if (lanca) throw new InvalidOperationException($"{nome} explodiu");
            return Task.FromResult(ok ? SagaStepResult.Successful(nome) : SagaStepResult.Failed($"{nome} falhou", new TimeoutException()));
        }

        public Task CompensateAsync(Contexto context, SagaStepResult executionResult, CancellationToken ct = default) {
            Log.Add("comp");
            if (compensacaoFalha) throw new InvalidOperationException("compensação falhou");
            return Task.CompletedTask;
        }
    }

    private static SagaOrchestrator<Contexto> Saga(ISagaStateStore? store = null, Guid? id = null) =>
        new(NullLogger<SagaOrchestrator<Contexto>>.Instance, id, store);

    [Fact]
    public async Task Passos_executam_em_ordem_e_o_store_registra_inicio_passos_e_conclusao() {
        var store = Substitute.For<ISagaStateStore>();
        var a = new Passo("a"); var b = new Passo("b");
        await using var saga = Saga(store, Guid.NewGuid()).AddStep(a).AddStep(b);

        var result = await saga.ExecuteAsync(new Contexto());
        await saga.CompleteAsync();
        await saga.CompleteAsync();

        result.Success.Should().BeTrue();
        result.StepsExecuted.Should().Be(2);
        a.Log.Should().Equal("exec");
        await store.Received(1).SaveSagaStartAsync(saga.SagaId, "pedido", Arg.Any<CancellationToken>());
        await store.Received(1).SaveStepCompletionAsync(saga.SagaId, "a", "a", Arg.Any<CancellationToken>());
        await store.Received(1).SaveStepCompletionAsync(saga.SagaId, "b", "b", Arg.Any<CancellationToken>());
        await store.Received(1).MarkAsCompletedAsync(saga.SagaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passo_que_falha_compensa_os_anteriores_em_ordem_inversa_e_lanca() {
        var store = Substitute.For<ISagaStateStore>();
        var a = new Passo("a"); var b = new Passo("b", compensacaoFalha: true); var c = new Passo("c", ok: false); var d = new Passo("d");
        await using var saga = Saga(store).AddStep(a).AddStep(b).AddStep(c).AddStep(d);

        var act = () => saga.ExecuteAsync(new Contexto());

        var ex = (await act.Should().ThrowAsync<SagaExecutionException>()).Which;
        ex.FailedStepName.Should().Be("c");
        ex.SagaId.Should().Be(saga.SagaId);
        ex.InnerException.Should().BeOfType<TimeoutException>();
        a.Log.Should().Equal("exec", "comp");
        b.Log.Should().Equal("exec", "comp");
        d.Log.Should().BeEmpty();
        await store.Received(1).MarkAsCompensatedAsync(saga.SagaId, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passo_que_lanca_compensa_e_propaga_a_excecao_original() {
        var a = new Passo("a"); var b = new Passo("b", lanca: true);
        var saga = Saga().AddStep(a).AddStep(b);

        var act = () => saga.ExecuteAsync(new Contexto());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("b explodiu");
        a.Log.Should().Equal("exec", "comp");
        await saga.DisposeAsync();
        await saga.DisposeAsync();
    }

    [Fact]
    public async Task Regras_de_uso_sem_passos_executar_duas_vezes_completar_antes_e_passo_nulo() {
        var saga = Saga();
        var semPassos = () => saga.ExecuteAsync(new Contexto());
        await semPassos.Should().ThrowAsync<InvalidOperationException>().WithMessage("*without steps*");
        var completarAntes = () => saga.CompleteAsync();
        await completarAntes.Should().ThrowAsync<InvalidOperationException>().WithMessage("*before execution*");
        var nulo = () => saga.AddStep(null!);
        nulo.Should().Throw<ArgumentNullException>();

        saga.AddStep(new Passo("a"));
        await saga.ExecuteAsync(new Contexto());
        var deNovo = () => saga.ExecuteAsync(new Contexto());
        await deNovo.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been executed*");
        var depois = () => saga.AddStep(new Passo("b"));
        depois.Should().Throw<InvalidOperationException>().WithMessage("*after saga execution*");
        var semLogger = () => new SagaOrchestrator<Contexto>(null!);
        semLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Dispor_sem_completar_marca_compensacao_no_store() {
        var store = Substitute.For<ISagaStateStore>();
        var saga = Saga(store).AddStep(new Passo("a"));
        await saga.ExecuteAsync(new Contexto());

        await saga.DisposeAsync();

        await store.Received(1).MarkAsCompensatedAsync(saga.SagaId, 0, CancellationToken.None);

        var storeQuebrado = Substitute.For<ISagaStateStore>();
        storeQuebrado.MarkAsCompensatedAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("store fora"));
        var saga2 = Saga(storeQuebrado).AddStep(new Passo("a"));
        await saga2.ExecuteAsync(new Contexto());
        var act = () => saga2.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync("falha no dispose é só logada");
    }

    [Fact]
    public void Contexto_resultados_e_estado_carregam_os_dados() {
        var ctx = new Contexto();
        ctx.SagaType.Should().Be("pedido");
        ctx.SagaId.Should().NotBeEmpty();
        ctx.Metadata.Should().ContainKeys("CreatedAt", "MachineName", "ProcessId");
        ctx.SharedData["x"] = 1;

        SagaExecutionResult.Successful(3).Should().BeEquivalentTo(new { Success = true, StepsExecuted = 3, StepsCompensated = 0 });
        var falha = SagaExecutionResult.Failed("erro", 2, 1, new TimeoutException());
        falha.Success.Should().BeFalse();
        falha.Exception.Should().BeOfType<TimeoutException>();
        SagaStepResult.Failed("x").ErrorMessage.Should().Be("x");
        var state = new SagaState { SagaId = ctx.SagaId, SagaType = "pedido", StartedAt = ctx.StartedAt, CompletedAt = null, ErrorMessage = null, Steps = [new SagaStepState { StepName = "a", ExecutedAt = ctx.StartedAt, Data = "d", ErrorMessage = null }] };
        state.Status.Should().Be("Running");
        state.Steps.Single().Status.Should().Be("Pending");
        state.Steps.Single().Data.Should().Be("d");
    }

    [Fact]
    public void SagaBuilder_registra_passos_como_scoped_e_ajusta_timeout() {
        var services = new ServiceCollection();
        var builder = new SagaBuilder(services, new SagaOptions()).AddStep<Passo, Contexto>().WithTimeout(TimeSpan.FromSeconds(30));

        builder.Options.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(30));
        builder.Options.EnableDetailedLogging.Should().BeTrue();
        services.Should().ContainSingle(d => d.ServiceType == typeof(Passo) && d.Lifetime == ServiceLifetime.Scoped);
        var act1 = () => new SagaBuilder(null!, new SagaOptions());
        var act2 = () => new SagaBuilder(services, null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
