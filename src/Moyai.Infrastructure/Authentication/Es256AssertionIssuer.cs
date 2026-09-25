using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>最小権限Contextから短寿命JWTを発行します。</summary>
public sealed class Es256AssertionIssuer(IAssertionSigner signer, AssertionOptions options, TimeProvider clock) : IAssertionIssuer
{
    public async Task<SignedAssertion> IssueAsync(AssertionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        options.Validate();
        if (context.Project == Guid.Empty || context.Scopes.Count == 0 || string.IsNullOrWhiteSpace(context.OperationId)
            || context.ProtocolVersion is not ("1" or "2"))
            throw new ProviderAuthenticationException("auth_project_mismatch");
        if (context.ProtocolVersion == "1" && (string.IsNullOrWhiteSpace(context.Repository)
            || context.ResourceKind is not null || context.Resource is not null))
            throw new ProviderAuthenticationException("auth_project_mismatch");
        if (context.ProtocolVersion == "2" && (context.Repository is not null
            || string.IsNullOrWhiteSpace(context.ResourceKind) || string.IsNullOrWhiteSpace(context.Resource)))
            throw new ProviderAuthenticationException("auth_project_mismatch");
        AssertionTrustKey key = await signer.GetActiveKeyAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.GetUtcNow();
        if (key.Issuer != options.Issuer || key.Status != SigningKeyState.Active || key.Algorithm != "ES256"
            || key.NotBeforeUtc > now || key.NotAfterUtc <= now.AddSeconds(options.LifetimeSeconds + options.ClockSkewSeconds))
            throw new ProviderAuthenticationException("authentication_unavailable");
        string header = AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256", kid = key.Kid }));
        long issued = now.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["iss"] = options.Issuer,
            ["sub"] = options.Issuer,
            ["aud"] = context.Provider,
            ["iat"] = issued,
            ["nbf"] = issued - options.ClockSkewSeconds,
            ["exp"] = issued + options.LifetimeSeconds,
            ["jti"] = AssertionJson.Encode(RandomNumberGenerator.GetBytes(16)),
            ["prv"] = context.Provider,
            ["project"] = context.Project.ToString("D"),
            ["scope"] = context.Scopes,
            ["protocol_version"] = context.ProtocolVersion,
            ["operation_id"] = context.OperationId,
        };
        if (context.ProtocolVersion == "1") claims["repository"] = context.Repository;
        else { claims["resource_kind"] = context.ResourceKind; claims["resource"] = context.Resource; }
        string payload = AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        string input = header + "." + payload;
        byte[] signature = await signer.SignAsync(key.Kid, Encoding.ASCII.GetBytes(input), cancellationToken).ConfigureAwait(false);
        if (signature.Length != 64) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        return new SignedAssertion(input + "." + AssertionJson.Encode(signature), key.Kid);
    }
}
