using System.Net;
using System.Text;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class BrokerKeyEncryptionProviderTests
{
    private static readonly byte[] Dek = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    [Fact]
    public async Task BrokerResponsesAreReadBeforeTheReceiveBufferIsCleared()
    {
        string dek = Convert.ToBase64String(Dek);
        var broker = Broker(operation => operation switch
        {
            "wrap" => $$"""{"wrapped_dek":"{{dek}}","key_version":"v2"}""",
            "unwrap" => $$"""{"dek":"{{dek}}"}""",
            "rotate" => """{"key_version":"v3"}""",
            "health" => """{"available":true}""",
            _ => throw new InvalidOperationException(operation),
        });

        WrappedDataKey wrapped = await broker.WrapAsync(Dek, Encoding.UTF8.GetBytes("context"));
        Assert.Equal(Dek, wrapped.WrappedDek);
        Assert.Equal("v2", wrapped.KeyVersion);
        Assert.Equal(Dek, await broker.UnwrapAsync(wrapped, Encoding.UTF8.GetBytes("context")));
        Assert.Equal("v3", await broker.RotateAsync());
        Assert.True(await broker.IsAvailableAsync());
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("""[]""")]
    [InlineData("""not-json""")]
    public async Task MalformedBrokerResponseIsReportedAsProviderUnavailable(string body)
    {
        var broker = Broker(_ => body);
        ProviderAuthenticationException exception = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => broker.RotateAsync());
        Assert.Equal("auth_key_provider_unavailable", exception.Code);
    }

    private static BrokerKeyEncryptionProvider Broker(Func<string, string> respond) =>
        new(new StubClientFactory(new StubHandler(respond)), "broker", new Uri("https://broker.example.test/"));

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class StubHandler(Func<string, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string operation = request.RequestUri!.Segments[^1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(operation), Encoding.UTF8, "application/json") });
        }
    }
}
