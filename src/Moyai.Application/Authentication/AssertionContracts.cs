using System.Text.Json.Serialization;

namespace Moyai.Application.Authentication;

/// <summary>Provider認証の公開エラーです。秘密を含む内部例外を保持しません。</summary>
public sealed class ProviderAuthenticationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>署名鍵の検証可能な状態です。</summary>
[JsonConverter(typeof(SigningKeyStateJsonConverter))]
public enum SigningKeyState { Next, Active, Retiring, Retired, Revoked }

/// <summary>状態を仕様の小文字文字列として表現します。</summary>
public sealed class SigningKeyStateJsonConverter() : JsonStringEnumConverter<SigningKeyState>(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

/// <summary>公開鍵だけを含むTrust Bundleエントリです。</summary>
public sealed record AssertionTrustKey(string Issuer, string Kid, string Algorithm, string X, string Y,
    DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc, SigningKeyState Status)
{
    public IReadOnlyDictionary<string, string> PublicKeyJwk => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["kty"] = "EC",
        ["crv"] = "P-256",
        ["x"] = X,
        ["y"] = Y,
        ["kid"] = Kid,
        ["alg"] = Algorithm,
    };
}

/// <summary>操作認可後に構成するContextです。Client入力を直接渡しません。</summary>
public sealed record AssertionContext(string Provider, Guid Project, string? Repository, string Tool,
    IReadOnlyList<string> Scopes, string OperationId, string ProtocolVersion = "1",
    string? ResourceKind = null, string? Resource = null);

/// <summary>JWTを診断文字列やJSONへ露出させない一時値です。</summary>
public sealed class SignedAssertion(string value, string keyId)
{
    [JsonIgnore] public string Value { get; } = value;
    public string KeyId { get; } = keyId;
    public override string ToString() => "[assertion redacted]";
}

/// <summary>検証後にのみAdapterへ渡すPrincipalです。</summary>
public sealed record AssertionPrincipal(string Issuer, string KeyId, AssertionContext Context);

/// <summary>操作単位でAssertionを発行します。</summary>
public interface IAssertionIssuer
{
    Task<SignedAssertion> IssueAsync(AssertionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Coreから秘密鍵とOS APIを分離します。</summary>
public interface IAssertionSigner
{
    Task<AssertionTrustKey> GetActiveKeyAsync(CancellationToken cancellationToken = default);
    Task<byte[]> SignAsync(string keyId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>各要求で最新の失効状態を取得します。</summary>
public interface IAssertionTrustStore
{
    Task<AssertionTrustKey?> FindAsync(string expectedIssuer, string keyId, CancellationToken cancellationToken = default);
}

/// <summary>共通Provider検証境界です。</summary>
public interface IAssertionValidator
{
    Task<AssertionPrincipal> ValidateAsync(string assertion, AssertionContext expected, CancellationToken cancellationToken = default);
}

/// <summary>全Consumerで共有する原子的Replay防止Contractです。</summary>
public interface IAssertionReplayCache
{
    Task<bool> TryUseAsync(string issuer, string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

/// <summary>公開設定です。環境固有値は必ず明示します。</summary>
public sealed record AssertionOptions(string Issuer, int LifetimeSeconds = 120, int ClockSkewSeconds = 30)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || !Issuer.StartsWith("moyai:", StringComparison.Ordinal)
            || Issuer.Length > 200 || Issuer.Any(char.IsControl) || LifetimeSeconds is < 30 or > 300
            || ClockSkewSeconds is < 0 or > 60)
            throw new ProviderAuthenticationException("authentication_unavailable");
    }
}

/// <summary>Capabilityの公開情報です。管理されたProvider設定から取得します。</summary>
public sealed record AssertionCapability(string ProviderId, string RequiredAudience, string ProtocolVersion,
    string Algorithm, bool ReplayProtection, IReadOnlyDictionary<string, string[]> ToolScopes,
    IReadOnlyCollection<string>? ResourceKinds = null)
{
    public void Require(AssertionContext context)
    {
        if (ProviderId != context.Provider || RequiredAudience != context.Provider)
            throw new ProviderAuthenticationException("auth_audience_mismatch");
        if (ProtocolVersion is not ("1" or "2") || ProtocolVersion != context.ProtocolVersion
            || Algorithm != "ES256" || !ReplayProtection
            || !ToolScopes.TryGetValue(context.Tool, out string[]? scopes))
            throw new ProviderAuthenticationException("provider_capability_missing");
        if (context.Scopes.Count == 0 || !context.Scopes.ToHashSet(StringComparer.Ordinal).SetEquals(scopes))
            throw new ProviderAuthenticationException("auth_scope_denied");
        if (ProtocolVersion == "2" && (ResourceKinds is null || context.ResourceKind is null
            || !ResourceKinds.Contains(context.ResourceKind, StringComparer.Ordinal)))
            throw new ProviderAuthenticationException("provider_capability_missing");
    }
}
