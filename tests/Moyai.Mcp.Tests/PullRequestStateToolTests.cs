using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moyai.Application.Providers;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class PullRequestStateToolTests
{
    [Theory]
    [InlineData(nameof(MoyaiTools.PullRequestGet), "pull_request_get", false)]
    [InlineData(nameof(MoyaiTools.PullRequestMerge), "pull_request_merge", true)]
    public void ActualToolsExposeReadAndDestructiveAnnotations(string methodName, string toolName, bool destructive)
    {
        MethodInfo method = typeof(MoyaiTools).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal(toolName, attribute.Name);
        Assert.Equal(destructive, attribute.Destructive);
        Assert.Equal(!destructive, attribute.ReadOnly);
        Assert.True(attribute.OpenWorld);
    }

    [Fact]
    public void MergeCliUsesActualSchemaAndRequiresExplicitShaAndStrategy()
    {
        MethodInfo method = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.PullRequestMerge))!;
        McpServerTool tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("Schema-only test."));
        JsonElement schema = tool.ProtocolTool.InputSchema;
        string sha = new('a', 40);
        var values = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--pull-request-id", "42",
            "--strategy", "merge", "--expected-source-commit", sha, "--message", " 日本語 "]), schema, "pull_request_merge");
        Assert.Equal(42L, values["pullRequestId"]);
        Assert.Equal("merge", values["strategy"]);
        Assert.Equal(sha, values["expectedSourceCommit"]);
        Assert.Equal(" 日本語 ", values["message"]);
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--pull-request-id", "42"]), schema, "pull_request_merge"));
    }

    [Fact]
    public void ReadCliKeepsInt64ForProviderBoundValidation()
    {
        McpServerTool tool = McpServerTool.Create(typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.PullRequestGet))!, _ => throw new InvalidOperationException("Schema-only test."));
        var values = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--pull-request-id", "2147483648"]), tool.ProtocolTool.InputSchema, "pull_request_get");
        Assert.Equal(2147483648L, values["pullRequestId"]);
    }

    [Fact]
    public void UnavailableDraftRemainsExplicitNullInMcpJson()
    {
        var snapshot = new PullRequestStateInfo(42, null, "open", "feature", "main", new string('a', 40), null, null);
        var result = new PullRequestStateResult(true, "pull_request_get", "Display", "githubie", snapshot, null);
        JsonElement json = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("pullRequest").GetProperty("draft").ValueKind);
    }
}
