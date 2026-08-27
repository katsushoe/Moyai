namespace Moyai.Infrastructure.Providers;

/// <summary>MCP Repository Providerの接続設定を表します。</summary>
public sealed record McpRepositoryProviderOptions(string Name, Uri Endpoint, string ToolPrefix);
