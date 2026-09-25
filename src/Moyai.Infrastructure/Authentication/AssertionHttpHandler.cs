using System.Net.Http.Headers;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>MCP tools/callにだけ1回分のAssertionを付与します。</summary>
public sealed class AssertionHttpHandler(HttpClient client, IAssertionIssuer issuer, AssertionContext context, IAssertionAudit? audit = null) : HttpMessageHandler
{
    public string KeyId { get; private set; } = "";
    private string? _assertion;
    private int _callSent;
    public string? Redact(string? value) => value is null || _assertion is null ? value : value.Replace(_assertion, "[assertion redacted]", StringComparison.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var outbound = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers)
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) outbound.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            outbound.Content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers) outbound.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("method", out JsonElement method) && method.GetString() == "tools/call")
            {
                if (Interlocked.Exchange(ref _callSent, 1) != 0) throw new ProviderAuthenticationException("authentication_unavailable");
                if (document.RootElement.GetProperty("params").GetProperty("name").GetString() != context.Tool)
                    throw new ProviderAuthenticationException("auth_scope_denied");
                SignedAssertion assertion = await issuer.IssueAsync(context, cancellationToken).ConfigureAwait(false);
                KeyId = assertion.KeyId;
                _assertion = assertion.Value;
                if (audit is not null) await audit.WriteAsync(context, KeyId, "issued", cancellationToken).ConfigureAwait(false);
                outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion.Value);
                outbound.Headers.Remove("X-Moyai-Operation-Id");
                outbound.Headers.Add("X-Moyai-Operation-Id", context.OperationId);
            }
        }
        try { return await client.SendAsync(outbound, cancellationToken).ConfigureAwait(false); }
        finally { outbound.Headers.Authorization = null; }
    }

    protected override void Dispose(bool disposing)
    {
        _assertion = null;
        base.Dispose(disposing);
    }
}
