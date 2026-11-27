using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Moyai.Application.Providers;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class LocalBranchCreateToolTests
{
    private static readonly string[] RequiredArguments = ["branch", "project", "source"];

    [Fact]
    public void ActualMcpSchemaRequiresOnlyProjectBranchAndSource()
    {
        MethodInfo method = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.BranchCreateLocal))!;
        McpServerTool tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("Schema-only test."));
        JsonElement schema = tool.ProtocolTool.InputSchema;
        Assert.Equal(RequiredArguments, schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).OrderBy(p => p).ToArray());
        Assert.Equal(3, schema.GetProperty("properties").EnumerateObject().Count());
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("source").GetProperty("type").GetString());
    }

    [Fact]
    public void LocalCreateIsSeparateDestructiveOperationWithRequiredSource()
    {
        MethodInfo method = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.BranchCreateLocal))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("branch_create_local", attribute.Name);
        Assert.True(attribute.Destructive);
        Assert.False(attribute.ReadOnly);
        Assert.False(method.GetParameters().Single(p => p.Name == "source").HasDefaultValue);
        Assert.Equal("branch_create_local", RepositoryOperation.BranchCreateLocal.ContractName());
        Assert.Equal("branch_create", RepositoryOperation.BranchCreate.ContractName());
    }

    [Theory]
    [InlineData("develop")]
    [InlineData("v1.0.1.0")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public void CliForwardsProjectBranchAndExplicitSource(string source)
    {
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"project":{"type":"string"},"branch":{"type":"string"},"source":{"type":"string"}},"required":["project","branch","source"]}""");
        var args = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--branch", "feature/local", "--source", source]), schema.RootElement, "branch_create_local");
        Assert.Equal("Display", args["project"]);
        Assert.Equal("feature/local", args["branch"]);
        Assert.Equal(source, args["source"]);
        Assert.Equal(3, args.Count);
    }

    [Fact]
    public void CliNeverSuppliesMissingSource()
    {
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"project":{"type":"string"},"branch":{"type":"string"},"source":{"type":"string"}},"required":["project","branch","source"]}""");
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--branch", "feature/local"]), schema.RootElement, "branch_create_local"));
    }
}
