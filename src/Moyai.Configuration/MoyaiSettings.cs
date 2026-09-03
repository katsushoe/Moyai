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
public sealed record ProviderSettings(string Name, string Endpoint, string ToolPrefix, bool Repository = false);
