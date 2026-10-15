using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class BranchSwitchToolTests
{
    [Fact]
    public void SwitchIsDestructiveAndCliForwardsOnlyProjectAndBranch()
    {
        var attribute = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.BranchSwitch))!.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("branch_switch", attribute.Name);
        Assert.True(attribute.Destructive);
        Assert.False(attribute.ReadOnly);
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"project":{"type":"string"},"branch":{"type":"string"}},"required":["project","branch"]}""");
        var args = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--branch", "feature/existing"]), schema.RootElement, "branch_switch");
        Assert.Equal("Display", args["project"]);
        Assert.Equal("feature/existing", args["branch"]);
        Assert.Equal(2, args.Count);
    }
}
