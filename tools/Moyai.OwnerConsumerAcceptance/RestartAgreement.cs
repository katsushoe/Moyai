using System.Security.Cryptography;
using System.Text.Json;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Reads a pinned, non-secret agreement for one bounded restart case.</summary>
internal sealed record RestartAgreement(string Profile, string ReadyPath, string PhaseAPath, string ProviderProfileId,
    Guid ProjectId, string RepositoryId, DateTimeOffset Expires, string ConsumerRuntimeVersion, RestartCandidatePins Candidate)
{
    internal const string CaseId = "owner-restart-consumer-20261028-01";
    internal void RequireRuntime(string description)
    {
        if (!string.Equals(description, ".NET " + ConsumerRuntimeVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Consumer runtime does not match the agreement.");
    }
    internal static RestartAgreement Load(string path, string expectedHash, DateTimeOffset now)
    {
        if (!Path.IsPathFullyQualified(path) || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Invalid agreement pin.");
        byte[] bytes = File.ReadAllBytes(path);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
            throw new InvalidOperationException("Agreement hash mismatch.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        BuckettieUserHostResolver.ValidateProperties(root);
        string Required(string property) => root.GetProperty(property).GetString() ?? throw new InvalidOperationException("Missing agreement field.");
        string Absolute(string property)
        {
            string value = Required(property);
            if (!Path.IsPathFullyQualified(value)) throw new InvalidOperationException("Agreement path must be absolute.");
            return Path.GetFullPath(value);
        }
        string counterpart = Required("counterpart_message_id");
        string profileId = Required("profile_id");
        string consoleName = Required("consumer_console_name");
        string manifestHash = Required("consumer_manifest_sha256");
        string consoleHash = Required("consumer_console_sha256");
        string repo = Required("repository_id");
        string runtimeVersion = Required("consumer_runtime_version");
        int count = root.GetProperty("consumer_manifest_files_count").GetInt32();
        DateTimeOffset starts = root.GetProperty("starts_at_utc").GetDateTimeOffset();
        DateTimeOffset expires = root.GetProperty("expires_at_utc").GetDateTimeOffset();
        if (Required("operation") != "owner_restart_read_only" || Required("case_id") != CaseId
            || counterpart.Length != 32 || !counterpart.All(Uri.IsHexDigit) || profileId.Length != 64 || !profileId.All(Uri.IsHexDigit)
            || manifestHash.Length != 64 || !manifestHash.All(Uri.IsHexDigit) || consoleHash.Length != 64 || !consoleHash.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(consoleName) || Path.GetFileName(consoleName) != consoleName
            || !Version.TryParse(runtimeVersion, out Version? parsedRuntime) || parsedRuntime.Major != 8 || parsedRuntime.Minor != 0 || parsedRuntime.Build < 0 || parsedRuntime.Revision >= 0
            || count is <= 0 or > 500 || expires <= starts || expires - starts > TimeSpan.FromMinutes(30)
            || now < starts || expires <= now.AddSeconds(65))
            throw new InvalidOperationException("Invalid or expired restart agreement.");
        Guid project = root.GetProperty("project_uuid").GetGuid();
        if (project == Guid.Empty) throw new InvalidOperationException("Missing project identity.");
        var pins = new RestartCandidatePins(Absolute("consumer_root"), Absolute("consumer_manifest"), manifestHash, count, consoleName, consoleHash);
        return new(Absolute("consumer_profile"), Absolute("provider_ready"), Absolute("phase_a_lifecycle"), profileId, project, repo, expires, runtimeVersion, pins);
    }
}
