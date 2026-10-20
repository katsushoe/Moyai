using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class PullRequestContractTests
{
    [Theory]
    [InlineData("github", "githubie", "provider.githubie.pull_request.write", "source_branch", "destination_branch")]
    [InlineData("bitbucket", "buckettie", "provider.buckettie.pr_create", "sourceBranch", "destinationBranch")]
    public void ArgumentsAndScopeAreProviderSpecific(string prefix, string provider, string scope, string sourceKey, string destinationKey)
    {
        var request = new RepositoryProviderRequest("Display", "unused", "https://example.invalid/repo", "non-origin", RepositoryOperation.PullRequestCreate, null, null,
            ProviderRepositoryId: "registered", PullRequest: new("docs/proposal", "main", "題名", "説明", false));
        var arguments = RepositoryProviderContract.Arguments(prefix, request);
        Assert.Equal(prefix + "_pr_create", RepositoryProviderContract.ToolName(prefix, request.Operation));
        Assert.Equal(scope, RepositoryAssertionPolicy.Scope(request.Operation, provider));
        Assert.Equal("registered", arguments["repository"]);
        Assert.Equal("docs/proposal", arguments[sourceKey]);
        Assert.Equal("main", arguments[destinationKey]);
        Assert.Equal("題名", arguments["title"]);
        Assert.Equal("説明", arguments["description"]);
        Assert.Equal(false, arguments["draft"]);
        Assert.Equal(6, arguments.Count);
    }

    [Theory]
    [InlineData("github", "{\"data\":{\"pr_create\":true}}", true)]
    [InlineData("github", "{\"data\":{\"pr_create\":false}}", false)]
    [InlineData("github", "{\"data\":{\"operations\":{\"pr_create\":true}}}", false)]
    [InlineData("bitbucket", "{\"data\":{\"operations\":{\"pr_create\":true}}}", true)]
    [InlineData("bitbucket", "{\"data\":{\"operations\":{\"pr_create\":\"true\"}}}", false)]
    [InlineData("bitbucket", "{\"data\":{\"pr_create\":true}}", false)]
    [InlineData("github", "{\"data\":{\"branch_create\":true}}", false)]
    [InlineData("github", null, false)]
    [InlineData("github", "invalid", false)]
    public void CapabilityIsExplicitAndDoesNotInferFromOtherOperations(string prefix, string? json, bool expected) =>
        Assert.Equal(expected, RepositoryProviderContract.SupportsPullRequestCreate(prefix, json));

    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("docs/proposal", null)]
    [InlineData("docs/proposal", " ")]
    [InlineData("HEAD", "main")]
    [InlineData("develop~1", "main")]
    [InlineData("develop", "main..other")]
    [InlineData("refs/heads/develop", "main")]
    public void ExplicitLiteralRouteIsRequired(string? source, string? destination) =>
        Assert.ThrowsAny<ArgumentException>(() => new PullRequestInput(source!, destination!, "Title", "", false).Validate());

    [Fact]
    public void TitleAndDescriptionValidationPreservesEmptyDescriptionAndUnicode()
    {
        new PullRequestInput("docs/test", "main", "日本語", "", true).Validate();
        Assert.Throws<ArgumentException>(() => new PullRequestInput("docs/test", "main", new string('x', 256), "", false).Validate());
        Assert.Throws<ArgumentException>(() => new PullRequestInput("docs/test", "main", "Title", new string('x', 32769), false).Validate());
        Assert.Throws<ArgumentException>(() => new PullRequestInput("docs/test", "main", "Title\n", "", false).Validate());
    }

    [Theory]
    [InlineData("githubie", "{\"ok\":true,\"data\":{\"number\":7,\"url\":\"https://example.invalid/pr/7\",\"title\":\"Title\",\"body\":null,\"source\":\"docs/proposal\",\"destination\":\"main\",\"state\":\"open\"}}", null)]
    [InlineData("buckettie", "{\"ok\":true,\"data\":{\"id\":7,\"url\":\"https://example.invalid/pr/7\",\"title\":\"Title\",\"description\":\"Body\",\"source_branch\":\"docs/proposal\",\"destination_branch\":\"main\",\"draft\":false,\"state\":\"OPEN\"}}", false)]
    public void SuccessIsNormalizedWithoutInventingDraft(string provider, string json, bool? draft)
    {
        PullRequestResult result = PullRequestResult.Parse("Display", provider, new(true, "pull_request_create", json, null, null));
        Assert.True(result.Ok);
        Assert.Equal(provider, result.Provider);
        Assert.Equal(7, result.PullRequest!.Id);
        Assert.Equal("docs/proposal", result.PullRequest.SourceBranch);
        Assert.Equal("main", result.PullRequest.DestinationBranch);
        Assert.Equal(draft, result.PullRequest.Draft);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"data\":{}}")]
    [InlineData("{\"ok\":true,\"data\":null}")]
    [InlineData("not json")]
    public void MalformedSuccessIsUnknownRatherThanSafeToRetry(string json)
    {
        PullRequestResult result = PullRequestResult.Parse("Display", "githubie", new(true, "pull_request_create", json, null, null));
        Assert.Equal("provider_invalid_response", result.Error!.Code);
        Assert.Equal("unknown", result.Error.Outcome);
    }

    [Theory]
    [InlineData("pull_request_route_not_allowed", "not_executed")]
    [InlineData("auth_scope_denied", "not_executed")]
    [InlineData("provider_specific_failure", "unknown")]
    public void ProviderRejectionIsPreservedWithoutPolicyBypass(string code, string outcome)
    {
        string json = JsonSerializer.Serialize(new { ok = false, error = new { code, outcome, correlation_id = "test-pr" } });
        using JsonDocument document = JsonDocument.Parse(json);
        var raw = RepositoryProviderResponse.Parse(RepositoryOperation.PullRequestCreate, new() { StructuredContent = document.RootElement.Clone() });
        var result = PullRequestResult.Parse("Display", "githubie", raw);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(outcome, result.Error.Outcome);
        Assert.Equal(json, result.Error.ProviderDetail);
    }
}
