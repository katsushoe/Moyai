using System.Text.Json.Serialization;

namespace Moyai.ProviderAuthentication;

/// <summary>Provider認証の公開エラーです。</summary>
public sealed class ProviderAuthenticationException(string code) : Exception(code)
{
    /// <summary>呼び出し元が処理する安定したエラーコードです。</summary>
    public string Code { get; } = code;
}

/// <summary>署名鍵の検証可能な状態です。</summary>
[JsonConverter(typeof(SigningKeyStateJsonConverter))]
public enum SigningKeyState { Next, Active, Retiring, Retired, Revoked }

/// <summary>状態を仕様の小文字文字列として表現します。</summary>
public sealed class SigningKeyStateJsonConverter()
    : JsonStringEnumConverter<SigningKeyState>(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

/// <summary>公開鍵だけを含むTrust Bundleエントリです。</summary>
public sealed record AssertionTrustKey(
    string Issuer,
    string Kid,
    string Algorithm,
    string X,
    string Y,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    SigningKeyState Status)
{
    /// <summary>公開鍵をJWKフィールドとして返します。</summary>
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

/// <summary>Providerが管理情報から構成する期待Contextです。</summary>
public sealed record AssertionContext(
    string ProtocolVersion,
    string Provider,
    Guid Project,
    string Tool,
    IReadOnlyList<string> Scopes,
    string OperationId,
    string? Repository = null,
    string? ResourceKind = null,
    string? Resource = null)
{
    /// <summary>Repository Provider用のProtocol v1 Contextを作成します。</summary>
    public static AssertionContext ForRepository(
        string provider,
        Guid project,
        string repository,
        string tool,
        IReadOnlyList<string> scopes,
        string operationId) => new("1", provider, project, tool, scopes, operationId, Repository: repository);

    /// <summary>Lifecycle Provider用のProtocol v2 Contextを作成します。</summary>
    public static AssertionContext ForResource(
        string provider,
        Guid project,
        string resourceKind,
        string resource,
        string tool,
        IReadOnlyList<string> scopes,
        string operationId) => new("2", provider, project, tool, scopes, operationId, ResourceKind: resourceKind, Resource: resource);
}

/// <summary>検証後にのみProvider処理へ渡すPrincipalです。</summary>
public sealed record AssertionPrincipal(string Issuer, string KeyId, AssertionContext Context)
{
    /// <summary>JWTのexpです。実行直前検証では設定済みClock Skewを加えて判定します。</summary>
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>各要求で最新の失効状態を取得します。</summary>
public interface IAssertionTrustStore
{
    /// <summary>IssuerとKey IDに一致する公開鍵を取得します。</summary>
    Task<AssertionTrustKey?> FindAsync(string expectedIssuer, string keyId, CancellationToken cancellationToken = default);
}

/// <summary>Provider検証境界です。</summary>
public interface IAssertionValidator
{
    /// <summary>Assertionを検証し、成功時だけPrincipalを返します。</summary>
    Task<AssertionPrincipal> ValidateAsync(string assertion, AssertionContext expected, CancellationToken cancellationToken = default);
}

/// <summary>承認待機後などにReplayを再予約せず実行可否を再検証します。</summary>
public interface IAssertionExecutionValidator
{
    /// <summary>期限、鍵状態、Capabilityを再確認し、成功時だけProvider処理を続行できます。</summary>
    Task EnsureCurrentAsync(AssertionPrincipal principal, CancellationToken cancellationToken = default);
}

/// <summary>原子的Replay防止Contractです。</summary>
public interface IAssertionReplayCache
{
    /// <summary>未使用のJTIを期限まで予約します。</summary>
    Task<bool> TryUseAsync(string issuer, string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

/// <summary>Issuerと時刻制約を定義します。</summary>
public sealed record AssertionOptions(string Issuer, int LifetimeSeconds = 120, int ClockSkewSeconds = 30)
{
    /// <summary>設定値を検証します。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer)
            || !Issuer.StartsWith("moyai:", StringComparison.Ordinal)
            || Issuer.Length > 200
            || Issuer.Any(char.IsControl)
            || LifetimeSeconds is < 30 or > 300
            || ClockSkewSeconds is < 0 or > 60)
        {
            throw new ProviderAuthenticationException("authentication_unavailable");
        }
    }
}

/// <summary>Providerが公開する検証Capabilityです。</summary>
public sealed record AssertionCapability(
    string ProviderId,
    string RequiredAudience,
    string ProtocolVersion,
    string Algorithm,
    bool ReplayProtection,
    IReadOnlyDictionary<string, string[]> ToolScopes,
    IReadOnlyCollection<string>? ResourceKinds = null)
{
    /// <summary>期待ContextがCapabilityに含まれることを確認します。</summary>
    public void Require(AssertionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(ProviderId, context.Provider, StringComparison.Ordinal)
            || !string.Equals(RequiredAudience, context.Provider, StringComparison.Ordinal))
        {
            throw new ProviderAuthenticationException("auth_audience_mismatch");
        }

        if (!string.Equals(ProtocolVersion, context.ProtocolVersion, StringComparison.Ordinal)
            || !string.Equals(Algorithm, "ES256", StringComparison.Ordinal)
            || !ReplayProtection
            || !ToolScopes.TryGetValue(context.Tool, out string[]? scopes))
        {
            throw new ProviderAuthenticationException("provider_capability_missing");
        }

        if (context.Scopes.Count == 0
            || !context.Scopes.ToHashSet(StringComparer.Ordinal).SetEquals(scopes))
        {
            throw new ProviderAuthenticationException("auth_scope_denied");
        }

        if (context.ProtocolVersion == "2"
            && (ResourceKinds is null
                || context.ResourceKind is null
                || !ResourceKinds.Contains(context.ResourceKind, StringComparer.Ordinal)))
        {
            throw new ProviderAuthenticationException("provider_capability_missing");
        }
    }
}
