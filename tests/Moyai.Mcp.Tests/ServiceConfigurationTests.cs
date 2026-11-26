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
    public void ConfigurationAcceptsLoopback(string url) => new MoyaiSettings { ServerUrl = url, ApiToken = new string('A', 64) }.Validate();

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///test.db")]
    [InlineData("http://user:secret@localhost")]
    [InlineData("")]
    public void ConfigurationRejectsUnsafeEndpoint(string url) => Assert.Throws<InvalidOperationException>(() => new MoyaiSettings { ServerUrl = url, ApiToken = new string('A', 64) }.Validate());

    [Fact]
    public void LoadResolvesDatabaseAgainstConfigDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "moyai.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { databasePath = "state.db", serverUrl = "http://localhost:4444", apiToken = new string('A', 64) }));
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
    public void OperationalConfigurationIsFixedAndRuntimeStateRemainsUserScoped()
    {
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akatsukisoft", "Moyai");
        Assert.True(Path.IsPathFullyQualified(MoyaiSettings.DefaultPath));
        Assert.Equal("moyai.json", Path.GetFileName(MoyaiSettings.DefaultPath));
        Assert.Equal("config", Path.GetFileName(Path.GetDirectoryName(MoyaiSettings.DefaultPath)));
        Assert.Equal(Path.Combine(profile, "data", "moyai.db"), new MoyaiSettings().DatabasePath);
        Assert.Equal(Path.Combine(profile, "logs"), MoyaiSettings.DefaultLogDirectory);
    }

    [Fact]
    public void OperationalConfigurationAcceptsOnlyCanonicalAliases()
    {
        string canonical = MoyaiSettings.DefaultPath;
        foreach (string? path in new[] { null, canonical, canonical.ToLowerInvariant(),
            Path.Combine(Path.GetDirectoryName(canonical)!, "..", "config", "moyai.json") })
            Assert.Equal(canonical, MoyaiSettings.ResolveOperationalPath(path));
    }

    [Theory]
    [InlineData(@"D:\Apps\Moyai\bin", @"D:\Apps\Moyai\config\moyai.json")]
    [InlineData(@"D:\Apps\Moyai\bin\host", @"D:\Apps\Moyai\config\moyai.json")]
    [InlineData(@"D:\Apps\Moyai\bin\tray", @"D:\Apps\Moyai\config\moyai.json")]
    [InlineData(@"E:\My Apps\もやい\bin\host", @"E:\My Apps\もやい\config\moyai.json")]
    public void UnregisteredPackageUsesItsInstallationDirectory(string binaryDirectory, string expected)
        => Assert.Equal(expected, OperationalConfigurationPath.Resolve(binaryDirectory));

    [Fact]
    public void EmbeddedInstallerHelperUsesRegisteredInstallationRatherThanExtractionDirectory()
        => Assert.Equal(@"E:\My Apps\もやい\config\moyai.json",
            OperationalConfigurationPath.Resolve(@"D:\Temp\MSI-helper", @"E:\My Apps\もやい"));

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public void InvalidRegisteredInstallationFailsWithoutFallback(string directory)
        => Assert.Throws<InvalidOperationException>(() => OperationalConfigurationPath.Resolve(@"D:\Temp\MSI-helper", directory));

    [Fact]
    public void OperationalConfigurationRejectsProfileAndOtherFiles()
    {
        Assert.Throws<ArgumentException>(() => MoyaiSettings.ResolveOperationalPath(Path.Combine(MoyaiSettings.ProfileDirectory, "config", "moyai.json")));
        Assert.Throws<ArgumentException>(() => MoyaiSettings.ResolveOperationalPath(Path.Combine(Path.GetTempPath(), "moyai.json")));
    }
}
