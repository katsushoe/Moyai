using Moyai.Domain.Releases;

namespace Moyai.Domain.Tests.Releases;

public sealed class ReleaseContentTests
{
    [Theory]
    [InlineData("includes")]
    [InlineData("FIXES")]
    [InlineData("implements")]
    [InlineData("resolves")]
    public void CreateWorkItemAcceptsSupportedRelations(string relation)
    {
        ReleaseWorkItem item = ReleaseWorkItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), relation, TimeProvider.System);

        Assert.Equal(relation.ToLowerInvariant(), item.Relation);
    }

    [Fact]
    public void CreateWorkItemRejectsUnsupportedRelation() =>
        Assert.Throws<ArgumentException>(() => ReleaseWorkItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "mentions", TimeProvider.System));

    [Fact]
    public void CreateArtifactNormalizesSupportedMetadata()
    {
        ReleaseArtifact artifact = ReleaseArtifact.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Windows MSI", "INSTALLER", "WINDOWS", "X64", "Moyai.msi", null, "https://example.test/Moyai.msi", 42, "abc", null, null, TimeProvider.System);

        Assert.Equal("installer", artifact.ArtifactType);
        Assert.Equal("windows", artifact.Platform);
        Assert.Equal("x64", artifact.Architecture);
        Assert.Equal(42, artifact.FileSize);
    }

    [Theory]
    [InlineData("unknown", "windows", "x64")]
    [InlineData("installer", "unknown", "x64")]
    [InlineData("installer", "windows", "unknown")]
    public void CreateArtifactRejectsUnsupportedMetadata(string type, string platform, string architecture) =>
        Assert.Throws<ArgumentException>(() => ReleaseArtifact.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Artifact", type, platform, architecture, "artifact.bin", null, null, null, null, null, null, TimeProvider.System));
}
