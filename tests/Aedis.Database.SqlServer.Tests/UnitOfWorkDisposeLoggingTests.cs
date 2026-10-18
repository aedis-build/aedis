using System.Data;
using System.Data.Common;
using Aedis.Database.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Aedis.Database.SqlServer.Tests;

/// <summary>
///     O rollback no descarte é o fluxo normal da sessão de leitura (nunca há commit) e não deve gerar warning;
///     na sessão de escrita sem commit, o warning continua sinalizando o esquecimento.
/// </summary>
public sealed class UnitOfWorkDisposeLoggingTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Descarte_com_transacao_aberta_so_avisa_na_escrita(bool isReadOnly, bool warns) {
        var logger = new CapturingLogger();
        var transaction = Substitute.For<DbTransaction>();
        var unitOfWork = new UnitOfWork(Substitute.For<DbConnection>(), isReadOnly, logger);
        unitOfWork.SetTransaction(transaction);

        await unitOfWork.DisposeAsync();

        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        logger.Levels.Contains(LogLevel.Warning).Should().Be(warns);
    }

    private sealed class CapturingLogger : ILogger<UnitOfWork>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
