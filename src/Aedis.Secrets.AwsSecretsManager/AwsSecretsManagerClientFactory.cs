using Amazon;
using Amazon.SecretsManager;

namespace Aedis.Secrets.AwsSecretsManager;

/// <summary>
///     Constrói o cliente do AWS Secrets Manager a partir das opções. Sem <c>AccessKeyId</c>/<c>SecretAccessKey</c>
///     explícitos, usa a <strong>cadeia de credenciais padrão da AWS</strong> (env vars, perfil, role
///     IAM/IRSA). Com <c>ServiceUrl</c>, aponta para um endpoint local (LocalStack) e assina na região de
///     <c>AuthenticationRegion</c>.
/// </summary>
internal static class AwsSecretsManagerClientFactory
{
    public static IAmazonSecretsManager Build(AwsSecretsManagerOptions options) {
        var config = new AmazonSecretsManagerConfig();
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl)) {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region;
        }
        else if (!string.IsNullOrWhiteSpace(options.Region)) {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return !string.IsNullOrWhiteSpace(options.AccessKeyId) && !string.IsNullOrWhiteSpace(options.SecretAccessKey)
            ? new AmazonSecretsManagerClient(options.AccessKeyId, options.SecretAccessKey, config)
            : new AmazonSecretsManagerClient(config);
    }
}
