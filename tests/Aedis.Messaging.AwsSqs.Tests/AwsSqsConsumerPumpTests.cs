using Aedis.Exceptions;
using Aedis.Messaging.Abstractions;
using Aedis.Messaging.Abstractions.Serialization;
using Aedis.Messaging.AwsSqs;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aedis.Messaging.AwsSqs.Tests;

/// <summary>
///     Pump de consumo do SQS com o cliente substituído (sem rede): nunca há mais mensagens em voo do que
///     <c>MaxNumberOfMessages</c> e cada ReceiveMessage pede só os slots livres; o loop sobrevive a erros do
///     cliente; e o ACK segue a taxonomia de desfecho — descartes confirmam, falhas não.
/// </summary>
public sealed class AwsSqsConsumerPumpTests
{
    public sealed class PingMessage : MessageBase
    {
        public override string EventName => "ping";
    }

    private sealed class GatedHandler : IMessageHandler<PingMessage>
    {
        private int _concurrent;
        private int _handled;

        public SemaphoreSlim Gate { get; } = new(0);
        public int MaxConcurrent { get; private set; }
        public int Handled => Volatile.Read(ref _handled);
        public Func<PingMessage, Exception?>? Fail { get; init; }

        public async Task HandleAsync(PingMessage message, CancellationToken cancellationToken) {
            var now = Interlocked.Increment(ref _concurrent);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);

            try {
                if (Fail?.Invoke(message) is { } failure) throw failure;
                await Gate.WaitAsync(cancellationToken);
                Interlocked.Increment(ref _handled);
            }
            finally {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }

    private static (AwsSqsConsumerManager Manager, IAmazonSQS Sqs, List<ReceiveMessageRequest> Requests) Build(
        int maxInFlight, Func<ReceiveMessageRequest, List<Message>> supply, int receiveErrorBackoffMs = 20) {
        var sqs = Substitute.For<IAmazonSQS>();
        var requests = new List<ReceiveMessageRequest>();
        sqs.GetQueueUrlAsync("fila", Arg.Any<CancellationToken>())
            .Returns(new GetQueueUrlResponse { QueueUrl = "http://sqs/fila" });
        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                var request = call.Arg<ReceiveMessageRequest>();
                lock (requests) requests.Add(request);
                return Task.FromResult(new ReceiveMessageResponse { Messages = supply(request) });
            });
        sqs.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteMessageResponse());

        var factory = Substitute.For<IAwsPubSubFactory>();
        factory.ResolveQueueName(Arg.Any<string>()).Returns(call => call.Arg<string>());
        factory.GetSqsClientAsync(Arg.Any<CancellationToken>()).Returns(sqs);

        var options = Options.Create(new AwsSqsOptions {
            MaxNumberOfMessages = maxInFlight,
            WaitTimeSeconds = 0,
            ReceiveErrorBackoffMs = receiveErrorBackoffMs
        });
        var manager = new AwsSqsConsumerManager(factory, options, NullLogger<AwsSqsConsumerManager>.Instance,
            MessageSerializerResolver.CreateDefault(), MessageEncoderResolver.CreateDefault());

        return (manager, sqs, requests);
    }

    private static Message FakeMessage(string id) => new() {
        MessageId = id,
        ReceiptHandle = "rh-" + id,
        Body = "{}",
        Attributes = new Dictionary<string, string> { ["ApproximateReceiveCount"] = "1" }
    };

    [Fact]
    public async Task Pump_nao_excede_MaxNumberOfMessages_em_voo_e_pede_so_os_slots_livres() {
        var produced = 0;
        var (manager, _, requests) = Build(3, request => {
            var batch = new List<Message>();
            for (var i = 0; i < request.MaxNumberOfMessages && produced < 7; i++)
                batch.Add(FakeMessage((++produced).ToString()));
            return batch;
        });
        var handler = new GatedHandler();
        using var cts = new CancellationTokenSource();

        await manager.StartConsumerAsync("fila", "ex", string.Empty, handler, ConsumerRetryOptions.None(), cts.Token);
        await WaitUntilAsync(() => handler.MaxConcurrent == 3);
        await Task.Delay(150);

        handler.MaxConcurrent.Should().Be(3);
        produced.Should().Be(3, "com os 3 slots ocupados o pump não busca mais mensagens");
        lock (requests) requests.Should().OnlyContain(r => r.MaxNumberOfMessages >= 1 && r.MaxNumberOfMessages <= 3);

        handler.Gate.Release(7);
        await WaitUntilAsync(() => handler.Handled == 7);
        handler.MaxConcurrent.Should().Be(3);

        await manager.StopConsumerAsync("fila");
    }

    [Fact]
    public async Task Loop_sobrevive_a_erro_do_cliente_e_volta_a_receber() {
        var calls = 0;
        var (manager, _, _) = Build(2, _ => {
            if (Interlocked.Increment(ref calls) == 1) throw new AmazonSQSException("transiente");
            return [];
        });
        var handler = new GatedHandler();

        await manager.StartConsumerAsync("fila", "ex", string.Empty, handler, ConsumerRetryOptions.None());
        await WaitUntilAsync(() => Volatile.Read(ref calls) >= 3);

        (await manager.IsConsumerHealthyAsync("fila")).Should().BeTrue();
        await manager.StopConsumerAsync("fila");
    }

    [Fact]
    public async Task Descarte_confirma_a_mensagem_e_falha_nao() {
        var produced = 0;
        var (manager, sqs, _) = Build(2, request => {
            var batch = new List<Message>();
            for (var i = 0; i < request.MaxNumberOfMessages && produced < 2; i++)
                batch.Add(FakeMessage((++produced).ToString()));
            return batch;
        });
        var seen = 0;
        var handler = new GatedHandler {
            Fail = _ => Interlocked.Increment(ref seen) == 1
                ? new DuplicateMessageException(Guid.NewGuid(), "duplicada")
                : new InvalidOperationException("falhou de verdade")
        };

        await manager.StartConsumerAsync("fila", "ex", string.Empty, handler, ConsumerRetryOptions.None());
        await WaitUntilAsync(() => Volatile.Read(ref seen) == 2);
        await Task.Delay(100);
        await manager.StopConsumerAsync("fila");

        await sqs.Received(1).DeleteMessageAsync("http://sqs/fila", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_cancela_e_drena_as_mensagens_em_voo() {
        var produced = 0;
        var (manager, _, _) = Build(2, request => {
            var batch = new List<Message>();
            for (var i = 0; i < request.MaxNumberOfMessages && produced < 2; i++)
                batch.Add(FakeMessage((++produced).ToString()));
            return batch;
        });
        var handler = new GatedHandler();

        await manager.StartConsumerAsync("fila", "ex", string.Empty, handler, ConsumerRetryOptions.None());
        await WaitUntilAsync(() => handler.MaxConcurrent == 2);

        var stop = manager.StopConsumerAsync("fila");
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().BeSameAs(stop, "o cancelamento interrompe os handlers e o loop drena");
        (await manager.IsConsumerHealthyAsync("fila")).Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        condition().Should().BeTrue("a condição deveria ter sido atingida dentro do prazo");
    }
}
