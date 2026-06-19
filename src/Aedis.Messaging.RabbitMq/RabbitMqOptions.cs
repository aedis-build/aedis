using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.RabbitMq;

/// <summary>
///     Opções de conexão e pool do provider RabbitMQ. Vinculadas à seção <see cref="SectionName" /> da
///     configuração e validadas por data annotations na inicialização. Controlam o endpoint, as credenciais
///     e os limites do pool de canais (prefetch, número máximo de canais e timeout de aquisição).
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>Nome da seção de configuração de onde as opções são lidas.</summary>
    public const string SectionName = "RABBITMQ";

    /// <summary>Host do broker RabbitMQ.</summary>
    [Required] public string Host { get; set; } = null!;

    /// <summary>Porta AMQP do broker.</summary>
    [Required] public int Port { get; set; } = 5672;

    /// <summary>Usuário de autenticação.</summary>
    [Required] public string Username { get; set; } = null!;

    /// <summary>Senha de autenticação.</summary>
    [Required] public string Password { get; set; } = null!;

    /// <summary>Virtual host do RabbitMQ a usar.</summary>
    [Required] public string VirtualHost { get; set; } = "/";

    /// <summary>Quantidade de mensagens não confirmadas que cada canal busca por vez (QoS prefetch).</summary>
    [Required] public ushort PrefetchCount { get; set; } = 1;

    /// <summary>Tamanho máximo do pool de canais (limita a concorrência de publish/consume).</summary>
    [Required] public ushort MaxChannels { get; set; } = 1;

    /// <summary>Tempo máximo de espera (em segundos) por um canal livre antes de novo backoff.</summary>
    [Required] public ushort ChannelTimeoutSeconds { get; set; } = 15;
}
