using Aedis.Exceptions;
using FluentAssertions;
using Xunit;

namespace Aedis.Exceptions.Tests;

/// <summary>
///     Taxonomia de exceções: BusinessException mapeia tipo de violação em categoria e status HTTP (409 para
///     conflito, 412 para o resto) e aceita o nome snake_case legado; as famílias Retryable/Skippable/
///     PermanentFailure/ExternalService carregam os dados de diagnóstico e dizem se a mensagem volta para a fila
///     ou vai para a dead-letter.
/// </summary>
public sealed class ExceptionsTests
{
    [Theory]
    [InlineData(ViolationType.ValidationError, "validation", 412)]
    [InlineData(ViolationType.ConflictError, "business", 409)]
    [InlineData(ViolationType.PreconditionFailed, "business", 412)]
    [InlineData(ViolationType.UniqueConstraintViolation, "business", 412)]
    [InlineData(ViolationType.ForeignKeyViolation, "business", 412)]
    [InlineData(ViolationType.BusinessError, "business", 412)]
    public void BusinessException_mapeia_categoria_e_status(ViolationType type, string category, int status) {
        var ex = new BusinessException("msg", type, rule: "r1");

        ex.Category.Should().Be(category);
        ex.DefaultStatusCode.Should().Be(status);
        ex.EffectiveStatusCode.Should().Be(status);
        ex.Rule.Should().Be("r1");
        new BusinessException("msg", type, "r", new InvalidOperationException("i"), statusCode: 422).EffectiveStatusCode.Should().Be(422);
    }

    [Fact]
    public void BusinessException_aceita_o_nome_snake_case_legado() {
        new BusinessException("m", "conflict_error").ViolationType.Should().Be(ViolationType.ConflictError);
        new BusinessException("m", "unique_constraint_violation", "r", new Exception("x"), 409).StatusCode.Should().Be(409);
        new BusinessException("m", "desconhecido").ViolationType.Should().Be(ViolationType.ValidationError);
    }

    [Theory]
    [InlineData(ViolationType.ValidationError, "validation_error")]
    [InlineData(ViolationType.ForeignKeyViolation, "foreign_key_violation")]
    [InlineData(ViolationType.UniqueConstraintViolation, "unique_constraint_violation")]
    [InlineData(ViolationType.ConflictError, "conflict_error")]
    [InlineData(ViolationType.PreconditionFailed, "precondition_failed")]
    [InlineData(ViolationType.BusinessError, "business_error")]
    public void ViolationType_vai_e_volta_de_snake_case(ViolationType type, string snake) {
        type.ToSnakeCase().Should().Be(snake);
        ViolationTypeExtensions.FromSnakeCase(snake.ToUpperInvariant()).Should().Be(type);
    }

    [Fact]
    public void ViolationType_fora_do_enum_e_vazio_tem_fallback() {
        ((ViolationType)99).ToSnakeCase().Should().Be("99");
        ViolationTypeExtensions.FromSnakeCase(" ").Should().Be(ViolationType.ValidationError);
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(504, true)]
    [InlineData(599, true)]
    [InlineData(404, false)]
    public void ExternalServiceException_status_transitorios_voltam_para_a_fila(int status, bool requeue) {
        var ex = new ExternalServiceException("svc", "m", statusCode: status);

        ex.ShouldRequeue.Should().Be(requeue);
        ex.StatusCode.Should().Be(status);
    }

    [Fact]
    public void ExternalServiceException_decide_requeue_pelo_status() {
        new ExternalServiceException("svc", "m").ShouldRequeue.Should().BeTrue("sem status é transitório");
        new ExternalServiceException("svc", "m", statusCode: 429).ShouldRequeue.Should().BeTrue();
        new ExternalServiceException("svc", "m", statusCode: 503).SendToDeadLetterQueue.Should().BeFalse();
        var permanente = new ExternalServiceException("svc", "m", new Exception("i"), errorCode: "E1", statusCode: 400, responseBody: "corpo");
        permanente.ShouldRequeue.Should().BeFalse();
        permanente.SendToDeadLetterQueue.Should().BeTrue();
        permanente.ServiceName.Should().Be("svc");
        permanente.ErrorCode.Should().Be("E1");
        permanente.ResponseBody.Should().Be("corpo");
        permanente.AddErrorData("k", 1);
        permanente.ErrorData.Should().ContainKey("k");
    }

    [Fact]
    public void Retryable_carrega_RetryAfter_e_subclasses_seus_dados() {
        var agenda = new BusinessScheduleException("fora do horário", "09-18", DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5));
        agenda.ScheduleWindow.Should().Be("09-18");
        agenda.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
        agenda.ShouldRequeue.Should().BeTrue();
        agenda.CurrentTime.Should().Be(DateTimeOffset.UnixEpoch);
        new BusinessScheduleException("m", "w", DateTimeOffset.UnixEpoch).RetryAfter.Should().BeNull();

        var processando = new MessageBeingProcessedException(Guid.NewGuid(), "lock:1", "pod-a");
        processando.LockKey.Should().Be("lock:1");
        processando.ProcessingInstance.Should().Be("pod-a");
        processando.RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        processando.ShouldRequeue.Should().BeTrue();

        var lider = new LeadershipRequiredException("leader:1");
        lider.LeaderLockKey.Should().Be("leader:1");
        lider.Message.Should().Contain("unknown");

        var timeout = new MessageProcessingTimeoutException("op", 30, "m");
        timeout.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        timeout.Operation.Should().Be("op");
        new MessageProcessingTimeoutException("op", 30, "m", new Exception("i")).TimeoutSeconds.Should().Be(30);

        var fora = new ServiceTemporarilyUnavailableException("kms", "m", new Exception("i"));
        fora.ServiceName.Should().Be("kms");
        new ServiceTemporarilyUnavailableException("kms", "m").InnerException.Should().BeNull();
    }

    [Fact]
    public void Skippable_e_Permanent_dizem_o_destino_da_mensagem() {
        var dup = new DuplicateMessageException(Guid.NewGuid(), "repetida", "idem-1");
        dup.Reason.Should().Be("duplicate");
        dup.IdempotencyKey.Should().Be("idem-1");
        dup.MessageId.Should().NotBeEmpty();

        var perm = new PermanentFailureFake("m", new Exception("i"));
        perm.ShouldRequeue.Should().BeFalse();
        perm.SendToDeadLetterQueue.Should().BeTrue();
        perm.AddErrorData("a", "b");
        perm.ErrorData["a"].Should().Be("b");
        new PermanentFailureFake("m").InnerException.Should().BeNull();
        new SkippableFake("m", "r", new Exception("i")).Reason.Should().Be("r");

        var proibido = new ForbiddenException("sem acesso", new Exception("i"), "pedido", "pedido:ler");
        proibido.Resource.Should().Be("pedido");
        proibido.RequiredPermission.Should().Be("pedido:ler");
        new ForbiddenException("m").Resource.Should().BeNull();
    }

    private sealed class PermanentFailureFake : PermanentFailureException
    {
        public PermanentFailureFake(string m) : base(m) { }
        public PermanentFailureFake(string m, Exception i) : base(m, i) { }
    }

    private sealed class SkippableFake(string m, string r, Exception i) : SkippableMessageException(m, r, i);
}
