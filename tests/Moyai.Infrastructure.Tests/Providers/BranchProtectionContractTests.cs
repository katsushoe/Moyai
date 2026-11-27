using System.Text.Json;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class BranchProtectionContractTests
{
    internal const string Policy = """{"required_pull_request_reviews":{"required_approving_review_count":2},"required_status_checks":{"strict":true,"contexts":["build"]},"allow_force_pushes":false,"allow_deletions":false,"enforce_admins":true,"restrictions":{"token":"private-do-not-return"}}""";

    [Theory]
    [InlineData("main")]
    [InlineData("feature/fix")]
    [InlineData("日本語")]
    public void ExactBranchNamesAreAccepted(string branch) => BranchProtectionEdit.ValidateBranch(branch);

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("refs//main")]
    [InlineData("main.lock")]
    [InlineData("foo/.hidden")]
    [InlineData("@{upstream}")]
    [InlineData("foo..bar")]
    [InlineData(" main")]
    public void InvalidBranchNamesAreRejected(string branch) => Assert.Throws<ArgumentException>(() => BranchProtectionEdit.ValidateBranch(branch));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"required_approving_reviews\":7}")]
    [InlineData("{\"require_pull_request\":false,\"required_approving_reviews\":0}")]
    [InlineData("{\"require_status_checks\":true}")]
    [InlineData("{\"require_status_checks\":false,\"strict_status_checks\":false}")]
    [InlineData("{\"status_check_contexts\":[\"build\",\"build\"]}")]
    [InlineData("{\"status_check_contexts\":[\"\"]}")]
    public void ContradictoryOrEmptyEditIsRejected(string json)
    {
        BranchProtectionEdit edit = JsonSerializer.Deserialize<BranchProtectionEdit>(json)!;
        Assert.Throws<ArgumentException>(edit.Validate);
    }

    [Fact]
    public void UnknownEditFieldIsNotSilentlyIgnored() => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BranchProtectionEdit>("{\"allow_force_push\":true}"));

    [Fact]
    public void WireArgumentsPreserveExplicitFalseAndNullAndCreationFlag()
    {
        var edit = new BranchProtectionEdit(RequirePullRequest: true, RequiredApprovingReviews: 0, AllowForcePushes: false,
            RequireStatusChecks: true, StatusCheckContexts: []);
        edit.Validate();
        var request = new RepositoryProviderRequest("display", "source", "https://github.com/o/r", null,
            RepositoryOperation.BranchProtectionUpdate, null, null, Branch: "main", ProviderRepositoryId: "registered",
            ProtectionEdit: edit, CreateProtectionIfMissing: true);
        var wire = RepositoryProviderContract.Arguments("github", request);
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(wire));
        Assert.Equal("registered", json.RootElement.GetProperty("repository").GetString());
        Assert.True(json.RootElement.GetProperty("create_if_missing").GetBoolean());
        var value = json.RootElement.GetProperty("edit");
        Assert.False(value.GetProperty("allow_force_pushes").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("allow_deletions").ValueKind);
        Assert.Equal(0, value.GetProperty("required_approving_reviews").GetInt32());
        Assert.Throws<ArgumentException>(() => RepositoryProviderContract.Arguments("bitbucket", request));
    }

    [Theory]
    [InlineData("{\"ok\":true,\"data\":{\"branch_protection_read\":true}}", true)]
    [InlineData("{\"ok\":true,\"data\":{\"branch_protection_read\":\"true\"}}", false)]
    [InlineData("{\"ok\":true,\"data\":{\"repository_settings_read\":true}}", false)]
    [InlineData("{\"ok\":false,\"data\":{\"branch_protection_read\":true}}", false)]
    public void OnlyExplicitHostingCapabilityIsAccepted(string json, bool expected) =>
        Assert.Equal(expected, RepositoryProviderContract.SupportsBranchProtection(json, RepositoryOperation.BranchProtectionGet));

    [Theory]
    [InlineData(RepositoryOperation.BranchProtectionGet, "repository.read")]
    [InlineData(RepositoryOperation.BranchProtectionUpdate, "provider.githubie.branch_protection.write")]
    [InlineData(RepositoryOperation.BranchProtectionDelete, "provider.githubie.branch_protection.write")]
    public void DedicatedScopesAndToolNamesMatch(RepositoryOperation operation, string scope)
    {
        Assert.Equal(scope, RepositoryAssertionPolicy.Scope(operation, "githubie"));
        Assert.Equal("github_" + operation.ContractName(), RepositoryProviderContract.ToolName("github", operation));
    }

    [Fact]
    public void ReadProjectionOmitsProviderSecretsAndUnknownPolicyProperties()
    {
        var raw = new RepositoryProviderResult(true, "branch_protection_get", "{\"ok\":true,\"data\":{\"branch\":\"main\",\"can_update\":true,\"token\":\"private-do-not-return\",\"policy\":" + Policy + "}}", null, null);
        var result = BranchProtectionResult.Parse("display", "githubie", "main", RepositoryOperation.BranchProtectionGet, raw);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Protection!.Policy!.RequiredApprovingReviews);
        Assert.DoesNotContain("private-do-not-return", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"branch\":\"other\",\"changed\":true,\"policy\":null}")]
    [InlineData("{\"branch\":\"main\",\"policy\":null}")]
    public void MalformedMutationSuccessIsUnknown(string data)
    {
        var raw = new RepositoryProviderResult(true, "branch_protection_update", "{\"ok\":true,\"data\":" + data + "}", null, null);
        var result = BranchProtectionResult.Parse("p", "githubie", "main", RepositoryOperation.BranchProtectionUpdate, raw);
        Assert.False(result.Ok);
        Assert.Equal("unknown", result.Error!.Outcome);
    }

    [Fact]
    public void SuccessWithErrorIsNeverAccepted()
    {
        var raw = new RepositoryProviderResult(true, "branch_protection_delete", "{\"ok\":true,\"error\":{\"outcome\":\"unknown\"},\"data\":{\"branch\":\"main\",\"changed\":true,\"policy\":null}}", null, null);
        var result = BranchProtectionResult.Parse("p", "githubie", "main", RepositoryOperation.BranchProtectionDelete, raw);
        Assert.False(result.Ok);
        Assert.Equal("unknown", result.Error!.Outcome);
    }

    [Fact]
    public void UnsafePolicyReadCanReturnMetadataWithoutBody()
    {
        var raw = new RepositoryProviderResult(true, "branch_protection_get", "{\"ok\":true,\"data\":{\"branch\":\"main\",\"can_update\":false,\"policy\":null}}", null, null);
        var result = BranchProtectionResult.Parse("p", "githubie", "main", RepositoryOperation.BranchProtectionGet, raw);
        Assert.True(result.Ok);
        Assert.False(result.Protection!.CanUpdate);
        Assert.Null(result.Protection.Policy);
    }
}
