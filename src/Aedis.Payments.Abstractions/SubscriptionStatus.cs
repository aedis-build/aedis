namespace Aedis.Payments.Abstractions;

/// <summary>Estado de uma assinatura, normalizado entre provedores.</summary>
public enum SubscriptionStatus
{
    /// <summary>Estado que o provider não mapeia; consulte o provedor.</summary>
    Unknown,

    /// <summary>Criada, aguardando o primeiro pagamento.</summary>
    Incomplete,

    /// <summary>Primeiro pagamento não aconteceu no prazo; a assinatura não vai ativar.</summary>
    IncompleteExpired,

    /// <summary>Em período de teste.</summary>
    Trialing,

    /// <summary>Em dia.</summary>
    Active,

    /// <summary>Última fatura falhou e está em cobrança.</summary>
    PastDue,

    /// <summary>Tentativas esgotadas; fatura em aberto.</summary>
    Unpaid,

    /// <summary>Cobrança pausada.</summary>
    Paused,

    /// <summary>Encerrada.</summary>
    Canceled,
}
