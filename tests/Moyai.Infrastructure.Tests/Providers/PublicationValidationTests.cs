using System.Text.Json;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class PublicationValidationTests
{
    private static readonly string Sha = new('a', 40);
    private const string Id = "55555555-5555-4555-8555-555555555555";

    [Theory]
    [InlineData("operation_not_found", "not_executed", false)]
    [InlineData("publication_status_failed", "unknown", true)]
    public void StatusUnavailableRecordPreservesTypedErrorWithoutInventingSource(string code, string outcome, bool reconcile)
    {
        string json=JsonSerializer.Serialize(new {ok=false,operation="pull_request_publish_status",
            data=new {operation_id=Id,source_branch="",destination_branch="",source_sha="",
                publication_state="not_attempted",pull_request_state="not_attempted",stage="validation",requires_reconciliation=false},
            error=new {code,outcome}});
        var result=PullRequestPublicationResult.Parse("Display","githubie",RepositoryOperation.PullRequestPublishStatus,
            new(Id,null,null,null),new(false,"pull_request_publish_status",json,code,json,outcome=="unknown"));
        Assert.Equal(code,result.Error!.Code);
        Assert.Equal(reconcile,result.RequiresReconciliation);
        Assert.False(result.ProviderDataAvailable);
        Assert.Null(result.Data);
        Assert.Null(result.RequestContext.SourceBranch);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("refs/heads/feature")]
    [InlineData("-feature")]
    [InlineData("feature..bad")]
    [InlineData("feature@{1}")]
    [InlineData("feature\tbad")]
    [InlineData("feature/.hidden")]
    [InlineData("feature.lock")]
    public void InputRevisionOrInvalidBranchRejects(string branch) =>
        Assert.Throws<ArgumentException>(() => new PullRequestPublicationInput(branch, "main", Sha, "title", Id, false).Validate());

    [Theory]
    [InlineData("55555555555545558555555555555555")]
    [InlineData("not-a-uuid")]
    [InlineData("")]
    public void InputOperationIdMustBeExplicitDFormat(string id) =>
        Assert.Throws<ArgumentException>(() => new PullRequestPublicationInput("feature", "main", Sha, "title", id, false).Validate());

    [Fact]
    public void InputDocumentedTextBoundsAcceptsMaximumAndRefusesOverflow()
    {
        new PullRequestPublicationInput("Feature/Exact", "main", Sha, new string('x',256), Id, false, new string('x',65536)).Validate();
        Assert.Throws<ArgumentException>(() => new PullRequestPublicationInput("feature", "main", Sha, new string('x',257), Id, false).Validate());
        Assert.Throws<ArgumentException>(() => new PullRequestPublicationInput("feature", "main", Sha, "title", Id, false, new string('x',65537)).Validate());
        Assert.Throws<ArgumentException>(() => new PullRequestPublicationInput("main", "main", Sha, "title", Id, false).Validate());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResultAbsentVerifiedPayloadDoesNotInventSideEffects(bool unknown)
    {
        var context = new PublicationRequestContext(Id, "feature", "main", Sha);
        var result = PullRequestPublicationResult.Parse("Display", "githubie", RepositoryOperation.PullRequestPublishCreate, context,
            new(false, "pull_request_publish_create", null, "provider_unavailable", "transport failed", unknown));
        Assert.Equal(unknown, result.RequiresReconciliation);
        Assert.False(result.ProviderDataAvailable);
        Assert.Null(result.Data);
        Assert.False(result.Error!.Retryable);
        Assert.Equal(Id, result.RequestContext.OperationId);
        JsonElement json = JsonSerializer.SerializeToElement(result);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("data").ValueKind);
        Assert.Equal("feature", json.GetProperty("request_context").GetProperty("source_branch").GetString());
    }

    [Theory]
    [InlineData("new_state", "not_attempted")]
    [InlineData("created", "unknown")]
    public void ResultInvalidOrInconsistentPartialPayloadRequiresStatus(string publication, string pr)
    {
        string json = JsonSerializer.Serialize(new { ok=false, operation="pull_request_publish_create",
            data=new { operation_id=Id,source_branch="feature",destination_branch="main",source_sha=Sha,
                publication_state=publication,pull_request_state=pr,requires_reconciliation=false,stage="pr" },
            error=new {code="publication_failed",outcome="failed"} });
        var parsed = PullRequestPublicationResult.Parse("Display","githubie",RepositoryOperation.PullRequestPublishCreate,
            new(Id,"feature","main",Sha),new(false,"pull_request_publish_create",json,"publication_failed",json));
        Assert.True(parsed.RequiresReconciliation);
        Assert.False(parsed.ProviderDataAvailable);
        Assert.Equal("unknown",parsed.Error!.Outcome);
    }
}

