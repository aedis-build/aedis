using Microsoft.Extensions.Logging;
using NSign;
using NSign.Http;
using NSign.Signatures;

namespace Aedis.Signing;

/// <summary>
///     <see cref="MessageContext" /> da NSign sobre um <see cref="HttpRequestMessage" /> pronto para envio. Lê
///     headers de request e de conteúdo; escreve os headers de assinatura no request.
/// </summary>
internal sealed class HttpRequestMessageSigningContext(
    ILogger logger,
    HttpFieldOptions httpFieldOptions,
    HttpRequestMessage request,
    CancellationToken cancellationToken,
    MessageSigningOptions? signingOptions)
    : MessageContext(logger, httpFieldOptions)
{
    private readonly HttpRequestMessage _request = request ?? throw new ArgumentNullException(nameof(request));

    private readonly Uri _targetUri = request.RequestUri is { IsAbsoluteUri: true } uri
        ? uri
        : throw new ArgumentException("A requisição precisa ter URI absoluta para ser assinada.", nameof(request));

    public override bool HasResponse => false;
    public override CancellationToken Aborted => cancellationToken;
    public override MessageSigningOptions? SigningOptions => signingOptions;

    public override void AddHeader(string headerName, string value) {
        _request.Headers.Remove(headerName);
        _request.Headers.TryAddWithoutValidation(headerName, value);
    }

    public override string? GetDerivedComponentValue(DerivedComponent component) =>
        SignatureComponents.DerivedValue(component, _request.Method.Method, _targetUri);

    public override IEnumerable<string> GetHeaderValues(string headerName) {
        if (_request.Headers.TryGetValues(headerName, out var values)) return values;
        if (_request.Content is { } content) {
            if (content.Headers.TryGetValues(headerName, out var contentValues)) return contentValues;
            if (headerName.Equals("content-length", StringComparison.OrdinalIgnoreCase) && content.Headers.ContentLength is { } length)
                return [length.ToString()];
        }

        return [];
    }

    public override IEnumerable<string> GetRequestHeaderValues(string headerName) => GetHeaderValues(headerName);

    public override IEnumerable<string> GetTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetRequestTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetQueryParamValues(string paramName) =>
        SignatureComponents.QueryParamValues(_targetUri, paramName);
}
