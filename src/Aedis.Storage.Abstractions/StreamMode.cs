namespace Aedis.Storage.Abstractions;

public enum StreamMode : byte
{
    Default = 0,
    Memory = 1,
    TempFile = 2,
    Chunked = 3
}