using System.Text.Json;
using Moyai.Cli;

namespace Moyai.Mcp.Tests;

public sealed class CliArgumentsStashTests
{
    private static readonly string[] ExpectedPaths = ["src/a.cs", "docs/readme.md"];
    [Fact]
    public void ConvertParsesSelectedStashPathsAsRepositoryRelativePathArray()
    {
        using JsonDocument schema = JsonDocument.Parse("""
            {"type":"object","properties":{"project":{"type":"string"},"message":{"type":"string"},"paths":{"type":"array","items":{"type":"string"}}},"required":["project","message"]}
            """);
        Dictionary<string, string?> values = CliArguments.Parse(
            ["--project", "Buckettie", "--message", "split changes", "--paths", "src/a.cs,docs/readme.md"]);

        IReadOnlyDictionary<string, object?> arguments = CliArguments.Convert(values, schema.RootElement, "repository_stash_push");

        Assert.Equal("Buckettie", arguments["project"]);
        Assert.Equal("split changes", arguments["message"]);
        Assert.Equal(ExpectedPaths, Assert.IsType<string[]>(arguments["paths"]));
    }
}
