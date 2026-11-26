using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class BranchProtectionToolTests
{
    [Theory]
    [InlineData(nameof(MoyaiTools.BranchProtectionGet), "branch_protection_get", false)]
    [InlineData(nameof(MoyaiTools.BranchProtectionUpdate), "branch_protection_update", true)]
    [InlineData(nameof(MoyaiTools.BranchProtectionDelete), "branch_protection_delete", true)]
    public void HostingToolAnnotationsAreExplicit(string method, string name, bool destructive)
    {
        var attribute = typeof(MoyaiTools).GetMethod(method)!.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal(name, attribute.Name);
        Assert.Equal(destructive, attribute.Destructive);
        Assert.Equal(!destructive, attribute.ReadOnly);
    }

    [Fact]
    public void CliUsesActualEditSchemaAndKeepsExplicitFalse()
    {
        var tool = McpServerTool.Create(typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.BranchProtectionUpdate))!, _ => throw new InvalidOperationException("Schema only."));
        var arguments = CliArguments.Convert(CliArguments.Parse(["--project", "display", "--branch", "main", "--edit", "{\"allow_force_pushes\":false}", "--create-if-missing", "true"]), tool.ProtocolTool.InputSchema, "branch_protection_update");
        Assert.Equal("display", arguments["project"]);
        Assert.True((bool)arguments["createIfMissing"]!);
        JsonElement edit = Assert.IsType<JsonElement>(arguments["edit"]);
        Assert.False(edit.GetProperty("allow_force_pushes").GetBoolean());
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(CliArguments.Parse(["--project", "display", "--branch", "main"]), tool.ProtocolTool.InputSchema, "branch_protection_update"));
    }
}
