using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Infrastructure.Providers;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartReadyValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Project = Guid.Parse("474c0456-5e98-43a6-8883-511c912f2a37");
    private static readonly RestartCandidatePins Pins = new("root", "manifest", new string('A', 64), 2, "candidate.exe", new string('B', 64));

    [Fact]
    public void MatchingReadyProvidesGeneration()
    {
        using JsonDocument json = JsonDocument.Parse(Ready().ToJsonString());
        RestartReadyFields ready = RestartReadyValidator.Validate(json.RootElement, Project, "fixture", "owner_restart_phase_a", Pins, Now);
        Assert.Equal(321, ready.ProcessId);
        Assert.Equal(123456, ready.ProcessTicks);
    }

    [Theory]
    [InlineData("status", "owner_restart_phase_b")]
    [InlineData("provider_id", "githubie")]
    [InlineData("repository_id", "other")]
    [InlineData("authentication_mode", "assertion")]
    [InlineData("version", "2.0.0.0")]
    [InlineData("consumer_manifest_sha256", "different")]
    [InlineData("consumer_console_sha256", "different")]
    [InlineData("profile_id", "different")]
    public void MismatchedIdentityOrPinsAreRejected(string field, string replacement)
    {
        JsonObject data = Ready();
        data[field] = replacement;
        using JsonDocument json = JsonDocument.Parse(data.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => RestartReadyValidator.Validate(json.RootElement, Project, "fixture", "owner_restart_phase_a", Pins, Now));
    }

    [Fact]
    public void TooShortReadyWindowIsRejected()
    {
        JsonObject data = Ready();
        data["expires_at_utc"] = Now.AddSeconds(65).ToString("O");
        using JsonDocument json = JsonDocument.Parse(data.ToJsonString());
        Assert.Throws<InvalidDataException>(() => RestartReadyValidator.Validate(json.RootElement, Project, "fixture", "owner_restart_phase_a", Pins, Now));
    }

    [Fact]
    public void MissingOrWrongManifestCountIsRejected()
    {
        JsonObject data = Ready();
        data["consumer_manifest_files_matched"] = 1;
        using JsonDocument json = JsonDocument.Parse(data.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => RestartReadyValidator.Validate(json.RootElement, Project, "fixture", "owner_restart_phase_a", Pins, Now));
    }

    private static JsonObject Ready()
    {
        string runtime = Path.GetFullPath(Path.GetTempPath());
        return new JsonObject
        {
            ["ready"] = true, ["provider_started"] = true, ["status"] = "owner_restart_phase_a",
            ["provider_id"] = "buckettie", ["authentication_mode"] = "windows_owner", ["owner_protocol_version"] = 1,
            ["project_uuid"] = Project.ToString(), ["repository_id"] = "fixture", ["version"] = "2.0.1.0",
            ["runtime"] = runtime, ["profile_id"] = BuckettieUserHostResolver.ProfileId(runtime),
            ["consumer_manifest_sha256"] = Pins.ManifestSha256, ["consumer_console_sha256"] = Pins.ConsoleSha256,
            ["consumer_manifest_files_matched"] = Pins.FileCount,
            ["ready_at_utc"] = Now.AddSeconds(-1).ToString("O"), ["expires_at_utc"] = Now.AddMinutes(10).ToString("O"),
            ["generation"] = new JsonObject { ["process_id"] = 321, ["process_start_utc_ticks"] = 123456 }
        };
    }
}
