namespace Aedis.Storage.Abstractions;

/// <summary>
///     Instantâneo do andamento de um upload, entregue ao callback de progresso a cada bloco transferido.
///     Use para atualizar barras de progresso ou logs durante o envio de um objeto.
/// </summary>
/// <param name="FilePath">Chave/caminho do objeto sendo enviado, quando disponível.</param>
/// <param name="TransferredBytes">Total de bytes já transferidos.</param>
/// <param name="TotalBytes">Total de bytes a transferir; 0 quando o tamanho é desconhecido.</param>
/// <param name="PercentDone">Percentual concluído (0–100); 0 quando o total é desconhecido.</param>
public sealed record UploadProgress(
    string? FilePath,
    long TransferredBytes,
    long TotalBytes,
    int PercentDone
);
