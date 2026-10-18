namespace Aedis.Payments.Abstractions;

/// <summary>Linha de uma cobrança avulsa (ex.: produtividade de um colaborador na semana).</summary>
/// <param name="Description">Texto exibido na fatura.</param>
/// <param name="Amount">Valor em unidades mínimas da moeda da cobrança; deve ser positivo.</param>
public sealed record ChargeLine(string Description, long Amount);
