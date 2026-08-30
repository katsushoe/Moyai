using System.Text.Json;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class StandardBuildProviderTests
{
    [Theory]
    [InlineData("csharp")]
    [InlineData("node")]
    [InlineData("php")]
    public async Task BuildExecutesInstalledStandardTool(string providerName)
    {
        string root = Path.Combine(Path.GetTempPath(), $"moyai-standard-build-{providerName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await CreateProjectAsync(root, providerName);
            var provider = new StandardBuildProvider(providerName);
            var request = new LifecycleRequest("Moyai", root, null, LifecycleAction.Build, null, null, null, null);

            LifecycleResult result = await provider.ExecuteAsync(request);

            Assert.True(result.Ok, result.ErrorMessage);
            Assert.Equal("build", result.Operation);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static Task CreateProjectAsync(string root, string providerName) => providerName switch
    {
        "csharp" => File.WriteAllTextAsync(Path.Combine(root, "Test.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"),
        "node" => File.WriteAllTextAsync(Path.Combine(root, "package.json"), JsonSerializer.Serialize(new { scripts = new Dictionary<string, string> { ["build"] = "node -e \"require('fs').writeFileSync('build.txt','node')\"" } })),
        "php" => File.WriteAllTextAsync(Path.Combine(root, "composer.json"), JsonSerializer.Serialize(new { scripts = new Dictionary<string, string> { ["build"] = "php -r \"file_put_contents('build.txt','php');\"" } })),
        _ => throw new ArgumentOutOfRangeException(nameof(providerName)),
    };
}
