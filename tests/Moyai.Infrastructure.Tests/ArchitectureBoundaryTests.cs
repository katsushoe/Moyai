using System.Text.RegularExpressions;

namespace Moyai.Infrastructure.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] SharedApplicationServices =
    [
        "ProjectService",
        "ProjectQueryService",
        "WorkItemService",
        "WorkItemCollaborationService",
        "ReleaseService",
        "ReleaseContentService",
        "ReleaseOrchestrationService",
        "ProviderRoutingService",
        "LifecycleService",
        "BuildService",
        "DeploymentService",
        "ServiceTokenLifecycleService",
    ];

    [Fact]
    public void CliAndMcpComposeTheSameApplicationServices()
    {
        string cli = ReadSource("src", "Moyai.Cli", "Program.cs");
        string mcp = ReadSource("src", "Moyai.Mcp", "Program.cs");

        foreach (string service in SharedApplicationServices)
        {
            Assert.Contains(service, cli, StringComparison.Ordinal);
            Assert.Contains(service, mcp, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void McpToolsDoNotDependOnSqlitePersistence()
    {
        string tools = ReadSource("src", "Moyai.Mcp", "Tools", "MoyaiTools.cs");

        Assert.DoesNotContain("Microsoft.Data.Sqlite", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("Moyai.Infrastructure.Persistence", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", tools, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryAndLifecycleAdaptersDoNotStartProviderProcesses()
    {
        string repository = ReadSource("src", "Moyai.Infrastructure", "Providers", "McpRepositoryProvider.cs");
        string lifecycle = ReadSource("src", "Moyai.Infrastructure", "Providers", "McpLifecycleProvider.cs");

        foreach (string source in new[] { repository, lifecycle })
        {
            Assert.DoesNotContain("System.Diagnostics", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MoyaiSourceDoesNotInvokeGitCli()
    {
        string root = RepositoryRoot();
        var forbidden = new Regex("(?:FileName\\s*=\\s*|ProcessStartInfo\\s*\\()\\s*\\\"git(?:\\.exe)?\\\"", RegexOptions.CultureInvariant);

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotMatch(forbidden, File.ReadAllText(file));
        }
    }

    private static string ReadSource(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moyai.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Moyai repository root was not found.");
    }
}
