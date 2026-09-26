using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moyai.ProviderAuthentication;

/// <summary>Protocol v1・v2の署名、Context、Capability、Replayを検証します。</summary>
public sealed class Es256AssertionValidator(
    IAssertionTrustStore trust,
    IAssertionReplayCache replay,
    AssertionOptions options,
    AssertionCapability capability,
    TimeProvider? clock = null) : IAssertionValidator, IAssertionExecutionValidator
{
    private static readonly string[] HeaderClaims = ["alg", "kid", "typ"];
    private static readonly string[] CommonClaims =
    [
        "aud", "exp", "iat", "iss", "jti", "nbf", "operation_id", "project", "protocol_version", "prv", "scope", "sub",
    ];
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<AssertionPrincipal> ValidateAsync(
        string assertion,
        AssertionContext expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        options.Validate();
        try
        {
            if (string.IsNullOrEmpty(assertion))
            {
                throw Error("auth_assertion_missing");
            }

            if (assertion.Length > 16_384)
            {
                throw Error("auth_assertion_invalid");
            }

            string[] parts = assertion.Split('.');
            if (parts.Length != 3 || parts[0].Length > 2_048)
            {
                throw Error("auth_assertion_invalid");
            }

            using JsonDocument header = AssertionJson.ParseObject(AssertionJson.Decode(parts[0]));
            using JsonDocument payload = AssertionJson.ParseObject(AssertionJson.Decode(parts[1]));
            JsonElement headerRoot = header.RootElement;
            JsonElement payloadRoot = payload.RootElement;
            RequireExactClaims(headerRoot, HeaderClaims);
            if (!string.Equals(Text(headerRoot, "alg"), "ES256", StringComparison.Ordinal)
                || !string.Equals(Text(headerRoot, "typ"), "JWT", StringComparison.Ordinal))
            {
                throw Error("auth_assertion_invalid");
            }

            string keyId = Text(headerRoot, "kid");
            AssertionTrustKey? key = await trust.FindAsync(options.Issuer, keyId, cancellationToken).ConfigureAwait(false);
            if (key is null)
            {
                throw Error("auth_key_unknown");
            }

            VerifySignature(parts, key);
            ValidateKey(key, keyId);
            ValidateIdentity(payloadRoot, expected);
            ValidateTime(payloadRoot);
            ValidateContext(payloadRoot, expected);
            ValidateScopes(payloadRoot, expected);
            capability.Require(expected);

            string jti = Text(payloadRoot, "jti");
            if (jti.Length is < 22 or > 128
                || !jti.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            {
                throw Error("auth_assertion_invalid");
            }

            long expiry = payloadRoot.GetProperty("exp").GetInt64();
            DateTimeOffset replayExpiry = DateTimeOffset.FromUnixTimeSeconds(expiry).AddSeconds(options.ClockSkewSeconds);
            if (!await replay.TryUseAsync(options.Issuer, jti, replayExpiry, cancellationToken).ConfigureAwait(false))
            {
                throw Error("auth_replay_detected");
            }

            // Replay永続化中に期限を越えたAssertionをProvider Toolへ渡しません。
            ValidateTime(payloadRoot);

            return new AssertionPrincipal(options.Issuer, keyId, expected)
            {
                ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiry),
            };
        }
        catch (Exception exception) when (exception is JsonException
            or FormatException
            or KeyNotFoundException
            or InvalidOperationException
            or CryptographicException
            or ArgumentException
            or OverflowException)
        {
            throw Error("auth_assertion_invalid");
        }
    }

    /// <inheritdoc />
    public async Task EnsureCurrentAsync(
        AssertionPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        options.Validate();
        if (!string.Equals(principal.Issuer, options.Issuer, StringComparison.Ordinal)
            || principal.ExpiresAtUtc == default)
        {
            throw Error("auth_assertion_invalid");
        }

        if (_clock.GetUtcNow() >= principal.ExpiresAtUtc.AddSeconds(options.ClockSkewSeconds))
        {
            throw Error("auth_assertion_expired");
        }

        capability.Require(principal.Context);
        AssertionTrustKey? key = await trust.FindAsync(
            options.Issuer,
            principal.KeyId,
            cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            throw Error("auth_key_unknown");
        }

        ValidateKey(key, principal.KeyId);
    }

    private static void VerifySignature(string[] parts, AssertionTrustKey key)
    {
        byte[] signature = AssertionJson.Decode(parts[2]);
        byte[] x = AssertionJson.Decode(key.X);
        byte[] y = AssertionJson.Decode(key.Y);
        if (signature.Length != 64 || x.Length != 32 || y.Length != 32)
        {
            throw Error("auth_assertion_invalid");
        }

        using ECDsa verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        });
        byte[] input = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (!verifier.VerifyData(input, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw Error("auth_assertion_invalid");
        }
    }

    private void ValidateKey(AssertionTrustKey key, string keyId)
    {
        if (!string.Equals(key.Issuer, options.Issuer, StringComparison.Ordinal)
            || !string.Equals(key.Kid, keyId, StringComparison.Ordinal)
            || !string.Equals(key.Algorithm, "ES256", StringComparison.Ordinal))
        {
            throw Error("auth_assertion_invalid");
        }

        if (key.Status is SigningKeyState.Revoked or SigningKeyState.Retired)
        {
            throw Error("auth_key_revoked");
        }

        if (key.Status is not (SigningKeyState.Active or SigningKeyState.Retiring)
            || _clock.GetUtcNow() < key.NotBeforeUtc
            || _clock.GetUtcNow() >= key.NotAfterUtc)
        {
            throw Error("auth_key_unknown");
        }
    }

    private void ValidateIdentity(JsonElement payload, AssertionContext expected)
    {
        if (!string.Equals(Text(payload, "iss"), options.Issuer, StringComparison.Ordinal)
            || !string.Equals(Text(payload, "sub"), options.Issuer, StringComparison.Ordinal))
        {
            throw Error("auth_assertion_invalid");
        }

        if (!string.Equals(Text(payload, "aud"), capability.RequiredAudience, StringComparison.Ordinal)
            || !string.Equals(Text(payload, "prv"), capability.ProviderId, StringComparison.Ordinal)
            || !string.Equals(expected.Provider, capability.ProviderId, StringComparison.Ordinal))
        {
            throw Error("auth_audience_mismatch");
        }
    }

    private void ValidateTime(JsonElement payload)
    {
        long issuedAt = payload.GetProperty("iat").GetInt64();
        long notBefore = payload.GetProperty("nbf").GetInt64();
        long expires = payload.GetProperty("exp").GetInt64();
        if (issuedAt < 0
            || notBefore < 0
            || expires > 253_402_300_799
            || expires - issuedAt is < 30 or > 300
            || notBefore > issuedAt
            || issuedAt - notBefore > 60)
        {
            throw Error("auth_assertion_invalid");
        }

        long now = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (now >= expires + options.ClockSkewSeconds)
        {
            throw Error("auth_assertion_expired");
        }

        if (issuedAt > now + options.ClockSkewSeconds || notBefore > now + options.ClockSkewSeconds)
        {
            throw Error("auth_assertion_not_yet_valid");
        }
    }

    private static void ValidateContext(JsonElement payload, AssertionContext expected)
    {
        string protocol = Text(payload, "protocol_version");
        if (protocol is not ("1" or "2") || !string.Equals(protocol, expected.ProtocolVersion, StringComparison.Ordinal))
        {
            throw Error("auth_protocol_unsupported");
        }

        string[] protocolClaims = protocol == "1" ? [.. CommonClaims, "repository"] : [.. CommonClaims, "resource", "resource_kind"];
        RequireExactClaims(payload, protocolClaims);
        if (!Guid.TryParseExact(Text(payload, "project"), "D", out Guid project)
            || project == Guid.Empty
            || project != expected.Project
            || !string.Equals(Text(payload, "operation_id"), expected.OperationId, StringComparison.Ordinal))
        {
            throw Error("auth_project_mismatch");
        }

        if (protocol == "1")
        {
            if (expected.ResourceKind is not null
                || expected.Resource is not null
                || string.IsNullOrWhiteSpace(expected.Repository)
                || !string.Equals(Text(payload, "repository"), expected.Repository, StringComparison.Ordinal))
            {
                throw Error("auth_project_mismatch");
            }

            return;
        }

        if (expected.Repository is not null
            || string.IsNullOrWhiteSpace(expected.ResourceKind)
            || string.IsNullOrWhiteSpace(expected.Resource)
            || !string.Equals(Text(payload, "resource_kind"), expected.ResourceKind, StringComparison.Ordinal)
            || !string.Equals(Text(payload, "resource"), expected.Resource, StringComparison.Ordinal))
        {
            throw Error("auth_project_mismatch");
        }
    }

    private static void ValidateScopes(JsonElement payload, AssertionContext expected)
    {
        JsonElement scope = payload.GetProperty("scope");
        if (scope.ValueKind != JsonValueKind.Array || scope.GetArrayLength() is < 1 or > 32)
        {
            throw Error("auth_scope_denied");
        }

        string[] scopes = scope.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray();
        if (scopes.Any(string.IsNullOrWhiteSpace)
            || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length
            || !scopes.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Scopes))
        {
            throw Error("auth_scope_denied");
        }
    }

    private static void RequireExactClaims(JsonElement element, IReadOnlyCollection<string> required)
    {
        HashSet<string> actual = element.EnumerateObject().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(required))
        {
            throw Error("auth_assertion_invalid");
        }
    }

    private static string Text(JsonElement element, string name)
    {
        string? value = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_024 || value.Any(char.IsControl))
        {
            throw Error("auth_assertion_invalid");
        }

        return value;
    }

    private static ProviderAuthenticationException Error(string code) => new(code);
}
