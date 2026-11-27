using System.Text.Json;

namespace Moyai.Infrastructure.Providers;

/// <summary>Githubie schema 1の公開接続情報を厳格に読み取ります。資格の復号は行いません。</summary>
internal sealed record GithubieDiscoveryRecord(Guid ProfileId, string OwnerSid, int SessionId, int ProcessId,
    long ProcessStartUtcTicks, Uri Endpoint, string Generation, string RuntimePath, string RuntimeSha256,
    string ProfileSha256, string ProtectedCredential)
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "schema_version", "provider_id", "profile_id", "owner_sid", "session_id", "process_id",
        "process_start_utc_ticks", "endpoint", "generation", "runtime_path", "runtime_sha256", "profile_sha256", "protected_credential",
    };

    internal static GithubieDiscoveryRecord Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length is < 1 or > 65536) throw Invalid();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            BuckettieUserHostResolver.ValidateProperties(root);
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            JsonProperty[] fields = root.EnumerateObject().ToArray();
            if (fields.Length != Fields.Count || fields.Any(field => !Fields.Contains(field.Name))) throw Invalid();
            if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("provider_id").GetString() != "githubie")
                throw new UserHostConnectionException("discovery_unsupported");
            if (!Guid.TryParse(root.GetProperty("profile_id").GetString(), out Guid profile) || profile == Guid.Empty) throw Invalid();
            string sid = Required(root, "owner_sid");
            int session = root.GetProperty("session_id").GetInt32();
            int pid = root.GetProperty("process_id").GetInt32();
            long ticks = root.GetProperty("process_start_utc_ticks").GetInt64();
            if (session <= 0 || pid <= 0 || ticks <= 0) throw Invalid();
            string endpointValue = Required(root, "endpoint");
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "http"
                || endpoint.Host != "127.0.0.1" || endpoint.Port <= 0 || endpoint.AbsolutePath != "/mcp"
                || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
                || endpointValue != $"http://127.0.0.1:{endpoint.Port}/mcp")
                throw new UserHostConnectionException("discovery_endpoint_rejected");
            string generation = Required(root, "generation");
            if (!Guid.TryParseExact(generation, "N", out _)) throw Invalid();
            string runtime = Required(root, "runtime_path");
            if (!Path.IsPathFullyQualified(runtime)) throw Invalid();
            string runtimeHash = Required(root, "runtime_sha256");
            string profileHash = Required(root, "profile_sha256");
            if (!IsHash(runtimeHash) || !IsHash(profileHash)) throw Invalid();
            string credential = Required(root, "protected_credential");
            byte[] decoded = Convert.FromBase64String(credential);
            try { if (decoded.Length == 0 || Convert.ToBase64String(decoded) != credential) throw Invalid(); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(decoded); }
            return new(profile, sid, session, pid, ticks, endpoint, generation, runtime, runtimeHash, profileHash, credential);
        }
        catch (UserHostConnectionException exception) when (exception.Code == "host_discovery_invalid") { throw Invalid(); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw Invalid(); }
    }

    public override string ToString() => nameof(GithubieDiscoveryRecord);
    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static string Required(JsonElement root, string name) => root.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw Invalid();
    private static UserHostConnectionException Invalid() => new("discovery_invalid");
}
