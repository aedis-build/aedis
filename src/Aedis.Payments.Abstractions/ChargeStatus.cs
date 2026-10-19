namespace Aedis.Payments.Abstractions;

/// <summary>Resultado de uma tentativa de cobrança.</summary>
public enum ChargeStatus
{
    /// <summary>Paga.</summary>
    Paid,

    /// <summary>Recusada (cartão, saldo, antifraude); a aplicação decide quando tentar de novo.</summary>
    Failed,

    /// <summary>O banco pediu autenticação do portador; só o cliente presente resolve.</summary>
    RequiresAction,

    /// <summary>Em processamento; o resultado final chega por webhook.</summary>
    Pending,
}
