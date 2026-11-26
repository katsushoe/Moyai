using System.Text.Json;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class ProviderUserHostConfigurationTests
{
    private static ProviderSettings Dynamic => new("buckettie", null, "bitbucket", true)
    { ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = Path.GetFullPath("runtime-fixture") };
    private static MoyaiSettings Settings(ProviderSettings provider) => new() { ApiToken = new string('A', 64), Providers = [provider] };

    [Fact]
    public void StaticConfigurationKeepsOriginalDefault() => Settings(new("githubbie", "http://127.0.0.1:9999/mcp", "github", true)).Validate();

    [Fact]
    public void OwnerAuthenticationRoundTripsWithoutIssuer()
    {
        MoyaiSettings settings = Settings(Dynamic with { AuthenticationMode = "windows_owner" });
        settings.Validate();
        MoyaiSettings loaded = JsonSerializer.Deserialize<MoyaiSettings>(JsonSerializer.Serialize(settings, MoyaiSettings.JsonOptions), MoyaiSettings.JsonOptions)!;
        loaded.Validate();
        Assert.Equal("windows_owner", loaded.Providers[0].AuthenticationMode);
        Assert.Equal("assertion", Dynamic.AuthenticationMode);
    }

    [Fact]
    public void GithubieOwnerConfigurationRequiresAbsoluteRuntimeButNotHash()
    {
        ProviderSettings provider = new("githubie", null, "github", true)
        {
            AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1",
            RuntimeExecutable = Path.GetFullPath("Githubie.Server.exe"), RuntimeSha256 = new string('A', 64),
        };
        Settings(provider).Validate();
        MoyaiSettings loaded = JsonSerializer.Deserialize<MoyaiSettings>(JsonSerializer.Serialize(Settings(provider), MoyaiSettings.JsonOptions), MoyaiSettings.JsonOptions)!;
        loaded.Validate();
        Settings(provider with { RuntimeSha256 = null }).Validate();
        Settings(provider with { RuntimeSha256 = "not-a-hash" }).Validate();
        Assert.Throws<InvalidOperationException>(() => Settings(provider with { RuntimeExecutable = "relative" }).Validate());
        Assert.Throws<InvalidOperationException>(() => Settings(provider with { AuthenticationMode = "assertion" }).Validate());
        Assert.Throws<InvalidOperationException>(() => Settings(provider with { Endpoint = "http://127.0.0.1:1234/mcp" }).Validate());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("static")]
    [InlineData("githubie")]
    public void InvalidOwnerModeIsRejected(string kind)
    {
        ProviderSettings provider = Dynamic with { AuthenticationMode = "windows_owner" };
        provider = kind switch
        {
            "unknown" => provider with { AuthenticationMode = "unknown" },
            "static" => provider with { ConnectionMode = "static", Endpoint = "http://127.0.0.1:9999/mcp", RuntimeDirectory = null },
            _ => provider with { Name = "githubie", ToolPrefix = "github" },
        };
        Assert.Throws<InvalidOperationException>(() => Settings(provider).Validate());
    }

    [Fact]
    public void ExplicitDynamicModeNeedsNoEndpoint()
    {
        Settings(Dynamic).Validate();
        string json = JsonSerializer.Serialize(Settings(Dynamic), MoyaiSettings.JsonOptions);
        MoyaiSettings loaded = JsonSerializer.Deserialize<MoyaiSettings>(json, MoyaiSettings.JsonOptions)!;
        loaded.Validate();
        Assert.Null(loaded.Providers[0].Endpoint);
        Assert.Equal(Dynamic.RuntimeDirectory, loaded.Providers[0].RuntimeDirectory);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("prefix")]
    [InlineData("endpoint")]
    [InlineData("mode")]
    [InlineData("missing-runtime")]
    [InlineData("relative-runtime")]
    [InlineData("static-runtime")]
    public void InvalidConnectionConfigurationIsRejected(string kind)
    {
        ProviderSettings provider = kind switch
        {
            "provider" => Dynamic with { Name = "githubie" },
            "prefix" => Dynamic with { ToolPrefix = "github" },
            "endpoint" => Dynamic with { Endpoint = "http://127.0.0.1:9999/mcp" },
            "mode" => Dynamic with { ConnectionMode = "unknown" },
            "missing-runtime" => Dynamic with { RuntimeDirectory = null },
            "relative-runtime" => Dynamic with { RuntimeDirectory = "relative" },
            _ => Dynamic with { ConnectionMode = "static", Endpoint = "http://127.0.0.1:9999/mcp" },
        };
        Assert.Throws<InvalidOperationException>(() => Settings(provider).Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("mcp")]
    [InlineData("//mcp")]
    [InlineData("/a/../mcp")]
    [InlineData("/mcp?x=1")]
    [InlineData("/mcp#x")]
    [InlineData("/mcp\\other")]
    public void NoncanonicalExpectedPathIsRejected(string path) => Assert.Throws<InvalidOperationException>(() => Settings(Dynamic with { ExpectedMcpPath = path }).Validate());
}
