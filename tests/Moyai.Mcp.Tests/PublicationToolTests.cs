using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class PublicationToolTests
{
    [Theory]
    [InlineData(nameof(MoyaiTools.PullRequestPublishCreate), "pull_request_publish_create", true)]
    [InlineData(nameof(MoyaiTools.PullRequestPublishStatus), "pull_request_publish_status", false)]
    public void ToolsExposeExplicitMutationAndReadSemantics(string methodName, string name, bool mutation)
    {
        var attribute=typeof(MoyaiTools).GetMethod(methodName)!.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal(name,attribute.Name);
        Assert.Equal(mutation,attribute.Destructive);
        Assert.Equal(!mutation,attribute.ReadOnly);
        Assert.True(attribute.OpenWorld);
    }

    [Fact]
    public void CliUsesActualMcpSchemaRequiresExplicitDraftAndUuid()
    {
        var method=typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.PullRequestPublishCreate))!;
        var tool=McpServerTool.Create(method,_=>throw new InvalidOperationException("schema only"));
        JsonElement schema=tool.ProtocolTool.InputSchema;
        string[] arguments=["--project","Display","--source-branch","Feature/Exact","--destination-branch","main",
            "--expected-source-sha",new string('a',40),"--title","日本語","--operation-id","55555555-5555-4555-8555-555555555555","--draft","false"];
        var values=CliArguments.Convert(CliArguments.Parse(arguments),schema,"pull_request_publish_create");
        Assert.Equal(false,values["draft"]);
        Assert.Equal("Feature/Exact",values["sourceBranch"]);
        Assert.Throws<ArgumentException>(()=>CliArguments.Convert(CliArguments.Parse(arguments[..^2]),schema,"pull_request_publish_create"));
        Assert.Throws<ArgumentException>(()=>CliArguments.Convert(CliArguments.Parse(arguments[..^1]),schema,"pull_request_publish_create"));
        Assert.Throws<ArgumentException>(()=>CliArguments.Convert(CliArguments.Parse(["--project","Display"]),schema,"pull_request_publish_create"));
    }

    [Fact]
    public void StatusHasOnlyProjectAndOriginalUuidBusinessInputs()
    {
        var tool=McpServerTool.Create(typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.PullRequestPublishStatus))!,
            _=>throw new InvalidOperationException("schema only"));
        var values=CliArguments.Convert(CliArguments.Parse(["--project","Display","--operation-id","55555555-5555-4555-8555-555555555555"]),
            tool.ProtocolTool.InputSchema,"pull_request_publish_status");
        Assert.Equal(2,values.Count);
    }
}

