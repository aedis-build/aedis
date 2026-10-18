namespace Aedis.Payments.Abstractions;

/// <summary>
///     Registro de eventos de webhook já processados, para que reenvios do provedor não sejam tratados duas vezes. A
///     aplicação registra uma implementação durável e compartilhada entre réplicas (ex.: tabela com chave única); o
///     processador recusa notificações sem uma. <see cref="InMemoryPaymentEventStore" /> serve a testes e a uma
///     única instância.
/// </summary>
public interface IPaymentEventStore
{
    /// <summary>
    ///     Reserva o evento para processamento. Devolve <c>false</c> se ele já foi concluído ou está reservado por
    ///     outra execução dentro do prazo da reserva.
    /// </summary>
    Task<bool> TryClaimAsync(string eventKey, CancellationToken cancellationToken);

    /// <summary>Marca o evento como concluído; reenvios passam a ser duplicatas.</summary>
    Task CompleteAsync(string eventKey, CancellationToken cancellationToken);

    /// <summary>Desfaz a reserva após falha, para o reenvio do provedor processar de novo.</summary>
    Task ReleaseAsync(string eventKey, CancellationToken cancellationToken);
}
