namespace Moyai.Mcp;

/// <summary>Validated host settings shared by interactive and Windows service execution.</summary>
public sealed record McpHostSettings(string DatabasePath, string ServerUrl)
{
    /// <summary>Resolves required host settings from environment and command-line configuration.</summary>
    public static McpHostSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? databasePath = configuration["MOYAI_DB_PATH"];
        string? serverUrl = configuration["MOYAI_MCP_URL"];
        if (string.IsNullOrWhiteSpace(databasePath)) throw new InvalidOperationException("MOYAI_DB_PATH is required.");
        if (string.IsNullOrWhiteSpace(serverUrl)) throw new InvalidOperationException("MOYAI_MCP_URL is required.");
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("MOYAI_MCP_URL must be an absolute HTTP(S) URL with a loopback host.");
        }
        return new McpHostSettings(databasePath, serverUrl);
    }
}
