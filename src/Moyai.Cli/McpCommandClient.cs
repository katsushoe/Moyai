using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Configuration;

namespace Moyai.Cli;

/// <summary>設定ファイルの選択から独立して、MCP要求とCLI結果を処理します。</summary>
internal static class McpCommandClient
{
    internal static async Task<int> ExecuteAsync(MoyaiSettings settings, string command,
        Dictionary<string, string?> values, TextWriter output, TextWriter error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(settings.ServerUrl.TrimEnd('/') + "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http);
        await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        if (command == "commands")
        {
            if (values.Count != 0) throw new ArgumentException("Unknown commands option.");
            await output.WriteLineAsync(JsonSerializer.Serialize(tools.Select(tool => new { command = tool.Name.Replace('_', '-'), tool.Description, schema = tool.JsonSchema })));
            return 0;
        }
        string toolName = command == "version" ? "get_version" : command.Replace('-', '_');
        var selected = tools.SingleOrDefault(tool => tool.Name == toolName)
            ?? throw new ArgumentException($"Unknown service command '{command}'. Use commands to list supported commands.");
        IReadOnlyDictionary<string, object?> arguments = CliArguments.Convert(values, selected.JsonSchema, toolName);
        CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: timeout.Token);
        return CliResponse.Write(command, result, output, error);
    }
}
