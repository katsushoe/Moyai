using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using Moyai.Application.Projects;
using Moyai.Cli;
using Moyai.Infrastructure.Persistence;
using Moyai.Mcp.Tools;

namespace Moyai.Mcp.Tests;

public sealed class ProviderMappingToolTests
{
    [Fact]
    public async Task MappingToolAndCliUseSameAuditedSetter()
    {
        string root = Path.Combine(Path.GetTempPath(), "moyai-mapping-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "test.db"), Pooling = false }.ToString());
        try
        {
            await new SqliteDatabaseInitializer(options).InitializeAsync();
            var projects = new ProjectService(new SqliteProjectRepository(options), TimeProvider.System);
            var original = await projects.CreateAsync(new CreateProjectCommand("Neon_SDK"));
            var tools = new MoyaiTools(projects, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            MethodInfo method = typeof(MoyaiTools).GetMethod(nameof(MoyaiTools.ProjectProviderRepositorySet))!;
            Assert.Equal("project_provider_repository_set", method.GetCustomAttribute<McpServerToolAttribute>()!.Name);
            Assert.True(method.GetParameters().Single(parameter => parameter.Name == "providerRepositoryId").IsOptional);
            using JsonDocument schema = JsonDocument.Parse("""
                {"type":"object","properties":{"project":{"type":"string"},"expectedRevision":{"type":"integer"},"providerRepositoryId":{"type":["string","null"]},"clearMapping":{"type":"boolean"}},"required":["project","expectedRevision"]}
                """);
            IReadOnlyDictionary<string, object?> arguments = CliArguments.Convert(
                CliArguments.Parse(["--project", "Neon_SDK", "--expected-revision", "1", "--provider-repository-id", "neonsdk"]), schema.RootElement, "project_provider_repository_set");
            var mapped = await tools.ProjectProviderRepositorySet((string)arguments["project"]!, (long)arguments["expectedRevision"]!, (string)arguments["providerRepositoryId"]!);
            Assert.Equal(original.Id, mapped.Id);
            Assert.Equal("neonsdk", (await tools.ProjectGet("Neon_SDK")).ProviderRepositoryId);
            IReadOnlyDictionary<string, object?> clear = CliArguments.Convert(
                CliArguments.Parse(["--project", "Neon_SDK", "--expected-revision", "2", "--clear-mapping"]), schema.RootElement, "project_provider_repository_set");
            var cleared = await tools.ProjectProviderRepositorySet((string)clear["project"]!, (long)clear["expectedRevision"]!, clearMapping: (bool)clear["clearMapping"]!);
            Assert.Null(cleared.ProviderRepositoryId);
            Assert.Equal(original.Id, cleared.Id);
            await Assert.ThrowsAnyAsync<ArgumentException>(() => tools.ProjectProviderRepositorySet("Neon_SDK", cleared.Revision));
        }
        finally { Directory.Delete(root, true); }
    }
}
