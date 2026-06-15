namespace Aedis.Storage.AzureBlob;

/// <summary>
///     Configuração de conexão para um container do Azure Blob Storage.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    /// <summary>Connection string da conta de armazenamento (obrigatória).</summary>
    public required string ConnectionString { get; set; }

    /// <summary>Nome do container (obrigatório).</summary>
    public required string ContainerName { get; set; }

    /// <summary>Prefixo opcional aplicado a todas as chaves.</summary>
    public string Prefix { get; set; } = string.Empty;
}
