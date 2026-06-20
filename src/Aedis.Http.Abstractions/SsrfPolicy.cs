using System.Net;
using System.Net.Sockets;

namespace Aedis.Http.Abstractions;

/// <summary>
///     Política anti-SSRF aplicada ao transporte: ao conectar, recusa endereços internos (loopback,
///     link-local/metadata de nuvem, redes privadas e ULA IPv6), impedindo que a aplicação seja usada para
///     alcançar serviços internos ou a API de metadados (169.254.169.254). A checagem é feita sobre o
///     <strong>IP resolvido no momento da conexão</strong>, o que neutraliza DNS rebinding. Opt-in
///     (<see cref="Enabled" />); a allowlist libera hosts internos legítimos, a blocklist proíbe hosts.
/// </summary>
public sealed class SsrfPolicy
{
    /// <summary>Liga a proteção SSRF no cliente HTTP. Default <c>false</c> (opt-in).</summary>
    public bool Enabled { get; set; }

    /// <summary>Quando <c>true</c> (default), bloqueia loopback, link-local/metadata, redes privadas e ULA.</summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    /// <summary>Hosts explicitamente liberados (ex.: um serviço interno legítimo) — isentos do bloqueio de rede privada.</summary>
    public IList<string> AllowedHosts { get; } = new List<string>();

    /// <summary>Hosts sempre proibidos, independentemente do IP.</summary>
    public IList<string> BlockedHosts { get; } = new List<string>();

    /// <summary>Indica se o <paramref name="host" /> está na blocklist.</summary>
    public bool IsHostBlocked(string host) =>
        BlockedHosts.Any(blocked => string.Equals(blocked, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>Indica se o <paramref name="host" /> está na allowlist (isento do bloqueio de IP interno).</summary>
    public bool IsHostAllowlisted(string host) =>
        AllowedHosts.Any(allowed => string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>Indica se o endereço <paramref name="address" /> cai numa faixa interna bloqueada.</summary>
    public bool IsAddressBlocked(IPAddress address) {
        if (!BlockPrivateNetworks)
            return false;

        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(normalized))
            return true;

        if (normalized.AddressFamily == AddressFamily.InterNetwork) {
            var octets = normalized.GetAddressBytes();
            return octets[0] == 0                                              // 0.0.0.0/8
                   || octets[0] == 10                                          // 10.0.0.0/8
                   || (octets[0] == 100 && octets[1] is >= 64 and <= 127)      // 100.64.0.0/10 (CGNAT)
                   || (octets[0] == 169 && octets[1] == 254)                   // 169.254.0.0/16 (link-local + metadata)
                   || (octets[0] == 172 && octets[1] is >= 16 and <= 31)       // 172.16.0.0/12
                   || (octets[0] == 192 && octets[1] == 168);                  // 192.168.0.0/16
        }

        if (normalized.AddressFamily == AddressFamily.InterNetworkV6) {
            if (normalized.IsIPv6LinkLocal || normalized.IsIPv6SiteLocal)
                return true;

            return (normalized.GetAddressBytes()[0] & 0xfe) == 0xfc;          // fc00::/7 (ULA)
        }

        return false;
    }
}
