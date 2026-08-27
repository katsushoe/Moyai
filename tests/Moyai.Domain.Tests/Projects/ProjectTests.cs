using Moyai.Domain.Projects;

namespace Moyai.Domain.Tests.Projects;

public sealed class ProjectTests
{
    [Theory]
    [InlineData("https://github.com/example/repo.git", "github")]
    [InlineData("git@bitbucket.org:example/repo.git", "bitbucket")]
    public void CreateInfersRepositoryProvider(string repositoryUrl, string expectedProvider)
    {
        Project project = Project.Create("Moyai", "source", "install", repositoryUrl, null, "csharp", "local", TimeProvider.System);
        Assert.Equal(expectedProvider, project.RepositoryProvider);
        Assert.Equal("origin", project.GitRemoteName);
        Assert.Equal(1, project.Revision);
    }

    [Fact]
    public void CreateLocalProjectRequiresInstallPath()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("Moyai", "source", null, "https://github.com/example/repo", null, "csharp", "local", TimeProvider.System));
    }

    [Fact]
    public void ArchiveAndRestoreIncrementRevision()
    {
        Project project = Project.Create("Moyai", "source", null, "https://github.com/example/repo", null, "csharp", "server", TimeProvider.System);
        project.Archive(TimeProvider.System);
        Assert.NotNull(project.ArchivedAt);
        project.Restore(TimeProvider.System);
        Assert.Null(project.ArchivedAt);
        Assert.Equal(3, project.Revision);
    }

    [Fact]
    public void UpdateRepositoryInfersProviderAndIncrementsRevision()
    {
        Project project = Project.Create("Moyai", "source", null, "https://github.com/example/repo", null, "csharp", "server", TimeProvider.System);

        project.Update("Moyai", "https://bitbucket.org/example/repo", null, null, null, null, null, "origin", null, TimeProvider.System);

        Assert.Equal("https://bitbucket.org/example/repo", project.RepositoryUrl);
        Assert.Equal("bitbucket", project.RepositoryProvider);
        Assert.Equal(2, project.Revision);
    }
}
