using System.Text.RegularExpressions;
using MessagePack;

namespace Aedis.Storage.Abstractions;

[MessagePackObject]
[method: SerializationConstructor]
[method: System.Text.Json.Serialization.JsonConstructor]
public record BucketObject(
    [property: Key(0)] string BucketName,
    [property: Key(1)] string FilePath,
    [property: Key(2)] DateTimeOffset LastModified)
{
    public BucketObject(string bucketName, string filePath)
        : this(bucketName, filePath, DateTimeOffset.UtcNow) { }

    [Key(3)] public string FileName => Path.GetFileName(FilePath);

    public override string ToString() {
        return Regex.Replace($"{BucketName}/{FilePath}", "/{2,}", "/");
    }
}
