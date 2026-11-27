using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartReservationTests
{
    [Fact]
    public void StartedCaseCannotBeReplayedAfterProcessReleasesLock()
    {
        string profile = Path.Combine(Path.GetTempPath(), "moyai-restart-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try
        {
            using (RestartReservation.Reserve(profile, "case-one")) { }
            Assert.Throws<IOException>(() => RestartReservation.Reserve(profile, "case-one"));
            using (RestartReservation.Reserve(profile, "case-two")) { }
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void PendingMutationPreventsReservationWithoutConsumingCase()
    {
        string profile = Path.Combine(Path.GetTempPath(), "moyai-restart-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try
        {
            string pending = Path.Combine(profile, "mutation-pending.json");
            File.WriteAllText(pending, "{}");
            Assert.Throws<InvalidOperationException>(() => RestartReservation.Reserve(profile, "case-one"));
            File.Delete(pending);
            using (RestartReservation.Reserve(profile, "case-one")) { }
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void ConcurrentConsumerIsRejected()
    {
        string profile = Path.Combine(Path.GetTempPath(), "moyai-restart-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try
        {
            using var first = RestartReservation.Reserve(profile, "case-one");
            Assert.Throws<IOException>(() => RestartReservation.Reserve(profile, "case-two"));
        }
        finally { Directory.Delete(profile, true); }
    }
}
