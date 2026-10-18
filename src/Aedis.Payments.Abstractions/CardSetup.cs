namespace Aedis.Payments.Abstractions;

/// <summary>
///     O que o front-end precisa para coletar um cartão novo com o componente do provedor (o número nunca passa pela
///     aplicação). Ao confirmar, o provider torna o cartão o padrão do cliente e emite <see cref="PaymentMethodUpdated" />.
/// </summary>
/// <param name="ClientSecret">Segredo de uso único da intenção de coleta; entregue só ao dono da sessão.</param>
/// <param name="PublishableKey">Chave pública da conta onde o cliente vive.</param>
public sealed record CardSetup(string ClientSecret, string PublishableKey);
