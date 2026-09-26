using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moyai.Configuration;

/// <summary>Persistent configuration shared by the service and its CLI client.</summary>
public sealed record MoyaiSettings
{
    public string DatabasePath { get; init; } = "../data/moyai.db";
    public string ServerUrl { get; init; } = "http://127.0.0.1:43120";
    public List<ProviderSettings> Providers { get; init; } = [];
    public int RequestTimeoutSeconds { get; init; } = 60;
    public ProviderAuthenticationSettings ProviderAuthentication { get; init; } = new();

    public static string DefaultPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "config", "moyai.json"));

    public static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads only the specified JSON file; never consults process environment.</summary>
    public static MoyaiSettings Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var settings = JsonSerializer.Deserialize<MoyaiSettings>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidOperationException("Configuration must be a JSON object.");
        settings.Validate();
        return settings with { DatabasePath = Path.GetFullPath(settings.DatabasePath, Path.GetDirectoryName(fullPath)!) };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath)) throw new InvalidOperationException("databasePath is required.");
        ValidateUrl(ServerUrl);
        var serverUri = new Uri(ServerUrl);
        if (serverUri.AbsolutePath != "/" || serverUri.Query.Length != 0 || serverUri.Fragment.Length != 0)
            throw new InvalidOperationException("serverUrl must be a listener origin without path, query or fragment.");
        if (RequestTimeoutSeconds is < 1 or > 3600) throw new InvalidOperationException("requestTimeoutSeconds must be 1..3600.");
        if (Providers is null) throw new InvalidOperationException("providers must be an array.");
        ProviderAuthentication.Validate();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProviderSettings provider in Providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.Name) || string.IsNullOrWhiteSpace(provider.ToolPrefix))
                throw new InvalidOperationException("Each provider needs name and toolPrefix.");
            ValidateUrl(provider.Endpoint);
            if (!names.Add(provider.Name)) throw new InvalidOperationException("Duplicate provider name.");
        }
    }

    public static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Endpoint must be an absolute HTTP(S) loopback URL without credentials.");
    }
}

/// <summary>Provider endpoint, role and MCP tool prefix; contains no credentials.</summary>
public sealed record ProviderSettings(string Name, string Endpoint, string ToolPrefix, bool Repository = false)
{
    public Dictionary<string, string[]> AssertionToolScopes { get; init; } = new(StringComparer.Ordinal);

    public string AssertionProviderId => Name switch
    {
        "githubbie" => "githubie",
        _ => Name,
    };
}

/// <summary>公開設定だけを保持するProvider認証構成です。</summary>
public sealed record ProviderAuthenticationSettings
{
    public string Mode { get; init; } = "assertion";
    public DateTimeOffset? LegacyStartedAt { get; init; }
    public DateTimeOffset? LegacyUntil { get; init; }
    public string Issuer { get; init; } = "";
    public int LifetimeSeconds { get; init; } = 120;
    public int ClockSkewSeconds { get; init; } = 30;
    public string ProtectorMode { get; init; } = "cng";
    public string KeyNamespace { get; init; } = "";
    public string ActiveKeyVersion { get; init; } = "";
    public string BrokerEndpoint { get; init; } = "";
    public string BrokerCertificateThumbprint { get; init; } = "";
    public string SecretToolPath { get; init; } = "";

    public void Validate()
    {
        if (Mode is not ("assertion" or "legacy") || LifetimeSeconds is < 30 or > 300 || ClockSkewSeconds is < 0 or > 60)
            throw new InvalidOperationException("Invalid provider authentication configuration.");
        if (Mode == "legacy" && (LegacyStartedAt is null || LegacyUntil is null || LegacyUntil <= LegacyStartedAt
            || LegacyUntil - LegacyStartedAt > TimeSpan.FromDays(7)))
            throw new InvalidOperationException("Legacy authentication requires an explicit window of at most seven days.");
        if (ProtectorMode is not ("cng" or "keychain" or "secret-service" or "broker")) throw new InvalidOperationException("Unsupported key protector mode.");
        if (ProtectorMode == "secret-service" && !Path.IsPathFullyQualified(SecretToolPath)) throw new InvalidOperationException("secretToolPath must be an absolute path to the trusted secret-tool executable.");
        if (Issuer.Length != 0 && (!Issuer.StartsWith("moyai:", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(KeyNamespace)))
            throw new InvalidOperationException("Configured authentication requires issuer and key namespace.");
        if (ProtectorMode == "broker" && (!Uri.TryCreate(BrokerEndpoint, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(BrokerCertificateThumbprint)))
            throw new InvalidOperationException("Broker requires HTTPS and a client certificate thumbprint.");
    }
}
