namespace Aedis.Diagnostics;

/// <summary>
///     Opções do desligamento gracioso host-agnóstico do Aedis (workers e jobs). A drenagem de
///     requisições HTTP em si pertence ao host ASP.NET; aqui controla-se o atraso de propagação e o
///     descarte dos recursos registrados no <see cref="DisposableRegistry" /> (locks de liderança, etc.).
/// </summary>
public sealed class GracefulShutdownOptions
{
    /// <summary>
    ///     Atraso após o sinal de parada antes de descartar os recursos, dando tempo para o
    ///     <c>/health/ready</c> propagar como Unhealthy e o orquestrador remover a instância do roteamento.
    ///     Padrão: 5 segundos. Use <see cref="TimeSpan.Zero" /> para descartar imediatamente.
    /// </summary>
    public TimeSpan DrainDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Tempo máximo que o host aguarda os serviços hospedados pararem (propagado para
    ///     <c>HostOptions.ShutdownTimeout</c>). Precisa cobrir <see cref="DrainDelay" /> mais o descarte dos
    ///     recursos; se for menor, o host aborta o desligamento no meio da drenagem — por isso a validação
    ///     exige <c>ShutdownTimeout &gt;= DrainDelay</c>. Padrão: 30 segundos.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
