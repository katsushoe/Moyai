using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Moyai.Mcp.Tests;

public sealed class McpHostSettingsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:43120")]
    [InlineData("http://localhost:43120")]
    [InlineData("http://[::1]:43120")]
    [InlineData("https://localhost:43120")]
    public void ReadAcceptsLoopback(string url)
    {
        var settings = McpHostSettings.Read(Configuration("test.db", url));
        Assert.Equal("test.db", settings.DatabasePath);
        Assert.Equal(url, settings.ServerUrl);
    }

    [Theory]
    [InlineData(null, "http://localhost:43120")]
    [InlineData("", "http://localhost:43120")]
    [InlineData(" ", "http://localhost:43120")]
    [InlineData("test.db", null)]
    [InlineData("test.db", " ")]
    [InlineData("test.db", "not-a-url")]
    [InlineData("test.db", "http://0.0.0.0:43120")]
    [InlineData("test.db", "http://example.com:43120")]
    [InlineData("test.db", "file:///test.db")]
    public void ReadRejectsMissingOrUnsafeSettings(string? path, string? url) =>
        Assert.Throws<InvalidOperationException>(() => McpHostSettings.Read(Configuration(path, url)));

    [Fact]
    public void InstallerArgumentsOverrideEarlierSettings()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(Configuration("old.db", "http://localhost:43121"))
            .AddCommandLine(["--MOYAI_DB_PATH", @"C:\Moyai\data\moyai.db", "--MOYAI_MCP_URL", "http://127.0.0.1:43120"])
            .Build();
        Assert.Equal(new McpHostSettings(@"C:\Moyai\data\moyai.db", "http://127.0.0.1:43120"), McpHostSettings.Read(configuration));
    }

    [Fact]
    public void ServiceRegistrationPreservesInteractiveLifetime()
    {
        using IHost host = new HostBuilder().ConfigureServices(services => services.AddWindowsService(options => options.ServiceName = "Moyai")).Build();
        Assert.IsType<Microsoft.Extensions.Hosting.Internal.ConsoleLifetime>(host.Services.GetRequiredService<IHostLifetime>());
    }

    private static IConfiguration Configuration(string? path, string? url) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["MOYAI_DB_PATH"] = path, ["MOYAI_MCP_URL"] = url }).Build();
}
