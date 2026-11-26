using System.Text.Json.Nodes;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class GithubieOwnerContractTests
{
    private const string Version = "{\"version\":\"2.1.0.0\"}";
    private const string Capabilities = """
        {"data":{"repository_commit":true,"authentication":{"integration_mode":"windows_owner","direct_connection":"unrestricted",
          "owner_authentication":{"version":1,"mode":"windows_owner","transport":"user_host_schema1","principal":"windows_owner",
          "assertion_required":false,"project_binding_required":false,"legacy_token_required":false,
          "credential_header":"X-Githubie-Session","generation_header":"X-Githubie-Generation",
          "operations":["github_repository_status","github_repository_commit"]}}}}
        """;
    private static readonly string[] Tools = ["github_repository_status", "github_repository_commit"];

    [Fact]
    public void RequiresOwnerListToolListAndDefinedOperationFlag()
    {
        GithubieOwnerContract contract = GithubieOwnerContract.Parse(Version, Capabilities, Tools);
        contract.Require("github_repository_status");
        contract.Require("github_repository_commit");
        Assert.Throws<ProviderAuthenticationException>(() => contract.Require("github_push"));
        GithubieOwnerContract unavailable = GithubieOwnerContract.Parse(Version, Capabilities, ["github_repository_status"]);
        Assert.Throws<ProviderAuthenticationException>(() => unavailable.Require("github_repository_commit"));
        JsonNode root = JsonNode.Parse(Capabilities)!;
        root["data"]!["repository_commit"] = false;
        Assert.Throws<ProviderAuthenticationException>(() => GithubieOwnerContract.Parse(Version, root.ToJsonString(), Tools).Require("github_repository_commit"));
    }

    [Theory]
    [InlineData("mode", "legacy")]
    [InlineData("transport", "static")]
    [InlineData("principal", "other")]
    [InlineData("assertion_required", true)]
    [InlineData("project_binding_required", true)]
    [InlineData("legacy_token_required", true)]
    [InlineData("generation_header", "X-Buckettie-Session")]
    public void RejectsIncompatibleAuthenticationContract(string field, object value)
    {
        JsonNode root = JsonNode.Parse(Capabilities)!;
        root["data"]!["authentication"]!["owner_authentication"]![field] = System.Text.Json.JsonSerializer.SerializeToNode(value);
        Assert.Throws<ProviderAuthenticationException>(() => GithubieOwnerContract.Parse(Version, root.ToJsonString(), Tools));
    }

    [Fact]
    public void RejectsOldVersionDuplicatePropertiesAndDuplicateOperations()
    {
        Assert.Throws<ProviderAuthenticationException>(() => GithubieOwnerContract.Parse("{\"version\":\"2.0.9.9\"}", Capabilities, Tools));
        Assert.Throws<ProviderAuthenticationException>(() => GithubieOwnerContract.Parse(Version, Capabilities.Replace("\"version\":1", "\"version\":1,\"version\":1"), Tools));
        Assert.Throws<ProviderAuthenticationException>(() => GithubieOwnerContract.Parse(Version, Capabilities.Replace("\"github_repository_commit\"", "\"github_repository_status\""), Tools));
    }
}
