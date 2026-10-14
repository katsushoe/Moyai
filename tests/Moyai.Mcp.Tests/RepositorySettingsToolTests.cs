using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class RepositorySettingsToolTests
{
    [Fact]
    public void SettingsToolsPublishReadAndDestructiveAnnotations()
    {
        var get = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.RepositorySettingsGet))!.GetCustomAttribute<McpServerToolAttribute>()!;
        var update = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.RepositorySettingsUpdate))!.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal("repository_settings_get", get.Name);
        Assert.True(get.ReadOnly);
        Assert.Equal("repository_settings_update", update.Name);
        Assert.True(update.Destructive);
    }

    [Fact]
    public void CliJsonChangesRetainFalseAndEmptyArrays()
    {
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"project":{"type":"string"},"changes":{"type":"object"}},"required":["project","changes"]}""");
        var args = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--changes", "{\"require_clean_working_tree\":false,\"pull_branches\":[]}"]), schema.RootElement, "repository_settings_update");
        JsonElement changes = Assert.IsType<JsonElement>(args["changes"]);
        Assert.False(changes.GetProperty("require_clean_working_tree").GetBoolean());
        Assert.Empty(changes.GetProperty("pull_branches").EnumerateArray());
        Assert.False(changes.TryGetProperty("protected_branches", out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"value\"")]
    public void CliRejectsNonObjectChanges(string value)
    {
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"changes":{"type":"object"}}}""");
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(CliArguments.Parse(["--changes", value]), schema.RootElement, "repository_settings_update"));
    }
}
