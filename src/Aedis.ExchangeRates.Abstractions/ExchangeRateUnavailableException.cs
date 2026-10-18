namespace Aedis.ExchangeRates.Abstractions;

/// <summary>A fonte não publicou cotação para o par na data pedida nem nos dias anteriores que ela consulta.</summary>
public sealed class ExchangeRateUnavailableException(string message) : Exception(message);
