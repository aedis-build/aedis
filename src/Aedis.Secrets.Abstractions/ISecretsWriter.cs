namespace Aedis.Secrets.Abstractions;

/// <summary>
///     Contrato agnóstico de <strong>escrita</strong> de segredos, separado de <see cref="ISecretsProvider" />
///     para que a maioria das aplicações opere com permissão somente-leitura no cofre e apenas quem rotaciona
///     ou provisiona segredos receba a permissão de escrita. Os providers do Aedis implementam ambos os
///     contratos; o registro de DI expõe este só quando o provider concreto o suporta. Valores nunca são
///     logados.
/// </summary>
public interface ISecretsWriter
{
    /// <summary>
    ///     Cria o segredo <paramref name="name" /> ou grava uma nova versão quando ele já existe. Idempotente
    ///     quanto à existência; cada chamada produz uma versão nova no cofre.
    /// </summary>
    Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Remove o segredo <paramref name="name" />. Segue a política de retenção do cofre (soft delete /
    ///     janela de recuperação) quando ela existe. Segredo inexistente não é erro.
    /// </summary>
    Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default);
}
