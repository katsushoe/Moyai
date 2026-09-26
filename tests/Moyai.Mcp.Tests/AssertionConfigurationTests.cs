using System.Reflection;
using ModelContextProtocol.Server;
using Moyai.Configuration;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class AssertionConfigurationTests
{
    [Theory]
    [InlineData(29, 30)]
    [InlineData(301, 30)]
    [InlineData(120, -1)]
    [InlineData(120, 61)]
    public void ConfigurationRejectsInvalidLifetimeOrSkew(int lifetime, int skew) =>
        Assert.Throws<InvalidOperationException>(() => new ProviderAuthenticationSettings { LifetimeSeconds = lifetime, ClockSkewSeconds = skew }.Validate());

    [Theory]
    [InlineData(30, 0)]
    [InlineData(300, 60)]
    public void ConfigurationAcceptsBoundaryLifetimeAndSkew(int lifetime, int skew) =>
        new ProviderAuthenticationSettings { LifetimeSeconds = lifetime, ClockSkewSeconds = skew }.Validate();

    [Theory]
    [InlineData("plaintext")]
    [InlineData("passphrase")]
    [InlineData("unknown")]
    public void ConfigurationDoesNotFallBackForUnsupportedProtector(string mode) =>
        Assert.Throws<InvalidOperationException>(() => new ProviderAuthenticationSettings { ProtectorMode = mode }.Validate());

    [Fact]
    public void BrokerRequiresMutualTlsAndNoCredentialInUrl()
    {
        Assert.Throws<InvalidOperationException>(() => new ProviderAuthenticationSettings { ProtectorMode = "broker", BrokerEndpoint = "http://localhost" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ProviderAuthenticationSettings { ProtectorMode = "broker", BrokerEndpoint = "https://localhost" }.Validate());
        new ProviderAuthenticationSettings { ProtectorMode = "broker", BrokerEndpoint = "https://localhost", BrokerCertificateThumbprint = "public-certificate-id" }.Validate();
    }

    [Theory]
    [InlineData("githubbie", "githubie")]
    [InlineData("buckettie", "buckettie")]
    public void AssertionProviderIdResolvesOnlyTheLegacyGithubieRoutingName(string routingName, string expected) =>
        Assert.Equal(expected, new ProviderSettings(routingName, "http://127.0.0.1:43121/mcp", "provider", true).AssertionProviderId);

    [Fact]
    public void KeyManagementToolsExposeOnlyNonSecretInputs()
    {
        MethodInfo[] methods = typeof(AssertionTools).GetMethods().Where(static method => method.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();
        Assert.Equal(6, methods.Length);
        foreach (MethodInfo method in methods)
        {
            Assert.DoesNotContain(method.GetParameters(), static parameter => parameter.Name is "token" or "assertion" or "password" or "privateKey" or "dek" or "kek");
        }
    }
}
