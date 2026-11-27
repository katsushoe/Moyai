using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moyai.Cli;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class CliFailureIntegrationTests
{
    private static readonly string[] ProtocolVersions = ["2026-07-28"];
    private static readonly string[] RequiredArguments = ["project"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task McpClientBusinessStatusUsesCorrectExitAndStream(bool ok, bool structured)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        await using WebApplication app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        int calls = 0;
        app.MapPost("/mcp", async (HttpContext context) =>
        {
            Assert.Equal("Bearer " + new string('A', 64), context.Request.Headers.Authorization.ToString());
            using JsonDocument document = await JsonDocument.ParseAsync(context.Request.Body);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id))
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }
            string? method = root.GetProperty("method").GetString();
            object response = method switch
            {
                "server/discover" => new { supportedVersions = ProtocolVersions, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" },
                "initialize" => new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "test-service", version = "1.0" } },
                "tools/list" => new { tools = new[] { new { name = "repository_status", inputSchema = new { type = "object", properties = new { project = new { type = "string" } }, required = RequiredArguments } } } },
                "tools/call" => BusinessResponse(ok, structured),
                _ => throw new InvalidOperationException("Unexpected test request: " + method),
            };
            if (method == "tools/call")
            {
                Assert.Equal("repository_status", root.GetProperty("params").GetProperty("name").GetString());
                Assert.Equal("Moyai", root.GetProperty("params").GetProperty("arguments").GetProperty("project").GetString());
                Interlocked.Increment(ref calls);
            }
            await context.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, result = response });
        });
        await app.StartAsync();
        string directory = Path.Combine(Path.GetTempPath(), "moyai-response-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "moyai.json");
        try
        {
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { serverUrl = app.Urls.Single(), requestTimeoutSeconds = 10, apiToken = new string('A', 64) }));
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exitCode = await McpCommandClient.ExecuteAsync(MoyaiSettings.Load(config), "repository-status",
                new() { ["project"] = "Moyai" }, stdout, stderr);
            string output = stdout.ToString();
            string error = stderr.ToString();
            Assert.Equal(ok ? 0 : 1, exitCode);
            Assert.Equal(1, calls);
            Assert.Equal("", ok ? error : output);
            using JsonDocument result = JsonDocument.Parse(ok ? output : error);
            Assert.Equal(ok, result.RootElement.GetProperty("ok").GetBoolean());
            if (!ok) Assert.Contains("provider_not_found", result.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(config);
            Directory.Delete(directory);
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("moyaictl", false)]
    [InlineData("moyaictl", true)]
    [InlineData("moyaimcp", false)]
    [InlineData("moyaimcp", true)]
    public async Task NativeClientsRejectAlternativeOperationalConfiguration(string executable, bool leading)
    {
        string otherConfig = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "moyai.json");
        var start = new ProcessStartInfo(ClientPath(executable)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        string[] arguments = leading ? ["--config", otherConfig, "repository-status", "--project", "Moyai"]
            : ["repository-status", "--project", "Moyai", "--config", otherConfig];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(1, process.ExitCode);
            Assert.Equal("", await stdout);
            using JsonDocument result = JsonDocument.Parse(await stderr);
            Assert.Equal("ArgumentException", result.RootElement.GetProperty("error").GetProperty("code").GetString());
            using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var product = machine.OpenSubKey(@"Software\Akatsukisoft\Moyai");
            string expectedConfig = OperationalConfigurationPath.Resolve(Path.GetDirectoryName(ClientPath(executable))!, product?.GetValue("InstallDirectory") as string);
            Assert.Contains(expectedConfig, result.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("moyaictl", "--help")]
    [InlineData("moyaictl", "--version")]
    [InlineData("moyaimcp", "--help")]
    [InlineData("moyaimcp", "--version")]
    public async Task LocalFlagsWorkWithoutConfigurationOrRunningService(string executable, string flag)
    {
        var start = new ProcessStartInfo(ClientPath(executable)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "--config", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json"), flag }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", await stderr);
            if (flag == "--version") Assert.Equal(typeof(CliResponse).Assembly.GetName().Version!.ToString(), (await stdout).Trim());
            else Assert.Contains("MCP command client", await stdout, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    private static string ClientPath(string executable)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moyai.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string path = Path.Combine(directory.FullName, "src", "Moyai.Cli", "bin", configuration, "net8.0-windows", executable + ".exe");
        Assert.True(File.Exists(path), "Missing built CLI executable: " + path);
        return path;
    }

    private static object BusinessResponse(bool ok, bool structured)
    {
        string payload = JsonSerializer.Serialize(new { ok, operation = "repository_status", output = ok ? "done" : null, errorCode = ok ? null : "provider_not_found", errorMessage = ok ? null : "repository missing" });
        return structured
            ? (object)new { isError = false, structuredContent = JsonSerializer.Deserialize<JsonElement>(payload), content = Array.Empty<object>() }
            : new { isError = false, content = new[] { new { type = "text", text = payload } } };
    }
}
