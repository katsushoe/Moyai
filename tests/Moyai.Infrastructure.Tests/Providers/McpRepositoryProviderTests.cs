using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class McpRepositoryProviderTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsUnavailableWhenProviderCannotBeReached()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://127.0.0.1:43199/mcp"), "github"), new TestHttpClientFactory(httpClient));
        var request = new RepositoryProviderRequest("Moyai", "source", "https://github.com/example/moyai", "origin", RepositoryOperation.Status, null, null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.False(result.Ok);
        Assert.Equal("provider_auth_unavailable", result.ErrorCode);
    }

    [Fact]
    public void ConstructorRejectsNonLoopbackEndpoint()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var options = new McpRepositoryProviderOptions("githubbie", new Uri("https://example.com/mcp"), "github");

        Assert.Throws<ArgumentException>(() => new McpRepositoryProvider(options, new TestHttpClientFactory(httpClient)));
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Provider unavailable.");
    }
}
