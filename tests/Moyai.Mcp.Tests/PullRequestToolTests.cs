using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moyai.Application.Providers;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class PullRequestToolTests
{
    private static readonly string[] RequiredArguments = ["description", "destinationBranch", "draft", "project", "sourceBranch", "title"];

    [Fact]
    public void ActualSchemaAndCliRequireExplicitRouteAndDraft()
    {
        MethodInfo method = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.PullRequestCreate))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("pull_request_create", attribute.Name);
        Assert.True(attribute.Destructive);
        Assert.True(attribute.OpenWorld);
        McpServerTool tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("Schema-only test."));
        JsonElement schema = tool.ProtocolTool.InputSchema;
        Assert.Equal(RequiredArguments, schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).OrderBy(p => p).ToArray());
        var arguments = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--source-branch", "docs/proposal", "--destination-branch", "main", "--title", "日本語", "--description", "", "--draft", "false"]), schema, "pull_request_create");
        Assert.Equal("docs/proposal", arguments["sourceBranch"]);
        Assert.Equal("main", arguments["destinationBranch"]);
        Assert.Equal(false, arguments["draft"]);
        Assert.Equal(6, arguments.Count);
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--title", "Title"]), schema, "pull_request_create"));
    }

    [Fact]
    public void AbsentProviderDraftRemainsExplicitNullInActualMcpJson()
    {
        var info = new PullRequestInfo(1, null, "Title", null, "docs/proposal", "main", null, "open");
        JsonElement json = JsonSerializer.SerializeToElement(info, McpJsonUtilities.DefaultOptions);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("draft").ValueKind);
    }
}
