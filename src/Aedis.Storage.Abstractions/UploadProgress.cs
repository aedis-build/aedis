namespace Aedis.Storage.Abstractions;

public sealed record UploadProgress(
    string? FilePath,
    long TransferredBytes,
    long TotalBytes,
    int PercentDone
);