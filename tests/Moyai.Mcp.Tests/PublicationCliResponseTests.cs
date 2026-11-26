using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Application.Providers;
using Moyai.Cli;

namespace Moyai.Mcp.Tests;

public sealed class PublicationCliResponseTests
{
    [Fact]
    public void StatusMissingRequestValuesRemainExplicitNullInActualMcpSerializer()
    {
        var context=new PublicationRequestContext("55555555-5555-4555-8555-555555555555",null,null,null);
        JsonElement json=JsonSerializer.SerializeToElement(context,ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        Assert.Equal(JsonValueKind.Null,json.GetProperty("source_branch").ValueKind);
        Assert.Equal(JsonValueKind.Null,json.GetProperty("destination_branch").ValueKind);
        Assert.Equal(JsonValueKind.Null,json.GetProperty("expected_source_sha").ValueKind);
    }

    [Theory]
    [InlineData("pull-request-publish-create")]
    [InlineData("pull-request-publish-status")]
    public void FailureRetainsSamePartialEnvelopeAndNonzeroExit(string command)
    {
        using JsonDocument document=JsonDocument.Parse("""
            {"ok":false,"operation":"pull_request_publish_create","request_context":{"operation_id":"55555555-5555-4555-8555-555555555555"},
             "provider_data_available":true,"data":{"publication_state":"created","pull_request_state":"unknown"},
             "error":{"code":"pull_request_result_unknown","outcome":"unknown","retryable":false,"suggested_action":"check_status"},
             "requires_reconciliation":true}
            """);
        var result=new CallToolResult { Content=[],StructuredContent=document.RootElement.Clone() };
        using var output=new StringWriter();
        using var error=new StringWriter();
        Assert.Equal(1,CliResponse.Write(command,result,output,error));
        using var parsed=JsonDocument.Parse(output.ToString());
        Assert.Equal("created",parsed.RootElement.GetProperty("data").GetProperty("publication_state").GetString());
        Assert.Equal(document.RootElement.GetRawText(),output.ToString().Trim());
        Assert.Equal("",error.ToString());
    }

    [Fact]
    public void UnknownWithoutDataRemainsExplicitNullOnCli()
    {
        var result=PullRequestPublicationResult.Failure("fixture","githubie",RepositoryOperation.PullRequestPublishCreate,
            new("55555555-5555-4555-8555-555555555555","feature","main",new string('a',40)),"provider_result_unknown",true);
        JsonElement json=JsonSerializer.SerializeToElement(result);
        using var output=new StringWriter();
        using var error=new StringWriter();
        Assert.Equal(1,CliResponse.Write("pull-request-publish-create",new() {Content=[],StructuredContent=json},output,error));
        using var parsed=JsonDocument.Parse(output.ToString());
        Assert.Equal(JsonValueKind.Null,parsed.RootElement.GetProperty("data").ValueKind);
        Assert.False(parsed.RootElement.GetProperty("provider_data_available").GetBoolean());
        Assert.True(parsed.RootElement.GetProperty("requires_reconciliation").GetBoolean());
    }
}

