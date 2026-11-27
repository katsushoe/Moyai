using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class McpRepositoryProviderTests
{
    private static readonly string[] SelectedStashPaths = ["src/a.cs", "docs/readme.md"];
    [Fact]
    public async Task ExecuteAsyncReturnsUnavailableWhenProviderCannotBeReached()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var provider = new McpRepositoryProvider(new McpRepositoryProviderOptions("githubbie", new Uri("http://127.0.0.1:43199/mcp"), "github"), new TestHttpClientFactory(httpClient));
        var request = new RepositoryProviderRequest("Moyai", "source", "https://github.com/example/moyai", null, RepositoryOperation.Status, null, null);

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.False(result.Ok);
        Assert.Equal("provider_unavailable", result.ErrorCode);
    }

    [Fact]
    public void ConstructorRejectsNonLoopbackEndpoint()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var options = new McpRepositoryProviderOptions("githubbie", new Uri("https://example.com/mcp"), "github");

        Assert.Throws<ArgumentException>(() => new McpRepositoryProvider(options, new TestHttpClientFactory(httpClient)));
    }

    [Theory]
    [InlineData("github", RepositoryOperation.BranchList, "github_branch_list")]
    [InlineData("github", RepositoryOperation.TagCreate, "github_tag_create")]
    [InlineData("bitbucket", RepositoryOperation.BranchList, "bitbucket_branch_list")]
    [InlineData("bitbucket", RepositoryOperation.TagCreate, "bitbucket_tag_create")]
    [InlineData("github", RepositoryOperation.ProviderVersion, "get_version")]
    [InlineData("github", RepositoryOperation.StashPush, "github_repository_stash_push")]
    [InlineData("github", RepositoryOperation.StashList, "github_repository_stash_list")]
    [InlineData("bitbucket", RepositoryOperation.StashApply, "bitbucket_repository_stash_apply")]
    [InlineData("bitbucket", RepositoryOperation.StashDrop, "bitbucket_repository_stash_drop")]
    [InlineData("bitbucket", RepositoryOperation.ProviderCapabilities, "bitbucket_provider_capabilities")]
    public void CommonContractMapsProviderTools(string prefix, RepositoryOperation operation, string expected)
    {
        Assert.Equal(expected, RepositoryProviderContract.ToolName(prefix, operation));
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"protected_branch\"}}", "provider_policy_rejected")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"repository_not_allowed\",\"retryable\":false}}", "provider_policy_rejected")]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"temporarily_unavailable\",\"retryable\":true}}", "provider_retryable_failure")]
    [InlineData("{\"error\":{\"code\":\"rate_limited\"}}", "provider_retryable_failure")]
    [InlineData("tag already exists", "provider_conflict")]
    [InlineData("{\"error\":{\"code\":\"release_already_exists\"}}", "provider_conflict")]
    [InlineData("branch not found", "provider_not_found")]
    [InlineData("Unauthorized token", "provider_authentication_failed")]
    [InlineData("unknown failure", "provider_operation_failed")]
    public void CommonContractNormalizesErrors(string detail, string expected)
    {
        Assert.Equal(expected, RepositoryProviderContract.NormalizeErrorCode(detail));
    }

    [Theory]
    [InlineData("selected_commit_patch_invalid")]
    [InlineData("expected_head_mismatch")]
    [InlineData("staged_changes_present")]
    public void CommonContractPreservesSelectedCommitRejectionErrors(string errorCode)
    {
        Assert.Equal(errorCode, RepositoryProviderContract.NormalizeErrorCode($"{{\"error\":{{\"code\":\"{errorCode}\"}}}}"));
    }

    [Theory]
    [InlineData("provider_remote_not_found")]
    [InlineData("provider_remote_ambiguous")]
    [InlineData("provider_remote_mismatch")]
    public void CommonContractPreservesRemoteResolutionErrors(string errorCode)
    {
        Assert.Equal(errorCode, RepositoryProviderContract.NormalizeErrorCode($"{{\"error\":{{\"code\":\"{errorCode}\"}}}}"));
    }

    [Fact]
    public void CommonContractMapsBranchAndTagArguments()
    {
        var branch = new RepositoryProviderRequest("Moyai", "source", "url", "origin", RepositoryOperation.BranchCreate, null, "token", Branch: "feature/test", BranchSource: "main");
        var tag = branch with { Operation = RepositoryOperation.TagPush, Branch = null, Tag = "v1.2.3" };
        var createTag = tag with { Operation = RepositoryOperation.TagCreate };

        Assert.Equal("feature/test", RepositoryProviderContract.Arguments("github", branch)["branch"]);
        Assert.Equal("main", RepositoryProviderContract.Arguments("github", branch)["source"]);
        Assert.Equal("v1.2.3", RepositoryProviderContract.Arguments("github", tag)["tag"]);
        Assert.Equal("main", RepositoryProviderContract.Arguments("github", createTag)["source"]);
        Assert.Null(RepositoryProviderContract.Arguments("github", createTag)["message"]);
        Assert.Equal("main", RepositoryProviderContract.Arguments("bitbucket", createTag)["source"]);
    }

    [Fact]
    public void CommonContractMapsSelectedStashPathsAndFullStashId()
    {
        var push = new RepositoryProviderRequest("Moyai", "source", "url", "origin", RepositoryOperation.StashPush,
            "before commit", null, Paths: SelectedStashPaths);
        var apply = push with { Operation = RepositoryOperation.StashApply, StashId = new string('a', 40) };

        var pushArguments = RepositoryProviderContract.Arguments("github", push);
        Assert.Equal("before commit", pushArguments["message"]);
        Assert.Equal(SelectedStashPaths, Assert.IsType<string[]>(pushArguments["paths"]));
        Assert.Equal(new string('a', 40), RepositoryProviderContract.Arguments("github", apply)["stash_id"]);
        Assert.Equal(new string('a', 40), RepositoryProviderContract.Arguments("bitbucket", apply)["stashId"]);
    }

    [Fact]
    public void CommonContractMapsSelectedCommitPatchAndExpectedHead()
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "origin", RepositoryOperation.CommitSelected,
            "split changes", null, ExpectedHead: new string('a', 40), Patch: "diff --git a/a b/a");
        IReadOnlyDictionary<string, object?> github = RepositoryProviderContract.Arguments("github", request);
        IReadOnlyDictionary<string, object?> bitbucket = RepositoryProviderContract.Arguments("bitbucket", request);
        Assert.Equal(request.ExpectedHead, github["expected_head"]);
        Assert.Equal(request.Patch, github["patch"]);
        Assert.Equal(request.Message, bitbucket["message"]);
        Assert.Equal(request.ExpectedHead, bitbucket["expected_head"]);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"data\":{\"operations\":{\"repository_commit_selected\":true}}}", true)]
    [InlineData("{\"ok\":true,\"data\":{\"operations\":{\"repository_commit_selected\":false}}}", false)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_commit_selected\":true}}", true)]
    [InlineData("{\"ok\":true,\"data\":{}}", false)]
    public void SelectedCommitRequiresExplicitProviderSupport(string output, bool expected) =>
        Assert.Equal(expected, RepositoryProviderContract.SupportsSelectedCommit(output));

    [Theory]
    [InlineData(RepositoryOperation.StashList, "repository.read")]
    [InlineData(RepositoryOperation.StashPush, "repository.stash.write")]
    [InlineData(RepositoryOperation.StashApply, "repository.stash.write")]
    [InlineData(RepositoryOperation.StashDrop, "repository.stash.write")]
    public void StashOperationsUseReadOrDedicatedMutationScopes(RepositoryOperation operation, string expectedScope)
    {
        Assert.Equal(expectedScope, RepositoryAssertionPolicy.Scope(operation));
    }

    [Theory]
    [InlineData("{\"ok\":true,\"data\":{\"operations\":{\"repository_stash_push\":true}}}", RepositoryOperation.StashPush, true)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_stash_apply\":true}}", RepositoryOperation.StashApply, true)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_stash_drop\":false}}", RepositoryOperation.StashDrop, false)]
    [InlineData("{\"ok\":true,\"data\":{}}", RepositoryOperation.StashList, false)]
    public void StashCapabilityNegotiationRequiresExplicitProviderSupport(string output, RepositoryOperation operation, bool expected)
    {
        Assert.Equal(expected, RepositoryProviderContract.SupportsStashOperation(output, operation));
    }

    [Fact]
    public void CommonContractMapsConfiguredRemoteForStatus()
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "github-origin-https", RepositoryOperation.Status, null, null);

        IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments("github", request, supportsRemoteResolution: true);

        Assert.Equal("github-origin-https", arguments["remote"]);
    }

    [Fact]
    public void CommonContractOmitsRemoteWhenNotConfigured()
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "", RepositoryOperation.Status, null, null);

        IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments("github", request);

        Assert.DoesNotContain("remote", arguments.Keys);
    }

    [Fact]
    public void CommonContractDoesNotMapRemoteToOperationsWithoutRemoteParameter()
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "github-origin-https", RepositoryOperation.BranchList, null, null);

        IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments("github", request);

        Assert.DoesNotContain("remote", arguments.Keys);
    }

    [Fact]
    public void CommonContractOmitsRemoteWhenProviderDoesNotAdvertiseSupport()
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "github-origin-https", RepositoryOperation.Status, null, null);

        IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments("github", request);

        Assert.DoesNotContain("remote", arguments.Keys);
    }

    [Theory]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":1,\"mode\":\"repository_url\"}}}", true)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":2,\"mode\":\"repository_url\"}}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":\"1\",\"mode\":\"repository_url\"}}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":null,\"mode\":\"repository_url\"}}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":true,\"mode\":\"repository_url\"}}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":{},\"mode\":\"repository_url\"}}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":null}}", false)]
    [InlineData("{\"data\":{\"remote_resolution\":{\"version\":1,\"mode\":\"other\"}}}", false)]
    [InlineData("{\"data\":{}}", false)]
    [InlineData("invalid", false)]
    public void CommonContractReadsRemoteResolutionCapability(string output, bool expected)
    {
        Assert.Equal(expected, RepositoryProviderContract.SupportsRemoteResolution(output));
    }

    [Theory]
    [InlineData("bitbucket", RepositoryOperation.Commit, true)]
    [InlineData("BITBUCKET", RepositoryOperation.Commit, true)]
    [InlineData("github", RepositoryOperation.Commit, false)]
    [InlineData("other", RepositoryOperation.Commit, false)]
    [InlineData("bitbucket", RepositoryOperation.Status, true)]
    [InlineData("github", RepositoryOperation.Status, true)]
    [InlineData("bitbucket", RepositoryOperation.Push, false)]
    [InlineData("bitbucket", RepositoryOperation.Pull, false)]
    [InlineData("bitbucket", RepositoryOperation.Diff, true)]
    [InlineData("BITBUCKET", RepositoryOperation.Diff, true)]
    [InlineData("github", RepositoryOperation.Diff, false)]
    [InlineData("other", RepositoryOperation.Diff, false)]
    [InlineData("bitbucket", RepositoryOperation.BranchList, false)]
    public void CommonContractLimitsRemoteToSupportedOperations(string prefix, RepositoryOperation operation, bool expected)
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", "non-origin", operation, "message", null);
        IReadOnlyDictionary<string, object?> arguments = RepositoryProviderContract.Arguments(prefix, request, supportsRemoteResolution: true);

        Assert.Equal(expected, RepositoryProviderContract.SupportsRemoteArgument(prefix, operation));
        Assert.Equal(expected, arguments.ContainsKey("remote"));
        Assert.Equal("Moyai", arguments["repository"]);
        if (expected) Assert.Equal("non-origin", arguments["remote"]);
        if (operation == RepositoryOperation.Commit) Assert.Equal("message", arguments["message"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("non-origin", false)]
    public void CommonContractOmitsCommitRemoteWhenMissingOrUnsupported(string? remote, bool supported)
    {
        var request = new RepositoryProviderRequest("Moyai", "source", "url", remote, RepositoryOperation.Commit, "message", null);
        Assert.DoesNotContain("remote", RepositoryProviderContract.Arguments("bitbucket", request, supported).Keys);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Theory]
    [InlineData("github", null, "Display_Project")]
    [InlineData("bitbucket", null, "Display_Project")]
    [InlineData("github", "provider-id", "provider-id")]
    [InlineData("bitbucket", "neonsdk", "neonsdk")]
    public void RepositoryArgumentUsesExplicitMappingOrLegacyProjectName(string prefix, string? mapping, string expected)
    {
        var request = new RepositoryProviderRequest("Display_Project", "source", "url", null, RepositoryOperation.Status, null, null, ProviderRepositoryId: mapping);
        Assert.Equal(expected, RepositoryProviderContract.Arguments(prefix, request)["repository"]);
        Assert.Equal("Display_Project", request.Project);
        Assert.Empty(RepositoryProviderContract.Arguments(prefix, request with { Operation = RepositoryOperation.ProviderVersion }));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Provider unavailable.");
    }
}
