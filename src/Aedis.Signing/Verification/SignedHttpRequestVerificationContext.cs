using Aedis.Signing.Abstractions;
using Microsoft.Extensions.Logging;
using NSign;
using NSign.Http;
using NSign.Signatures;

namespace Aedis.Signing.Verification;

/// <summary><see cref="MessageContext" /> da NSign sobre uma <see cref="SignedHttpRequest" /> recebida (só leitura).</summary>
internal sealed class SignedHttpRequestVerificationContext(
    ILogger logger,
    HttpFieldOptions httpFieldOptions,
    SignedHttpRequest request,
    SignatureVerificationOptions verificationOptions,
    CancellationToken cancellationToken)
    : MessageContext(logger, httpFieldOptions)
{
    private readonly SignedHttpRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public override bool HasResponse => false;
    public override CancellationToken Aborted => cancellationToken;
    public override SignatureVerificationOptions? VerificationOptions => verificationOptions;

    public override void AddHeader(string headerName, string value) =>
        throw new NotSupportedException("Uma requisição recebida é somente leitura.");

    public override string? GetDerivedComponentValue(DerivedComponent component) =>
        SignatureComponents.DerivedValue(component, _request.Method, _request.TargetUri);

    public override IEnumerable<string> GetHeaderValues(string headerName) {
        var values = _request.GetHeaderValues(headerName).ToList();
        if (values.Count == 0 && headerName.Equals("content-length", StringComparison.OrdinalIgnoreCase) && _request.Body.Length > 0)
            return [_request.Body.Length.ToString()];
        return values;
    }

    public override IEnumerable<string> GetRequestHeaderValues(string headerName) => GetHeaderValues(headerName);

    public override IEnumerable<string> GetTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetRequestTrailerValues(string fieldName) => [];

    public override IEnumerable<string> GetQueryParamValues(string paramName) =>
        SignatureComponents.QueryParamValues(_request.TargetUri, paramName);
}
