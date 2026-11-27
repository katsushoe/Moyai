using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Moyai.Configuration;

/// <summary>Persistent configuration shared by the service and its CLI client.</summary>
public sealed record MoyaiSettings
{
    public string DatabasePath { get; init; } = Path.Combine(ProfileDirectory, "data", "moyai.db");
    public string ServerUrl { get; init; } = "http://127.0.0.1:43120";
    public string ApiToken { get; init; } = "";
    public List<ProviderSettings> Providers { get; init; } = [];
    public int RequestTimeoutSeconds { get; init; } = 60;
    public bool AutoUpdateOnStartup { get; init; } = true;
    public ProviderAuthenticationSettings ProviderAuthentication { get; init; } = new();

    public static string ProfileDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akatsukisoft", "Moyai");

    /// <summary>選択済みインストール先にある唯一の運用設定です。</summary>
    public static string DefaultPath
    {
        get
        {
            string? installationDirectory = null;
            if (OperatingSystem.IsWindows())
            {
                using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using RegistryKey? product = machine.OpenSubKey(@"Software\Akatsukisoft\Moyai");
                object? registered = product?.GetValue("InstallDirectory", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (registered is not null && registered is not string)
                    throw new InvalidOperationException("Registered Moyai installation directory must be an absolute path string.");
                installationDirectory = (string?)registered;
            }
            return OperationalConfigurationPath.Resolve(AppContext.BaseDirectory, installationDirectory);
        }
    }

    /// <summary>運用設定を正規化し、別ファイルへの切替を拒否します。</summary>
    public static string ResolveOperationalPath(string? selectedPath = null)
    {
        string canonical = DefaultPath;
        if (selectedPath is not null && !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(selectedPath), canonical))
            throw new ArgumentException($"Moyai operational configuration must be {canonical}. Other configuration files are not supported.", nameof(selectedPath));
        return canonical;
    }

    public static string DefaultLogDirectory => Path.Combine(ProfileDirectory, "logs");

    public static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads only the specified JSON file; never consults process environment.</summary>
    public static MoyaiSettings Load(string path)
        => Load(path, out _);

    /// <summary>設定を一度だけ読み、実際の読込内容に対応する状態を保持します。</summary>
    public static MoyaiSettings Load(string path, out LoadedConfigurationSnapshot snapshot)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(fullPath);
        try
        {
            using var buffer = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(buffer, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var settings = JsonSerializer.Deserialize<MoyaiSettings>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidOperationException("Configuration must be a JSON object.");
            settings.Validate();
            settings = settings with { DatabasePath = Path.GetFullPath(settings.DatabasePath, Path.GetDirectoryName(fullPath)!) };
            snapshot = new(fullPath, settings.DatabasePath, bytes, settings.ApiToken);
            return settings;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Validate()
    {
        ValidateApiToken(ApiToken);
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
            if (provider.AuthenticationMode is not ("assertion" or "windows_owner"))
                throw new InvalidOperationException("Unsupported Provider authenticationMode.");
            if (provider.AuthenticationMode == "windows_owner" && !((provider.Name == "buckettie" && provider.ToolPrefix == "bitbucket" && provider.ConnectionMode == "buckettie_user_host_v1")
                || (provider.Name == "githubie" && provider.ToolPrefix == "github" && provider.ConnectionMode == "githubie_user_host_v1")))
                throw new InvalidOperationException("windows_owner requires a verified Provider user-host transport.");
            if (provider.ConnectionMode == "static")
            {
                ValidateUrl(provider.Endpoint!);
                if (provider.RuntimeDirectory is not null)
                    throw new InvalidOperationException("Static providers cannot specify runtimeDirectory.");
            }
            else if (provider.ConnectionMode == "githubie_user_host_v1")
            {
                if (provider.Name != "githubie" || provider.ToolPrefix != "github" || provider.AuthenticationMode != "windows_owner"
                    || !string.IsNullOrEmpty(provider.Endpoint) || provider.RuntimeDirectory is not null || provider.ExpectedMcpPath != "/mcp"
                    || string.IsNullOrWhiteSpace(provider.RuntimeExecutable) || !Path.IsPathFullyQualified(provider.RuntimeExecutable))
                    throw new InvalidOperationException("Githubie user-host requires owner mode and an absolute runtime executable.");
            }
            else if (provider.ConnectionMode == "buckettie_user_host_v1")
            {
                if (provider.Name != "buckettie" || provider.ToolPrefix != "bitbucket"
                    || !string.IsNullOrEmpty(provider.Endpoint)
                    || string.IsNullOrWhiteSpace(provider.RuntimeDirectory)
                    || !Path.IsPathFullyQualified(provider.RuntimeDirectory))
                    throw new InvalidOperationException("Buckettie user-host requires an absolute runtimeDirectory and no endpoint.");
                string path = provider.ExpectedMcpPath;
                if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
                    || path.IndexOfAny(['?', '#', '\\']) >= 0
                    || !Uri.TryCreate("http://127.0.0.1" + path, UriKind.Absolute, out Uri? mcpUri)
                    || mcpUri.AbsolutePath != path)
                    throw new InvalidOperationException("expectedMcpPath must be a canonical absolute URI path.");
            }
            else throw new InvalidOperationException("Unsupported provider connectionMode.");
            if (!names.Add(provider.Name)) throw new InvalidOperationException("Duplicate provider name.");
        }
    }

    public static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Endpoint must be an absolute HTTP(S) loopback URL without credentials.");
    }

    public static void ValidateApiToken(string value)
    {
        if (value is null || value.Length != 64 || !value.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException("apiToken must contain 64 hexadecimal characters. Initialize a new user profile.");
    }
}

/// <summary>Provider endpoint, role and MCP tool prefix; contains no credentials.</summary>
public sealed record ProviderSettings(string Name, string? Endpoint, string ToolPrefix, bool Repository = false)
{
    public string AuthenticationMode { get; init; } = "assertion";
    public string ConnectionMode { get; init; } = "static";
    public string? RuntimeDirectory { get; init; }
    public string? RuntimeExecutable { get; init; }
    /// <summary>非推奨。互換性のため読み込みますが、照合には使いません。</summary>
    public string? RuntimeSha256 { get; init; }
    public string ExpectedMcpPath { get; init; } = "/mcp";
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
