using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class LostResponseVerificationTests
{
    private static readonly string Parent = new('a', 40);
    private static readonly string Child = new('b', 40);
    private static readonly Guid Project = Guid.Parse("474c0456-5e98-43a6-8883-511c912f2a37");
    private static readonly DateTimeOffset Completed = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private const string Message = "test: isolated response loss marker";

    [Fact]
    public void IndependentStateReconcilesUnknownWithoutRewritingOriginalEvidence()
    {
        FixtureEvidence evidence = Evidence();
        FixtureCheck check = FixtureValidation.VerifyLostResponse(evidence, Observation());
        Assert.True(check.Passed);
        Assert.True(evidence.ResultUnknown);
        Assert.False(evidence.ProviderOk);
        Assert.Null(check.ReturnedCommitSha);
        Assert.False(FixtureValidation.Verify(evidence, Observation()).Passed);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("message")]
    [InlineData("count")]
    [InlineData("refs")]
    [InlineData("files")]
    [InlineData("time")]
    public void WrongIndependentObservationCannotClearPending(string field)
    {
        FixtureObservation observation = field switch
        {
            "parent" => Observation() with { ParentHead = Child },
            "message" => Observation() with { Message = "other" },
            "count" => Observation() with { CommitCount = 2 },
            "refs" => Observation() with { Refs = new() { ["refs/heads/develop"] = Parent } },
            "files" => Observation() with { FileSha256 = new() },
            _ => Observation() with { ObservedAtUtc = Completed.AddSeconds(-1) }
        };
        Assert.False(FixtureValidation.VerifyLostResponse(Evidence(), observation).Passed);
    }

    private static FixtureEvidence Evidence() => new(
        new("owner-response-loss-fixture", "Commit", true, Message: Message, ExpectedHead: Parent, ExpectedBranch: "develop",
            ExpectedClean: true, ExpectedChangedPaths: ["README.md"], ExpectedUnchangedRefs: new() { ["refs/heads/main"] = Parent },
            ExpectedFileSha256: new() { ["README.md"] = "fixture-hash" }),
        Project, "fixture", Completed.AddSeconds(-1), Completed, false, "provider_unavailable", true, new(true, null, []));

    private static FixtureObservation Observation() => new(1, "owner-response-loss-fixture", Project, "fixture", Completed.AddSeconds(1),
        "develop", Child, Parent, true, ["README.md"], 1, 0,
        new() { ["refs/heads/main"] = Parent, ["refs/heads/develop"] = Child }, new() { ["README.md"] = "fixture-hash" }, Message);
}
