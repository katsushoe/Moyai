using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moyai.Application.Providers;
using Moyai.Cli;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class RepositorySettingsToolTests
{
    [Theory]
    [InlineData("githubie")]
    [InlineData("buckettie")]
    public void SettingsMcpJsonRetainsUnsetAuthors(string provider)
    {
        var settings = new RepositorySettings(["develop"], ["develop"], ["main"], "main", "v.*", false, null, null);
        var result = new RepositorySettingsResult(true, "repository_settings_get", "Fixture", settings, [], false, null, provider);

        JsonElement json = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        JsonElement returned = json.GetProperty("settings");
        Assert.Equal(provider, json.GetProperty("provider").GetString());

        Assert.Equal(8, returned.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("commit_author_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("commit_author_email").ValueKind);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GithubieLocalCommitOutputDistinguishesAbsentAndEmpty(bool present)
    {
        var settings = new RepositorySettings(["develop"], ["develop"], ["main"], "main", "v.*", true, null, null, present ? [] : null);
        JsonElement output = JsonSerializer.SerializeToElement(settings, McpJsonUtilities.DefaultOptions);
        Assert.Equal(present ? 9 : 8, output.EnumerateObject().Count());
        Assert.Equal(present, output.TryGetProperty("local_commit_branches", out JsonElement local));
        if (present) Assert.Empty(local.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, output.GetProperty("commit_author_name").ValueKind);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"codex/docs-main-v1010\",\"develop\"]")]
    public void CliLocalCommitArrayIsForwardedWithoutDefaults(string value)
    {
        using JsonDocument schema = JsonDocument.Parse("""{"properties":{"project":{"type":"string"},"changes":{"type":"object"}},"required":["project","changes"]}""");
        var args = CliArguments.Convert(CliArguments.Parse(["--project", "Display", "--changes", "{\"local_commit_branches\":" + value + "}"]), schema.RootElement, "repository_settings_update");
        var changes = Assert.IsType<JsonElement>(args["changes"]);
        Assert.Single(changes.EnumerateObject());
        Assert.Equal(value, changes.GetProperty("local_commit_branches").GetRawText());
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
