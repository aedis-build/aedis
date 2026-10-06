using Aedis.Exceptions;
using Aedis.Messaging;
using Aedis.Messaging.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aedis.Messaging.Tests;

/// <summary>
///     Taxonomia única de desfecho: cada família de exceção do framework cai na classe esperada, e as
///     decisões ACK/retry/dead-letter derivadas são coerentes entre si.
/// </summary>
public sealed class MessageOutcomeClassifierTests
{
    private sealed class ValidationFailure(string message) : PermanentFailureException(message);

    private sealed class ExpiredMessage(string message) : SkippableMessageException(message, "expired");

    [Fact]
    public void Sem_excecao_e_sucesso() {
        MessageOutcomeClassifier.FromException(null).Should().Be(MessageOutcome.Success);
    }

    [Fact]
    public void Duplicada_vem_antes_de_skippable() {
        MessageOutcomeClassifier.FromException(new DuplicateMessageException(Guid.NewGuid(), "dup"))
            .Should().Be(MessageOutcome.Duplicate);
        MessageOutcomeClassifier.FromException(new ExpiredMessage("expirada")).Should().Be(MessageOutcome.Skipped);
    }

    [Fact]
    public void Permanente_e_recuperavel() {
        MessageOutcomeClassifier.FromException(new ValidationFailure("inválida")).Should().Be(MessageOutcome.PermanentFailure);
        MessageOutcomeClassifier.FromException(new ServiceTemporarilyUnavailableException("db", "fora"))
            .Should().Be(MessageOutcome.Retryable);
    }

    [Theory]
    [InlineData(null, MessageOutcome.ExternalRetryable)]
    [InlineData(503, MessageOutcome.ExternalRetryable)]
    [InlineData(429, MessageOutcome.ExternalRetryable)]
    [InlineData(400, MessageOutcome.ExternalPermanent)]
    [InlineData(404, MessageOutcome.ExternalPermanent)]
    public void Servico_externo_depende_do_status(int? status, MessageOutcome esperado) {
        MessageOutcomeClassifier.FromException(new ExternalServiceException("api", "falhou", statusCode: status))
            .Should().Be(esperado);
    }

    [Fact]
    public void Cancelamento_e_desconhecido() {
        MessageOutcomeClassifier.FromException(new OperationCanceledException()).Should().Be(MessageOutcome.Cancelled);
        MessageOutcomeClassifier.FromException(new InvalidOperationException()).Should().Be(MessageOutcome.UnhandledFailure);
    }

    [Theory]
    [InlineData(MessageOutcome.Success)]
    [InlineData(MessageOutcome.Duplicate)]
    [InlineData(MessageOutcome.Skipped)]
    [InlineData(MessageOutcome.PermanentFailure)]
    [InlineData(MessageOutcome.Retryable)]
    [InlineData(MessageOutcome.ExternalRetryable)]
    [InlineData(MessageOutcome.ExternalPermanent)]
    [InlineData(MessageOutcome.Cancelled)]
    [InlineData(MessageOutcome.UnhandledFailure)]
    public void Cada_desfecho_tem_exatamente_uma_decisao(MessageOutcome outcome) {
        var decisions = new[] {
            MessageOutcomeClassifier.ShouldAcknowledge(outcome),
            MessageOutcomeClassifier.ShouldRetry(outcome),
            MessageOutcomeClassifier.ShouldDeadLetter(outcome)
        };

        decisions.Count(d => d).Should().Be(1);
        MessageOutcomeClassifier.ToTagValue(outcome).Should().MatchRegex("^[a-z_]+$");
    }
}
