using Microsoft.Data.Sqlite;
using Moyai.Application.Deployments;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Domain.Deployments;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Persistence;

public sealed class DeploymentTargetUpdateTests : IAsyncLifetime
{
    private const string ProjectName = "TargetRoundTrip";
    private const string OriginalPath = "/tmp/kelpie-deploy-test";
    private const string CorrectedPath = "/artifacts/ac37.txt";
    private const string ConfigJson = "{\"fixture\":true}";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"moyai-target-{Guid.NewGuid():N}");
    private DeploymentService _service = null!;
    private SqliteDeploymentRepository _deployments = null!;
    private SqliteProjectRepository _projects = null!;
    private Guid _projectId;
    private long _projectRevision;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "moyai.db"),
            Pooling = false,
        }.ToString());
        await new SqliteDatabaseInitializer(options).InitializeAsync();
        _projects = new SqliteProjectRepository(options);
        var project = await new ProjectService(_projects, TimeProvider.System).CreateAsync(
            new CreateProjectCommand(ProjectName, _root, null, "https://github.com/example/target-test.git",
                null, "node", "server", "agent", "test"));
        _projectId = project.Id;
        _projectRevision = project.Revision;
        _deployments = new SqliteDeploymentRepository(options);
        var lifecycle = new LifecycleService(_projects, new SqliteServiceTokenRepository(options), [],
            new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
        _service = new DeploymentService(_projects, new SqliteBuildRepository(options),
            new SqliteReleaseRepository(options), _deployments, lifecycle, TimeProvider.System);
    }

    [Fact]
    public async Task TargetDestinationUpdateAndReverseUpdatePreserveIdentityAndOtherSettings()
    {
        DeploymentTarget original = await UpdateAsync(OriginalPath, 0);

        DeploymentTarget updated = await UpdateAsync(CorrectedPath, original.Revision);
        DeploymentTarget restored = await UpdateAsync(OriginalPath, updated.Revision);

        Assert.Equal(1, original.Revision);
        Assert.Equal(original with
        {
            DestinationPath = CorrectedPath,
            UpdatedAt = updated.UpdatedAt,
            Revision = 2,
        }, updated);
        Assert.Equal(original with { UpdatedAt = restored.UpdatedAt, Revision = 3 }, restored);
        Assert.Equal(restored, await _service.GetTargetAsync(ProjectName));
        Assert.Empty(await _deployments.ListAsync(_projectId));
        Assert.Equal(_projectRevision, (await _projects.GetByNameAsync(ProjectName))!.Revision);
    }

    [Fact]
    public async Task ReverseUpdateWithStaleRevisionLeavesPersistedTargetUnchanged()
    {
        DeploymentTarget original = await UpdateAsync(OriginalPath, 0);
        DeploymentTarget updated = await UpdateAsync(CorrectedPath, original.Revision);

        await Assert.ThrowsAsync<RevisionConflictException>(() => UpdateAsync(OriginalPath, original.Revision));

        Assert.Equal(updated, await _service.GetTargetAsync(ProjectName));
        Assert.Empty(await _deployments.ListAsync(_projectId));
        Assert.Equal(_projectRevision, (await _projects.GetByNameAsync(ProjectName))!.Revision);
    }

    private Task<DeploymentTarget> UpdateAsync(string path, long expectedRevision) =>
        _service.UpdateTargetAsync(ProjectName, "lifecycle-test", "server", path,
            "kelpiessh-lifecycle-test", ConfigJson, expectedRevision, "agent", "test");

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
        return Task.CompletedTask;
    }
}
