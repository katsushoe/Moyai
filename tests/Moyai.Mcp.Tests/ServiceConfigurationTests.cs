using System.Text.Json;
using Moyai.Cli;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class ServiceConfigurationTests
{
    [Theory]
    [InlineData("http://localhost:43120")]
    [InlineData("http://127.0.0.1:43120")]
    [InlineData("https://[::1]:43120")]
    public void ConfigurationAcceptsLoopback(string url) => new MoyaiSettings { ServerUrl = url }.Validate();

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///test.db")]
    [InlineData("http://user:secret@localhost")]
    [InlineData("")]
    public void ConfigurationRejectsUnsafeEndpoint(string url) => Assert.Throws<InvalidOperationException>(() => new MoyaiSettings { ServerUrl = url }.Validate());

    [Fact]
    public void LoadResolvesDatabaseAgainstConfigDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "moyai.json");
        try
        {
            File.WriteAllText(path, "{\"databasePath\":\"state.db\",\"serverUrl\":\"http://localhost:4444\"}");
            var settings = MoyaiSettings.Load(path);
            Assert.Equal(Path.Combine(directory, "state.db"), settings.DatabasePath);
            Assert.False(File.Exists(settings.DatabasePath));
            File.WriteAllText(path, "{\"unknown\":true}");
            Assert.Throws<JsonException>(() => MoyaiSettings.Load(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ArgumentsUseSchemaTypesAndRejectUnknownOptions()
    {
        using var schema = JsonDocument.Parse("{\"properties\":{\"expectedRevision\":{\"type\":\"integer\"},\"includeDeleted\":{\"type\":\"boolean\"}},\"required\":[\"expectedRevision\"]}");
        var values = CliArguments.Parse(["--expected-revision", "2", "--include-deleted"]);
        var converted = CliArguments.Convert(values, schema.RootElement, "test");
        Assert.Equal(2L, converted["expectedRevision"]);
        Assert.Equal(true, converted["includeDeleted"]);
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(new() { ["invalid"] = "x" }, schema.RootElement, "test"));
        Assert.Throws<ArgumentException>(() => CliArguments.Convert(new(), schema.RootElement, "test"));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["--name", "a", "--name", "b"]));
    }

    [Fact]
    public void OmittedNullableRequiredMcpParameterIsSentAsNull()
    {
        using var schema = JsonDocument.Parse("{\"properties\":{\"description\":{\"type\":[\"string\",\"null\"]}},\"required\":[\"description\"]}");
        var result = CliArguments.Convert(new(), schema.RootElement, "work_item_update");
        Assert.True(result.ContainsKey("description"));
        Assert.Null(result["description"]);
    }

    [Fact]
    public void PauseAndResumePreserveAdmissionState()
    {
        var admission = new ServiceAdmission();
        Assert.False(admission.IsPaused);
        admission.Pause(); Assert.True(admission.IsPaused);
        admission.Pause(); Assert.True(admission.IsPaused);
        admission.Resume(); Assert.False(admission.IsPaused);
    }
}
