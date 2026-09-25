using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Providers;
using Moyai.Infrastructure.Tests.Authentication;
using ProviderAuth = Moyai.ProviderAuthentication;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class KelpieLifecycleAdapterTests
{
    [Fact]
    public async Task ProtocolV2IssuerInteroperatesWithProviderPackage()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var context = new AssertionContext(
            "kelpiessh",
            Guid.NewGuid(),
            null,
            "deploy_prepare",
            ["deploy.prepare"],
            "deployment-interop:deploy_prepare",
            "2",
            "kelpie_target",
            "target-01");
        SignedAssertion assertion = await fixture.Issuer.IssueAsync(context);
        AssertionTrustKey trustKey = await fixture.Ring.GetActiveKeyAsync();
        string trustPath = Path.Combine(Path.GetTempPath(), $"moyai-kelpie-trust-{Guid.NewGuid():N}.json");
        string replayPath = Path.Combine(Path.GetTempPath(), $"moyai-kelpie-replay-{Guid.NewGuid():N}.db");
        try
        {
            await File.WriteAllTextAsync(
                trustPath,
                JsonSerializer.Serialize(new[] { trustKey }, Moyai.Infrastructure.Authentication.AssertionJson.Options));
            var replay = new ProviderAuth.SqliteAssertionReplayCache(
                new ProviderAuth.SqliteAssertionReplayCacheOptions(replayPath),
                fixture.Clock);
            await replay.InitializeAsync();
            var expected = ProviderAuth.AssertionContext.ForResource(
                "kelpiessh",
                context.Project,
                "kelpie_target",
                "target-01",
                "deploy_prepare",
                ["deploy.prepare"],
                context.OperationId);
            var capability = new ProviderAuth.AssertionCapability(
                "kelpiessh",
                "kelpiessh",
                "2",
                "ES256",
                true,
                new Dictionary<string, string[]> { ["deploy_prepare"] = ["deploy.prepare"] },
                ["kelpie_target"]);
            var validator = new ProviderAuth.Es256AssertionValidator(
                new ProviderAuth.FileAssertionTrustStore(trustPath),
                replay,
                new ProviderAuth.AssertionOptions(fixture.Options.Issuer),
                capability,
                fixture.Clock);

            ProviderAuth.AssertionPrincipal principal = await validator.ValidateAsync(assertion.Value, expected);

            Assert.Equal(expected, principal.Context);
        }
        finally
        {
            File.Delete(trustPath);
            File.Delete(replayPath);
        }
    }

    [Fact]
    public async Task DeployUsesProtocolV2AssertionForEveryStage()
    {
        using var handler = new KelpieHandler();
        var issuer = new RecordingIssuer();
        var audit = new RecordingAudit();
        var provider = Provider(handler, issuer, audit);
        Guid projectId = Guid.NewGuid();
        Guid deploymentId = Guid.NewGuid();
        var request = DeployRequest(projectId, deploymentId);

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal(
            ["target_status", "deploy_prepare", "deploy_upload", "deploy_activate", "deploy_verify", "deploy_cleanup"],
            handler.Calls.Select(static call => call.Tool));
        Assert.Equal(6, issuer.Contexts.Count);
        Assert.Equal(6, audit.Results.Count(static resultCode => resultCode == "issued"));
        Assert.Equal(6, audit.Results.Count(static resultCode => resultCode == "accepted"));
        Assert.All(issuer.Contexts, context =>
        {
            Assert.Equal("2", context.ProtocolVersion);
            Assert.Equal("kelpiessh", context.Provider);
            Assert.Equal(projectId, context.Project);
            Assert.Null(context.Repository);
            Assert.Equal("kelpie_target", context.ResourceKind);
            Assert.Equal("target-01", context.Resource);
            Assert.Single(context.Scopes);
        });
        Assert.All(handler.Calls, static call => Assert.StartsWith("assertion-", call.Bearer, StringComparison.Ordinal));
        Assert.All(handler.Initializations, static authorization => Assert.Null(authorization));
        JsonElement prepare = handler.Calls.Single(static call => call.Tool == "deploy_prepare").Arguments;
        Assert.Equal(deploymentId.ToString("D"), prepare.GetProperty("deploymentId").GetString());
        Assert.Equal("target-01", prepare.GetProperty("targetName").GetString());
        Assert.Equal("target-01", prepare.GetProperty("targetId").GetString());
        Assert.Equal("/srv/app/package.zip", prepare.GetProperty("destination").GetString());
    }

    [Fact]
    public async Task KnownStageFailureStopsWithoutRetryingMutation()
    {
        using var handler = new KelpieHandler { FailTool = "deploy_upload" };
        var issuer = new RecordingIssuer();
        var provider = Provider(handler, issuer, new RecordingAudit());

        LifecycleResult result = await provider.ExecuteAsync(DeployRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal("artifact_hash_mismatch", result.ErrorCode);
        Assert.Equal(["target_status", "deploy_prepare", "deploy_upload"], handler.Calls.Select(static call => call.Tool));
        Assert.Equal(1, handler.Calls.Count(static call => call.Tool == "deploy_upload"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownMutationOutcomeQueriesStatusWithoutRepeatingMutation(bool timeout)
    {
        using var handler = new KelpieHandler { ThrowOnceTool = "deploy_activate", ThrowTimeout = timeout };
        var issuer = new RecordingIssuer();
        var provider = Provider(handler, issuer, new RecordingAudit());

        LifecycleResult result = await provider.ExecuteAsync(DeployRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.Ok);
        Assert.Equal(1, handler.Calls.Count(static call => call.Tool == "deploy_activate"));
        Assert.Equal(1, handler.Calls.Count(static call => call.Tool == "deploy_status"));
        Assert.Contains("deploy_verify", handler.Calls.Select(static call => call.Tool));
        Assert.Contains("deploy_cleanup", handler.Calls.Select(static call => call.Tool));
    }

    [Fact]
    public async Task ExpiredAssertionRetriesToolOnlyOnceWithNewAssertion()
    {
        using var handler = new KelpieHandler { ExpireOnceTool = "deploy_prepare" };
        var issuer = new RecordingIssuer();
        var provider = Provider(handler, issuer, new RecordingAudit());

        LifecycleResult result = await provider.ExecuteAsync(DeployRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.Ok);
        KelpieCall[] prepareCalls = handler.Calls.Where(static call => call.Tool == "deploy_prepare").ToArray();
        Assert.Equal(2, prepareCalls.Length);
        Assert.NotEqual(prepareCalls[0].Bearer, prepareCalls[1].Bearer);
    }

    [Fact]
    public async Task RollbackUsesOriginalDeploymentIdThenCleanup()
    {
        using var handler = new KelpieHandler();
        var issuer = new RecordingIssuer();
        var provider = Provider(handler, issuer, new RecordingAudit());
        Guid deploymentId = Guid.NewGuid();
        var request = new LifecycleRequest(
            "Moyai",
            "source",
            null,
            LifecycleAction.DeployRollback,
            null,
            null,
            null,
            null,
            ProjectId: Guid.NewGuid(),
            DeploymentId: deploymentId,
            KelpieTarget: "target-01");

        LifecycleResult result = await provider.ExecuteAsync(request);

        Assert.True(result.Ok);
        Assert.Equal(["deploy_rollback", "deploy_cleanup"], handler.Calls.Select(static call => call.Tool));
        Assert.All(handler.Calls, call => Assert.Equal(
            deploymentId.ToString("D"),
            call.Arguments.GetProperty("deploymentId").GetString()));
    }

    private static McpLifecycleProvider Provider(
        KelpieHandler handler,
        IAssertionIssuer issuer,
        IAssertionAudit audit) => new(
            new McpRepositoryProviderOptions("server", new Uri("http://localhost/mcp"), "kelpie"),
            new ClientFactory(handler),
            issuer,
            audit);

    private static LifecycleRequest DeployRequest(Guid projectId, Guid deploymentId) => new(
        "Moyai",
        "source",
        null,
        LifecycleAction.Deploy,
        "1.0.0",
        Path.Combine(Path.GetTempPath(), "moyai-kelpie-package.zip"),
        null,
        null,
        ProjectId: projectId,
        DeploymentId: deploymentId,
        KelpieTarget: "target-01",
        DestinationPath: "/srv/app/package.zip",
        ArtifactSha256: new string('a', 64));

    private sealed class RecordingIssuer : IAssertionIssuer
    {
        public List<AssertionContext> Contexts { get; } = [];

        public Task<SignedAssertion> IssueAsync(
            AssertionContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return Task.FromResult(new SignedAssertion($"assertion-{Contexts.Count}", "test-key"));
        }
    }

    private sealed class RecordingAudit : IAssertionAudit
    {
        public List<string> Results { get; } = [];

        public Task WriteAsync(
            AssertionContext context,
            string keyId,
            string resultCode,
            CancellationToken cancellationToken = default)
        {
            Results.Add(resultCode);
            return Task.CompletedTask;
        }
    }

    private sealed class ClientFactory(KelpieHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class KelpieHandler : HttpMessageHandler
    {
        private static readonly string[] SupportedVersions = ["2026-07-28"];
        private bool _hasExpired;
        private bool _hasThrown;

        public string? ExpireOnceTool { get; init; }
        public string? FailTool { get; init; }
        public string? ThrowOnceTool { get; init; }
        public bool ThrowTimeout { get; init; }
        public List<KelpieCall> Calls { get; } = [];
        public List<AuthenticationHeaderValue?> Initializations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            string? method = root.GetProperty("method").GetString();
            if (method == "server/discover")
            {
                return Response(id, new
                {
                    supportedVersions = SupportedVersions,
                    capabilities = new { tools = new { } },
                    ttlMs = 0,
                    cacheScope = "private",
                });
            }

            if (method == "initialize")
            {
                Initializations.Add(request.Headers.Authorization);
                return Response(id, new
                {
                    protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(),
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "kelpiessh-test", version = "1.0" },
                });
            }

            string tool = root.GetProperty("params").GetProperty("name").GetString()
                ?? throw new InvalidOperationException("Tool name is required.");
            JsonElement arguments = root.GetProperty("params").GetProperty("arguments").Clone();
            Calls.Add(new KelpieCall(tool, arguments, request.Headers.Authorization?.Parameter));
            if (!_hasThrown && string.Equals(tool, ThrowOnceTool, StringComparison.Ordinal))
            {
                _hasThrown = true;
                if (ThrowTimeout)
                {
                    throw new TaskCanceledException("injected timeout");
                }

                throw new HttpRequestException("injected transport failure");
            }

            bool expired = !_hasExpired && string.Equals(tool, ExpireOnceTool, StringComparison.Ordinal);
            if (expired)
            {
                _hasExpired = true;
            }

            object payload = expired
                ? new { success = false, deployment = new { state = "prepared", error = new { code = "auth-assertion-expired", message = "expired", retryable = true } } }
                : Payload(tool, string.Equals(tool, FailTool, StringComparison.Ordinal));
            return Response(id, new
            {
                isError = false,
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload) } },
            });
        }

        private static object Payload(string tool, bool fail)
        {
            if (tool == "target_status")
            {
                return new { targetName = "target-01", targetId = "target-01", available = true, status = "configured" };
            }

            string state = tool switch
            {
                "deploy_prepare" => "prepared",
                "deploy_upload" => "uploaded",
                "deploy_activate" or "deploy_status" => "activated",
                "deploy_verify" => "verified",
                "deploy_rollback" => "rolled_back",
                "deploy_cleanup" => "cleaned",
                _ => "failed",
            };
            return fail
                ? new { success = false, deployment = new { state, error = new { code = "artifact-hash-mismatch", message = "hash mismatch", retryable = false } } }
                : new { success = true, deployment = new { state, error = (object?)null } };
        }

        private static HttpResponseMessage Response(JsonElement id, object result)
        {
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record KelpieCall(string Tool, JsonElement Arguments, string? Bearer);
}
