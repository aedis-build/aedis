namespace Aedis.Http.Abstractions.Authentication;

/// <summary>
///     Token de acesso cacheado com seus dois instantes-chave: <see cref="ExpiresAt" /> (quando o token
///     realmente expira no provedor) e <see cref="RefreshAt" /> (quando deve ser renovado de forma proativa,
///     antes de expirar). Entre <see cref="RefreshAt" /> e <see cref="ExpiresAt" /> o token ainda é válido e
///     pode ser servido enquanto uma renovação ocorre em segundo plano (serve-while-revalidate).
/// </summary>
/// <param name="AccessToken">O token de acesso (Bearer).</param>
/// <param name="ExpiresAt">Instante em que o token expira de fato no provedor.</param>
/// <param name="RefreshAt">Instante a partir do qual o token deve ser renovado proativamente.</param>
public sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset RefreshAt)
{
    /// <summary>Indica se o token já expirou em <paramref name="now" /> (não deve mais ser usado).</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Indica se o token entrou na janela de renovação proativa em <paramref name="now" /> (ainda válido).</summary>
    public bool IsDueForRefresh(DateTimeOffset now) => now >= RefreshAt;
}
