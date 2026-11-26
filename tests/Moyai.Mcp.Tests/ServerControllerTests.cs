using System.Globalization;
using System.Resources;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class ServerControllerTests
{
    [Theory]
    [InlineData("{\"serverUrl\":\"http://127.0.0.1:12345\"}", "http://127.0.0.1:12345/health")]
    [InlineData("{\"serverUrl\":\"http://localhost:23456\"}", "http://localhost:23456/health")]
    [InlineData("{\"serverUrl\":\"https://example.com\"}", "http://127.0.0.1:43120/health")]
    [InlineData("{\"serverUrl\":\"ftp://localhost\"}", "http://127.0.0.1:43120/health")]
    [InlineData("{\"serverUrl\":\"http://user:pass@localhost\"}", "http://127.0.0.1:43120/health")]
    [InlineData("{", "http://127.0.0.1:43120/health")]
    [InlineData("[]", "http://127.0.0.1:43120/health")]
    [InlineData("{}", "http://127.0.0.1:43120/health")]
    [InlineData(null, "http://127.0.0.1:43120/health")]
    public void HealthUriUsesOnlyConfiguredLoopbackOrDefault(string? json, string expected)
    {
        string path = Path.Combine(Path.GetTempPath(), "moyai-health-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            if (json is not null) File.WriteAllText(path, json);
            Assert.Equal(expected, ServerController.ResolveHealthUri(path).AbsoluteUri);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(200, "{\"status\":\"ok\",\"version\":\"2.1.0.1\"}", true, "2.1.0.1")]
    [InlineData(299, "{\"status\":\"ok\"}", true, null)]
    [InlineData(200, "{\"status\":\"unhealthy\"}", false, null)]
    [InlineData(500, "{\"status\":\"ok\"}", false, null)]
    [InlineData(302, "{\"status\":\"ok\"}", false, null)]
    [InlineData(200, "{", false, null)]
    [InlineData(200, "[]", false, null)]
    public void HealthClassificationRequiresSuccessAndOk(int code, string body, bool running, string? version)
    {
        var status = ServerController.ClassifyHealth(code, body);
        Assert.Equal(running ? ServerState.Running : ServerState.Error, status.State);
        Assert.Equal(version, status.Version);
    }

    [Theory]
    [InlineData("GET", "/health", true)]
    [InlineData("POST", "/health", false)]
    [InlineData("GET", "/mcp", false)]
    public async Task OnlyHealthGetBypassesAuthentication(string method, string path, bool health)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        using var body = new MemoryStream();
        context.Response.Body = body;
        var endpoint = new HealthEndpoint(c => { c.Response.StatusCode = 401; return Task.CompletedTask; });
        await endpoint.InvokeAsync(context);
        Assert.Equal(health ? 200 : 401, context.Response.StatusCode);
        if (health)
        {
            using var json = JsonDocument.Parse(body.ToArray());
            Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(typeof(HealthEndpoint).Assembly.GetName().Version!.ToString(), json.RootElement.GetProperty("version").GetString());
        }
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("en")]
    public void StopWarningNamesProductClientsAndRestartCondition(string culture)
    {
        var resources = new ResourceManager("Moyai.Tray.MainForm", typeof(MainForm).Assembly);
        string warning = resources.GetString("ServerStopConfirm", CultureInfo.GetCultureInfo(culture))!;
        Assert.Contains("Moyai", warning, StringComparison.Ordinal);
        Assert.Contains("Codex", warning, StringComparison.Ordinal);
        Assert.Contains("Claude Code", warning, StringComparison.Ordinal);
        Assert.Contains(culture == "ja" ? "再び起動するまで" : "until it is started again", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPillLedsForVisualReview()
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mcp-server-led-20261126"));
        Directory.CreateDirectory(directory);
        using var leds = new StatusLed();
        foreach (var state in Enum.GetValues<ServerState>())
        {
            var bitmap = Assert.IsType<System.Drawing.Bitmap>(leds.For(new(state)));
            Assert.Equal(16, bitmap.Width);
            Assert.Equal(16, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(8, 0).A);
            Assert.True(bitmap.GetPixel(8, 8).A > 240);
            bitmap.Save(Path.Combine(directory, state + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Assert.Same(leds.For(null), leds.For(new(ServerState.Stopped)));
    }
}
