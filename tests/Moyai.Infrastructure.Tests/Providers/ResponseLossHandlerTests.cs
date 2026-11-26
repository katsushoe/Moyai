using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class ResponseLossHandlerTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:54321/mcp");

    [Fact]
    public async Task CompletedMutationResponseLossReturnsUnknownWithoutReplay()
    {
        using var wire = new FixtureWire();
        using var loss = new ResponseLossHandler("bitbucket_repository_commit", wire);
        using var client = new HttpClient(loss, false);
        var options = new McpRepositoryProviderOptions("buckettie", null, "bitbucket")
        {
            AuthenticationMode = "windows_owner", ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = Path.GetFullPath("isolated-runtime"),
            ConnectionResolver = (_, _) => new UserHostConnection(Endpoint, "fixture-credential", "fixture-generation")
        };
        var provider = new McpRepositoryProvider(options, new Factory(client));
        var request = new RepositoryProviderRequest("Fixture", "fixture", "https://bitbucket.org/example/fixture.git", null,
            RepositoryOperation.Commit, "fixture lost response", null, UseWindowsOwnerAuthentication: true);
        RepositoryProviderResult result = await provider.ExecuteAsync(request);
        Assert.False(result.Ok);
        Assert.True(result.ResultUnknown, result.ErrorCode);
        Assert.Equal(1, wire.CompletedMutations);
        Assert.Equal(1, loss.MutationDispatches);
        Assert.Equal(1, loss.ResponsesDiscarded);
        Assert.DoesNotContain("fixture-credential", result.ErrorMessage ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondMutationIsBlockedBeforeForwarding()
    {
        using var wire = new FixtureWire();
        using var loss = new ResponseLossHandler("bitbucket_repository_commit", wire);
        using var client = new HttpClient(loss);
        using var first = Call("bitbucket_repository_commit");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(first));
        using var second = Call("bitbucket_repository_commit");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(second));
        Assert.Equal(1, wire.CompletedMutations);
        Assert.Equal(1, loss.MutationDispatches);
    }

    [Fact]
    public async Task UnapprovedMutationIsNeverForwarded()
    {
        using var wire = new FixtureWire();
        using var loss = new ResponseLossHandler("bitbucket_repository_commit", wire);
        using var client = new HttpClient(loss);
        using var request = Call("bitbucket_push");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request));
        Assert.Equal(0, wire.CompletedMutations);
        Assert.Equal(0, loss.MutationDispatches);
    }

    private static HttpRequestMessage Call(string tool) => new(HttpMethod.Post, Endpoint)
    { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = new { } } }), Encoding.UTF8, "application/json") };

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixtureWire : HttpMessageHandler
    {
        public int CompletedMutations { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using JsonDocument json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = json.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)) return new(HttpStatusCode.Accepted);
            string? method = root.GetProperty("method").GetString();
            object result;
            if (method == "server/discover") result = new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" };
            else if (method == "initialize") result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "fixture", version = "2.0.1.0" } };
            else
            {
                string tool = root.GetProperty("params").GetProperty("name").GetString()!;
                object data;
                if (tool == "get_version") data = new { version = "2.0.1.0" };
                else if (tool == "bitbucket_provider_capabilities") data = new
                {
                    contract_version = 3, operations = new { repository_commit = true },
                    authentication = new { provider_id = "buckettie", authentication_mode = "windows_owner", owner_protocol_version = 1,
                        requires_assertion = false, project_binding_required = false, integration_mode = "moyai", direct_connection = "unrestricted",
                        replay_protection = false, required_audience = (string?)null, protocol_version = (int?)null, algorithm = (string?)null,
                        tool_scopes = new { }, supported_tools = new[] { "bitbucket_repository_commit" } }
                };
                else { CompletedMutations++; data = new { ok = true, data = new { commit_hash = new string('a', 40) } }; }
                result = new { isError = false, content = Array.Empty<object>(), structuredContent = data };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
    }
}
