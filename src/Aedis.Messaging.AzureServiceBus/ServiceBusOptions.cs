using System.ComponentModel.DataAnnotations;

namespace Aedis.Messaging.AzureServiceBus;

/// <summary>
///     Opções do provider Azure Service Bus do Aedis. Lidas da seção <c>ServiceBus</c> da configuração.
/// </summary>
public sealed class ServiceBusOptions
{
    /// <summary>Nome da seção de configuração de onde as opções são lidas (<c>ServiceBus</c>).</summary>
    public const string SectionName = "ServiceBus";

    /// <summary>Connection string do namespace do Azure Service Bus.</summary>
    [Required]
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    ///     Mensagens processadas em paralelo por consumidor (MaxConcurrentCalls do ServiceBusProcessor).
    ///     Padrão 1 (sequencial). Para escalar, rode mais instâncias da aplicação.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>Timeout das operações de conexão, em segundos. Padrão 15.</summary>
    public int ConnectionTimeoutSeconds { get; set; } = 15;
}
