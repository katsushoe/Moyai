using System.Text.Json;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

internal sealed record RestartCandidatePins(string Root, string Manifest, string ManifestSha256, int FileCount, string ConsoleName, string ConsoleSha256);
internal sealed record RestartReadyFields(string Runtime, int ProcessId, long ProcessTicks, DateTimeOffset Expires);

internal static class RestartReadyValidator
{
    internal static RestartReadyFields Validate(JsonElement root, Guid projectId, string repositoryId, string status,
        RestartCandidatePins pins, DateTimeOffset now)
    {
        BuckettieUserHostResolver.ValidateProperties(root);
        FixtureValidation.RequireWindow(root, now);
        string runtime = root.GetProperty("runtime").GetString() ?? throw new InvalidOperationException("Missing runtime.");
        if (root.GetProperty("status").GetString() != status || !root.GetProperty("provider_started").GetBoolean()
            || root.GetProperty("provider_id").GetString() != "buckettie" || root.GetProperty("authentication_mode").GetString() != "windows_owner"
            || root.GetProperty("owner_protocol_version").GetInt32() != 1 || root.GetProperty("project_uuid").GetGuid() != projectId
            || root.GetProperty("repository_id").GetString() != repositoryId || !Path.IsPathFullyQualified(runtime)
            || !Version.TryParse(root.GetProperty("version").GetString(), out Version? version) || version < new Version(2, 0, 1, 0)
            || !string.Equals(root.GetProperty("consumer_manifest_sha256").GetString(), pins.ManifestSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(root.GetProperty("consumer_console_sha256").GetString(), pins.ConsoleSha256, StringComparison.OrdinalIgnoreCase)
            || root.GetProperty("consumer_manifest_files_matched").GetInt32() != pins.FileCount
            || root.GetProperty("profile_id").GetString() != BuckettieUserHostResolver.ProfileId(runtime))
            throw new InvalidOperationException("Readiness identity mismatch.");
        JsonElement generation = root.GetProperty("generation");
        int pid = generation.GetProperty("process_id").GetInt32();
        long ticks = generation.GetProperty("process_start_utc_ticks").GetInt64();
        if (pid <= 0 || ticks <= 0) throw new InvalidOperationException("Invalid generation.");
        return new(runtime, pid, ticks, root.GetProperty("expires_at_utc").GetDateTimeOffset());
    }
}
