using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RABBITMQ";

    [Required] public string Host { get; set; } = null!;
    [Required] public int Port { get; set; } = 5672;
    [Required] public string Username { get; set; } = null!;
    [Required] public string Password { get; set; } = null!;
    [Required] public string VirtualHost { get; set; } = "/";
    [Required] public ushort PrefetchCount { get; set; } = 1;
    [Required] public ushort MaxChannels { get; set; } = 1;
    [Required] public ushort ChannelTimeoutSeconds { get; set; } = 15;
}
