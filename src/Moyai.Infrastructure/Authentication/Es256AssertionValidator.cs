using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>Provider非依存の署名、Context、Capability、Replay検証です。</summary>
public sealed class Es256AssertionValidator(IAssertionTrustStore trust, IAssertionReplayCache replay,
    AssertionOptions options, AssertionCapability capability, TimeProvider clock) : IAssertionValidator
{
    public async Task<AssertionPrincipal> ValidateAsync(string assertion, AssertionContext expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        options.Validate();
        try
        {
            if (string.IsNullOrEmpty(assertion)) throw Error("auth_assertion_missing");
            if (assertion.Length > 16384) throw Error("auth_assertion_invalid");
            string[] parts = assertion.Split('.');
            if (parts.Length != 3 || parts[0].Length > 2048) throw Error("auth_assertion_invalid");
            using JsonDocument header = AssertionJson.Parse(AssertionJson.Decode(parts[0]));
            using JsonDocument payload = AssertionJson.Parse(AssertionJson.Decode(parts[1]));
            JsonElement h = header.RootElement;
            JsonElement p = payload.RootElement;
            if (Text(h, "alg") != "ES256" || Text(h, "typ") != "JWT" || h.EnumerateObject().Count() != 3)
                throw Error("auth_assertion_invalid");
            string kid = Text(h, "kid");
            // The configured issuer is authoritative even before the payload is trusted.
            AssertionTrustKey? key = await trust.FindAsync(options.Issuer, kid, cancellationToken).ConfigureAwait(false);
            if (key is null) throw Error("auth_key_unknown");
            if (key.Issuer != options.Issuer || key.Kid != kid || key.Algorithm != "ES256") throw Error("auth_assertion_invalid");
            byte[] signature = AssertionJson.Decode(parts[2]);
            byte[] x = AssertionJson.Decode(key.X);
            byte[] y = AssertionJson.Decode(key.Y);
            if (signature.Length != 64 || x.Length != 32 || y.Length != 32) throw Error("auth_assertion_invalid");
            using ECDsa verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
            if (!verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Error("auth_assertion_invalid");
            if (key.Status is SigningKeyState.Revoked or SigningKeyState.Retired) throw Error("auth_key_revoked");
            if (key.Status is not (SigningKeyState.Active or SigningKeyState.Retiring)) throw Error("auth_key_unknown");
            if (Text(p, "iss") != options.Issuer || Text(p, "sub") != options.Issuer) throw Error("auth_assertion_invalid");
            if (Text(p, "aud") != expected.Provider || Text(p, "prv") != expected.Provider) throw Error("auth_audience_mismatch");
            if (Text(p, "protocol_version") != "1") throw Error("auth_protocol_unsupported");
            DateTimeOffset now = clock.GetUtcNow();
            if (now < key.NotBeforeUtc || now >= key.NotAfterUtc) throw Error("auth_key_unknown");
            long iat = p.GetProperty("iat").GetInt64();
            long nbf = p.GetProperty("nbf").GetInt64();
            long exp = p.GetProperty("exp").GetInt64();
            // Bound arithmetic before adding skew; NumericDate must be in the supported UTC range.
            if (iat < 0 || exp > 253402300799 || nbf < 0 || exp - iat is < 30 or > 300
                || nbf > iat || iat - nbf > 60) throw Error("auth_assertion_invalid");
            long utc = now.ToUnixTimeSeconds();
            if (utc >= exp + options.ClockSkewSeconds) throw Error("auth_assertion_expired");
            if (iat > utc + options.ClockSkewSeconds || nbf > utc + options.ClockSkewSeconds) throw Error("auth_assertion_not_yet_valid");
            if (!Guid.TryParseExact(Text(p, "project"), "D", out Guid project) || project == Guid.Empty
                || project != expected.Project || Text(p, "repository") != expected.Repository)
                throw Error("auth_project_mismatch");
            string operationId = Text(p, "operation_id");
            if (operationId != expected.OperationId) throw Error("auth_project_mismatch");
            JsonElement scope = p.GetProperty("scope");
            if (scope.ValueKind != JsonValueKind.Array || scope.GetArrayLength() is < 1 or > 32) throw Error("auth_scope_denied");
            string[] scopes = scope.EnumerateArray().Select(static s => s.GetString() ?? "").ToArray();
            if (scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length
                || !scopes.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Scopes)) throw Error("auth_scope_denied");
            capability.Require(expected);
            string jti = Text(p, "jti");
            if (jti.Length is < 22 or > 128 || !jti.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw Error("auth_assertion_invalid");
            if (!await replay.TryUseAsync(options.Issuer, jti, DateTimeOffset.FromUnixTimeSeconds(exp).AddSeconds(options.ClockSkewSeconds), cancellationToken).ConfigureAwait(false))
                throw Error("auth_replay_detected");
            return new AssertionPrincipal(options.Issuer, kid, expected);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or CryptographicException or ArgumentException or OverflowException)
        {
            // Do not retain cryptographic inputs or parser excerpts in an inner exception.
            throw Error("auth_assertion_invalid");
        }
    }

    private static string Text(JsonElement element, string name)
    {
        string? value = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)) throw Error("auth_assertion_invalid");
        return value;
    }
    private static ProviderAuthenticationException Error(string code) => new(code);
}
