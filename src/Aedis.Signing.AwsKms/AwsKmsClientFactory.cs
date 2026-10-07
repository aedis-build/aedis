using Amazon;
using Amazon.KeyManagementService;

namespace Aedis.Signing.AwsKms;

/// <summary>
///     Cria o cliente do KMS com as mesmas regras dos demais clientes AWS do Aedis: <c>ServiceUrl</c> tem
///     precedência sobre <c>Region</c>; chaves explícitas só quando informadas — senão a cadeia padrão do SDK.
/// </summary>
public static class AwsKmsClientFactory
{
    /// <summary>Constrói o cliente a partir das opções.</summary>
    public static IAmazonKeyManagementService Build(AwsKmsSigningOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var config = new AmazonKeyManagementServiceConfig {
            Timeout = TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds)
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl)) {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region)) {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return !string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey)
            ? new AmazonKeyManagementServiceClient(options.AccessKeyId, options.SecretAccessKey, config)
            : new AmazonKeyManagementServiceClient(config);
    }
}
