using System.Text.Json;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class TrayUpdateTests
{
    [Theory]
    [InlineData("2.1.0.0", false, true, true)]
    [InlineData("2.2.0.0", false, true, false)]
    [InlineData("2.1.0.0", true, true, false)]
    [InlineData("2.1.0.0", false, false, false)]
    public void SelectRequiresNewerStableOfficialAsset(string installed, bool preview, bool official, bool expected)
    {
        const string name = "Moyai-2.2.0.0-x64.msi";
        using var release = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = "v2.2.0.0",
            draft = false,
            prerelease = preview,
            assets = new[] { new { name, state = "uploaded", size = 100,
                digest = "sha256:" + new string('a', 64),
                browser_download_url = $"https://{(official ? "github.com" : "example.com")}/katsushoe/Moyai/releases/download/v2.2.0.0/{name}" } }
        }));
        Assert.Equal(expected, StartupUpdate.Select(release.RootElement, Version.Parse(installed)) is not null);
    }

    [Fact]
    public void SettingsKeepSecretsAndRejectStaleSaves()
    {
        string path = Path.GetTempFileName();
        string token = new('a', 64);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { apiToken = token, databasePath = "relative.db" }));
            var original = new SettingsFile(path);
            var stale = new SettingsFile(path);
            Assert.True(original.AutoUpdate);
            original.Save(false, 300);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal(token, json.RootElement.GetProperty("apiToken").GetString());
            Assert.Equal("relative.db", json.RootElement.GetProperty("databasePath").GetString());
            Assert.Equal(300, new SettingsFile(path).Timeout);
            Assert.False(new SettingsFile(path).AutoUpdate);
            Assert.Throws<IOException>(() => stale.Save(true, 500));
        }
        finally { File.Delete(path); }
    }
}
