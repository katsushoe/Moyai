namespace Moyai.Infrastructure.Providers;

/// <summary>MCP Repository Providerの接続設定を表します。</summary>
public sealed record McpRepositoryProviderOptions(string Name, Uri? Endpoint, string ToolPrefix)
{
    public string AuthenticationMode { get; init; } = "assertion";
    public bool UsesWindowsOwnerAuthentication => AuthenticationMode == "windows_owner";
    public string ConnectionMode { get; init; } = "static";
    public string? RuntimeDirectory { get; init; }
    public string? RuntimeExecutable { get; init; }
    /// <summary>非推奨。互換性のため保持しますが、照合には使いません。</summary>
    public string? RuntimeSha256 { get; init; }
    public string ExpectedMcpPath { get; init; } = "/mcp";
    public string? ExpectedGeneration { get; init; }
    public bool UsesGithubieUserHost => ConnectionMode == "githubie_user_host_v1";
    public bool UsesUserHost => ConnectionMode == "buckettie_user_host_v1" || UsesGithubieUserHost;
    internal Func<string, string, UserHostConnection>? ConnectionResolver { get; init; }

    internal void Validate()
    {
        if (AuthenticationMode is not ("assertion" or "windows_owner")) throw new ArgumentException("Invalid Provider authentication mode.");
        if (UsesWindowsOwnerAuthentication && !UsesUserHost) throw new ArgumentException("Owner authentication requires a verified user host.");
        if (ConnectionMode == "static" && Endpoint is { IsLoopback: true } && RuntimeDirectory is null) return;
        if (UsesGithubieUserHost && Name == "githubie" && ToolPrefix == "github" && UsesWindowsOwnerAuthentication
            && Endpoint is null && RuntimeDirectory is null && ExpectedMcpPath == "/mcp"
            && !string.IsNullOrWhiteSpace(RuntimeExecutable) && Path.IsPathFullyQualified(RuntimeExecutable)) return;
        if (UsesUserHost && Name == "buckettie" && ToolPrefix == "bitbucket" && Endpoint is null
            && !string.IsNullOrWhiteSpace(RuntimeDirectory) && Path.IsPathFullyQualified(RuntimeDirectory))
        {
            BuckettieUserHostResolver.ValidateMcpPath(ExpectedMcpPath);
            return;
        }
        throw new ArgumentException("Invalid Provider connection configuration.");
    }
}
