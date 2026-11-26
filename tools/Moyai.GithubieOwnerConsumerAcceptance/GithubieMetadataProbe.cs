using System.Text.Json;
using ModelContextProtocol.Client;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.GithubieOwnerConsumerAcceptance;

/// <summary>業務Toolを呼ばず、検証済みowner接続でメタデータだけを確認します。</summary>
internal static class GithubieMetadataProbe
{
    internal static async Task<int> RunAsync(McpRepositoryProviderOptions options, IHttpClientFactory factory, CancellationToken cancellationToken)
    {
        using var connection = new ProviderConnection(options, factory);
        connection.BeginTool("__metadata_only__");
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        { Endpoint = connection.Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, connection.Client);
        await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        await client.PingAsync((ModelContextProtocol.RequestOptions?)null, cancellationToken).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!tools.Any(tool => tool.Name == "get_version")) throw new ProviderAuthenticationException("provider_capability_missing");
        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.ProviderVersion,
            await client.CallToolAsync("get_version", new Dictionary<string, object?>(), cancellationToken: cancellationToken).ConfigureAwait(false));
        if (!result.Ok || result.Output is null) throw new ProviderAuthenticationException(result.ErrorCode ?? "provider_capability_missing");
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Output);
            BuckettieUserHostResolver.ValidateProperties(document.RootElement);
            JsonElement root = document.RootElement;
            JsonElement data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement value) ? value : root;
            string? text = data.ValueKind == JsonValueKind.String ? data.GetString() : data.GetProperty("version").GetString();
            if (!Version.TryParse(text, out Version? version) || version.Revision < 0 || version < new Version(2, 1, 0, 0))
                throw new ProviderAuthenticationException("provider_capability_missing");
            return tools.Count;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or UserHostConnectionException)
        { throw new ProviderAuthenticationException("provider_capability_missing"); }
    }
}
