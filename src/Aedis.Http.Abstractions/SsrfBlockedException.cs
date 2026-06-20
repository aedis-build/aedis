using System.Net;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Lançada quando o SSRF guard recusa uma conexão por o destino resolver para um endereço interno
///     bloqueado (ou por host na blocklist / fora da allowlist). Propaga-se através do cliente HTTP — o
///     framework não a engole (fail-fast).
/// </summary>
public sealed class SsrfBlockedException : Exception
{
    /// <summary>Cria a exceção identificando o host e, quando conhecido, o endereço resolvido bloqueado.</summary>
    public SsrfBlockedException(string host, IPAddress? resolvedAddress)
        : base($"Conexão recusada pela proteção SSRF: '{host}'" + (resolvedAddress is null ? " não está autorizado." : $" resolve para o endereço interno {resolvedAddress}.")) {
        Host = host;
        ResolvedAddress = resolvedAddress;
    }

    /// <summary>Host de destino que foi recusado.</summary>
    public string Host { get; }

    /// <summary>Endereço interno para o qual o host resolveu, ou <c>null</c> se a recusa foi por host (block/allowlist).</summary>
    public IPAddress? ResolvedAddress { get; }
}
