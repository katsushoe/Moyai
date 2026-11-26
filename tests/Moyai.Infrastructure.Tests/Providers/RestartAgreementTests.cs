using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartAgreementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidPinnedAgreementReturnsExpectedIdentity()
    {
        using var fixture = new AgreementFixture();
        RestartAgreement agreement = fixture.Load();
        Assert.Equal("fixture", agreement.RepositoryId);
        Assert.Equal(Now.AddMinutes(10), agreement.Expires);
    }

    [Fact]
    public void WrongRuntimeIsRejected()
    {
        using var fixture = new AgreementFixture();
        RestartAgreement agreement = fixture.Load();
        agreement.RequireRuntime(".NET 8.0.31");
        Assert.Throws<InvalidOperationException>(() => agreement.RequireRuntime(".NET 8.0.30"));
    }

    [Fact]
    public void ChangedAgreementDoesNotMatchPin()
    {
        using var fixture = new AgreementFixture();
        File.WriteAllText(fixture.Path, "{}");
        Assert.Throws<InvalidOperationException>(fixture.Load);
    }

    [Theory]
    [InlineData("case_id", "different")]
    [InlineData("operation", "mutation")]
    [InlineData("counterpart_message_id", "missing")]
    [InlineData("consumer_profile", "relative")]
    [InlineData("consumer_console_name", "../other.exe")]
    [InlineData("consumer_runtime_version", "9.0.0")]
    public void InvalidIdentityAndPathsAreRejected(string field, string replacement)
    {
        using var fixture = new AgreementFixture();
        fixture.Data[field] = replacement;
        fixture.Save();
        Assert.Throws<InvalidOperationException>(fixture.Load);
    }

    [Fact]
    public void ExpiredOrTooShortWindowIsRejected()
    {
        using var fixture = new AgreementFixture();
        fixture.Data["expires_at_utc"] = Now.AddSeconds(65).ToString("O");
        fixture.Save();
        Assert.Throws<InvalidOperationException>(fixture.Load);
    }

    private sealed class AgreementFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moyai-agreement-test-" + Guid.NewGuid().ToString("N"));
        private string _hash = "";
        public string Path => System.IO.Path.Combine(_directory, "agreement.json");
        public JsonObject Data { get; }
        public AgreementFixture()
        {
            Directory.CreateDirectory(_directory);
            Data = new JsonObject
            {
                ["operation"] = "owner_restart_read_only", ["case_id"] = RestartAgreement.CaseId,
                ["counterpart_message_id"] = new string('a', 32), ["profile_id"] = new string('b', 64),
                ["consumer_manifest_sha256"] = new string('a', 64), ["consumer_console_sha256"] = new string('b', 64),
                ["consumer_manifest_files_count"] = 2, ["consumer_console_name"] = "candidate.exe",
                ["consumer_root"] = _directory, ["consumer_manifest"] = Path, ["consumer_profile"] = _directory,
                ["consumer_runtime_version"] = "8.0.31",
                ["provider_ready"] = Path, ["phase_a_lifecycle"] = Path, ["project_uuid"] = Guid.NewGuid().ToString(),
                ["repository_id"] = "fixture", ["starts_at_utc"] = Now.AddMinutes(-1).ToString("O"),
                ["expires_at_utc"] = Now.AddMinutes(10).ToString("O")
            };
            Save();
        }
        public void Save() { File.WriteAllText(Path, Data.ToJsonString()); _hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path))); }
        public RestartAgreement Load() => RestartAgreement.Load(Path, _hash, Now);
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
