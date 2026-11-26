using System.Net;
using System.Text;
using System.Text.Json;
using Moyai.Infrastructure.Providers;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartCredentialProbeTests
{
    private const string OldSecret = "old-fixture-secret";
    private static UserHostConnection Old() => new(new Uri("http://127.0.0.1:43101/mcp"), OldSecret, "generation-a");
    private static UserHostConnection New() => new(new Uri("http://127.0.0.1:43102/mcp"), "new-fixture-secret", "generation-b");

    [Fact]
    public async Task OldCredentialIsSentOnceOnlyToNewEndpointAndResultContainsNoSecret()
    {
        using var handler = new Gate();
        using var client = new HttpClient(handler);
        using var oldConnection = Old();
        using var newConnection = New();
        var probe = new RestartCredentialProbe();
        RestartProbeResult result = await probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal(newConnection.Endpoint, handler.Endpoint);
        Assert.Equal(OldSecret, handler.Credential);
        Assert.Equal("initialize", handler.Method);
        Assert.DoesNotContain(OldSecret, result.ToString(), StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task LostResponseCannotBeRetried()
    {
        using var handler = new Gate { Disconnect = true };
        using var client = new HttpClient(handler);
        using var oldConnection = Old();
        using var newConnection = New();
        var probe = new RestartCredentialProbe();
        await Assert.ThrowsAsync<HttpRequestException>(() => probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(403, "user_host_credential_invalid", "not_executed")]
    [InlineData(401, "user_host_credential_required", "not_executed")]
    [InlineData(401, "user_host_credential_invalid", "unknown")]
    public async Task AmbiguousOrWrongRejectionIsNotSuccessAndCannotBeReplayed(int status, string code, string outcome)
    {
        using var handler = new Gate { Status = (HttpStatusCode)status, Code = code, Outcome = outcome };
        using var client = new HttpClient(handler);
        using var oldConnection = Old();
        using var newConnection = New();
        var probe = new RestartCredentialProbe();
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CheckAsync(oldConnection, newConnection, client, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task SameGenerationNeverSends()
    {
        using var handler = new Gate();
        using var client = new HttpClient(handler);
        using var oldConnection = Old();
        using var same = new UserHostConnection(new Uri("http://127.0.0.1:43102/mcp"), "new", "generation-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RestartCredentialProbe().CheckAsync(oldConnection, same, client, CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    private sealed class Gate : HttpMessageHandler
    {
        public int Count { get; private set; }
        public Uri? Endpoint { get; private set; }
        public string? Credential { get; private set; }
        public string? Method { get; private set; }
        public bool Disconnect { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.Unauthorized;
        public string Code { get; init; } = "user_host_credential_invalid";
        public string Outcome { get; init; } = "not_executed";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Endpoint = request.RequestUri;
            Credential = request.Headers.GetValues("X-Buckettie-Session").Single();
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(HttpMethod.Post, request.Method);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Method = body.RootElement.GetProperty("method").GetString();
            if (Disconnect) throw new HttpRequestException("fixture disconnect");
            return new HttpResponseMessage(Status) { Content = new StringContent($"{{\"error\":{{\"code\":\"{Code}\",\"outcome\":\"{Outcome}\"}}}}", Encoding.UTF8, "application/json") };
        }
    }
}
