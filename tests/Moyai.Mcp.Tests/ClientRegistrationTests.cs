using System.Text.Json.Nodes;
using Moyai.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace Moyai.Mcp.Tests;

public sealed class ClientRegistrationTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "Moyai-Client-" + Guid.NewGuid().ToString("N"));
    private const string Endpoint = "http://127.0.0.1:43120/mcp";

    public ClientRegistrationTests() => Directory.CreateDirectory(_profile);
    public void Dispose() => Directory.Delete(_profile, true);
    private string Config(string client) => client == "codex" ? Path.Combine(_profile, ".codex", "config.toml") : Path.Combine(_profile, ".claude.json");
    private void Seed(string client, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config(client))!);
        File.WriteAllText(Config(client), text);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void ConfigureAndUnconfigurePreserveOtherSettingsAndAreIdempotent(string client)
    {
        Seed(client, client == "codex" ? "# retained comment\nmodel = 'example'\n[mcp_servers.other]\nurl = 'http://localhost:4/mcp'\n" : "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"url\":\"http://localhost:4/mcp\"}}}");
        var registration = new ClientRegistration(client, _profile);
        Assert.Equal("configured", registration.Apply(true, Endpoint));
        byte[] after = File.ReadAllBytes(Config(client));
        Assert.Equal("unchanged", registration.Apply(true, Endpoint));
        Assert.Equal(after, File.ReadAllBytes(Config(client)));
        Assert.Equal("unconfigured", registration.Apply(false, null));
        Assert.Equal("not_owned", registration.Apply(false, null));
        string text = File.ReadAllText(Config(client));
        if (client == "codex")
        {
            var model = TomlSerializer.Deserialize<TomlTable>(text)!;
            Assert.Equal("example", model["model"]);
            Assert.True(((TomlTable)model["mcp_servers"]).ContainsKey("other"));
            Assert.Contains("retained comment", text);
        }
        else
        {
            JsonNode root = JsonNode.Parse(text)!;
            Assert.Equal("dark", root["theme"]!.GetValue<string>());
            Assert.NotNull(root["mcpServers"]!["other"]);
            Assert.Null(root["mcpServers"]!["moyai"]);
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void PreexistingIdenticalEntryIsNotAdoptedOrRemoved(string client)
    {
        Seed(client, client == "codex" ? $"[mcp_servers.moyai]\nurl = '{Endpoint}'\n" : "{\"mcpServers\":{\"moyai\":{\"type\":\"http\",\"url\":\"" + Endpoint + "\"}}}");
        byte[] before = File.ReadAllBytes(Config(client));
        var registration = new ClientRegistration(client, _profile);
        Assert.Equal("unchanged", registration.Apply(true, Endpoint));
        Assert.Equal("not_owned", registration.Apply(false, null));
        Assert.Equal(before, File.ReadAllBytes(Config(client)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void RollbackRestoresOriginalBytesAndOwnership(string client)
    {
        Seed(client, client == "codex" ? "model='example'\r\n" : "{ \"theme\": \"dark\" }");
        byte[] before = File.ReadAllBytes(Config(client));
        var registration = new ClientRegistration(client, _profile);
        registration.Apply(true, Endpoint, true);
        registration.Finish("rollback");
        Assert.Equal(before, File.ReadAllBytes(Config(client)));
        Assert.Equal("not_owned", registration.Apply(false, null));
        registration.Apply(true, Endpoint);
        byte[] installed = File.ReadAllBytes(Config(client));
        registration.Apply(false, null, true);
        registration.Finish("rollback");
        Assert.Equal(installed, File.ReadAllBytes(Config(client)));
        Assert.Equal("unconfigured", registration.Apply(false, null));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void AbsentClientCanBePreconfiguredAndRollbackRemovesCreatedFile(string client)
    {
        var registration = new ClientRegistration(client, _profile);
        registration.Apply(true, Endpoint, true);
        Assert.True(File.Exists(Config(client)));
        registration.Finish("rollback");
        Assert.False(File.Exists(Config(client)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void OwnedEndpointCanUpgradeAndCommit(string client)
    {
        var registration = new ClientRegistration(client, _profile);
        registration.Apply(true, Endpoint);
        registration.Apply(true, "http://localhost:43210/mcp", true);
        Assert.Throws<InvalidOperationException>(() => registration.Apply(false, null));
        registration.Finish("commit");
        Assert.Contains("43210", File.ReadAllText(Config(client)));
        Assert.Equal("unconfigured", registration.Apply(false, null));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void ConflictingOrMalformedInputLeavesFilesUnchanged(string client)
    {
        Seed(client, client == "codex" ? "[mcp_servers.moyai]\ncommand='other'" : "{\"mcpServers\":{\"moyai\":{\"command\":\"other\"}}}");
        var registration = new ClientRegistration(client, _profile);
        byte[] before = File.ReadAllBytes(Config(client));
        Assert.Throws<InvalidOperationException>(() => registration.Apply(true, Endpoint));
        Assert.Equal(before, File.ReadAllBytes(Config(client)));
        Seed(client, "not valid { [");
        Assert.Throws<InvalidOperationException>(() => registration.Apply(true, Endpoint));
        Assert.Equal("not valid { [", File.ReadAllText(Config(client)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void RollbackRefusesConcurrentEdits(string client)
    {
        var registration = new ClientRegistration(client, _profile);
        registration.Apply(true, Endpoint, true);
        File.AppendAllText(Config(client), " ");
        Assert.Throws<IOException>(() => registration.Finish("rollback"));
        Assert.EndsWith(" ", File.ReadAllText(Config(client)));
        registration.Finish("commit");
    }

    [Fact]
    public void InstallerRollbackDoesNotConsumeAnotherTransactionsJournal()
    {
        var registration = new ClientRegistration("codex", _profile);
        registration.Apply(true, Endpoint, true, "previous-install");
        Assert.Throws<InvalidOperationException>(() => registration.Apply(true, Endpoint, true, "new-install"));
        registration.Finish("rollback", "new-install");
        Assert.True(File.Exists(Config("codex")));
        registration.Finish("rollback", "previous-install");
        Assert.False(File.Exists(Config("codex")));
    }
}
