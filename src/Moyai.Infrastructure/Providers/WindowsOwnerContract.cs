using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Providers;

/// <summary>明示的な所有ユーザー契約と操作能力を検証します。欠落を互換性へ推測しません。</summary>
internal sealed class WindowsOwnerContract
{
    private static readonly Version MinimumVersion = new(2, 0, 1, 0);
    private static readonly Dictionary<string, string?> Operations = new(StringComparer.Ordinal)
    {
        ["bitbucket_branch_create"] = "branch_create", ["bitbucket_branch_delete"] = "branch_delete",
        ["bitbucket_branch_get"] = null, ["bitbucket_branch_list"] = null, ["bitbucket_fetch"] = null,
        ["bitbucket_force_push_with_lease"] = "force_push_with_lease",
        ["bitbucket_history_rewrite_execute"] = "history_rewrite_execute", ["bitbucket_history_rewrite_preview"] = "history_rewrite_preview",
        ["bitbucket_pr_create"] = null, ["bitbucket_pr_diff"] = null, ["bitbucket_pr_get"] = null, ["bitbucket_pr_list"] = null,
        ["bitbucket_pr_merge"] = "pr_merge", ["bitbucket_pull"] = null, ["bitbucket_push"] = "explicit_push",
        ["bitbucket_repository_commit"] = "repository_commit", ["bitbucket_repository_commit_selected"] = "repository_commit_selected",
        ["bitbucket_repository_diff"] = "repository_diff", ["bitbucket_repository_settings_get"] = "repository_settings_read",
        ["bitbucket_repository_settings_update"] = "repository_settings_update",
        ["bitbucket_repository_stash_apply"] = "repository_stash_apply", ["bitbucket_repository_stash_drop"] = "repository_stash_drop",
        ["bitbucket_repository_stash_list"] = "repository_stash_list", ["bitbucket_repository_stash_push"] = "repository_stash_push",
        ["bitbucket_repository_status"] = null, ["bitbucket_tag_create"] = null, ["bitbucket_tag_delete"] = "tag_delete",
        ["bitbucket_tag_get"] = null, ["bitbucket_tag_list"] = null, ["bitbucket_tag_push"] = "tag_push",
        ["buckettie_branch_switch"] = "branch_switch", ["buckettie_release_create"] = "release_create",
        ["buckettie_release_get"] = "release_get", ["buckettie_release_publish"] = "release_publish", ["buckettie_release_withdraw"] = "release_withdraw",
    };
    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal)
    {
        "get_version", "bitbucket_provider_capabilities", "bitbucket_repository_status", "bitbucket_repository_diff",
        "bitbucket_branch_get", "bitbucket_branch_list", "bitbucket_tag_get", "bitbucket_tag_list",
        "bitbucket_pr_get", "bitbucket_pr_list", "bitbucket_pr_diff", "bitbucket_repository_settings_get",
        "bitbucket_repository_stash_list", "bitbucket_history_rewrite_preview", "buckettie_release_get",
    };
    private readonly JsonElement _data;
    private readonly HashSet<string> _tools;
    private WindowsOwnerContract(JsonElement data, HashSet<string> tools) { _data = data; _tools = tools; }

    internal static bool IsRead(string tool) => Reads.Contains(tool);

    internal static WindowsOwnerContract Parse(string versionJson, string capabilityJson)
    {
        try
        {
            using JsonDocument versionDoc = JsonDocument.Parse(versionJson);
            BuckettieUserHostResolver.ValidateProperties(versionDoc.RootElement);
            JsonElement versionRoot = Data(versionDoc.RootElement);
            string? version = versionRoot.ValueKind == JsonValueKind.String ? versionRoot.GetString()
                : versionRoot.GetProperty("version").GetString();
            if (!Version.TryParse(version, out Version? parsed) || parsed.Revision < 0 || parsed < MinimumVersion) throw Missing();
            using JsonDocument capabilities = JsonDocument.Parse(capabilityJson);
            BuckettieUserHostResolver.ValidateProperties(capabilities.RootElement);
            JsonElement data = Data(capabilities.RootElement);
            if (data.GetProperty("contract_version").GetInt32() != 3) throw Missing();
            JsonElement auth = data.GetProperty("authentication");
            if (auth.GetProperty("provider_id").GetString() != "buckettie"
                || auth.GetProperty("authentication_mode").GetString() != "windows_owner"
                || auth.GetProperty("owner_protocol_version").GetInt32() != 1
                || auth.GetProperty("requires_assertion").ValueKind != JsonValueKind.False
                || auth.GetProperty("project_binding_required").ValueKind != JsonValueKind.False
                || auth.GetProperty("integration_mode").GetString() != "moyai"
                || auth.GetProperty("direct_connection").GetString() != "unrestricted"
                || auth.GetProperty("replay_protection").ValueKind != JsonValueKind.False) throw Missing();
            foreach (string field in new[] { "required_audience", "protocol_version", "algorithm" })
                if (auth.GetProperty(field).ValueKind != JsonValueKind.Null) throw Missing();
            JsonElement scopes = auth.GetProperty("tool_scopes");
            if (scopes.ValueKind != JsonValueKind.Object || scopes.EnumerateObject().Any()) throw Missing();
            var tools = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement tool in auth.GetProperty("supported_tools").EnumerateArray())
            {
                string? name = tool.GetString();
                if (string.IsNullOrWhiteSpace(name) || !tools.Add(name)) throw Missing();
            }
            return new(data.Clone(), tools);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or UserHostConnectionException) { throw Missing(); }
    }

    internal void Require(string tool)
    {
        if (!_tools.Contains(tool) || !Operations.TryGetValue(tool, out string? flag)) throw Missing();
        if (flag is not null && (!_data.TryGetProperty("operations", out JsonElement operations)
            || operations.ValueKind != JsonValueKind.Object || !operations.TryGetProperty(flag, out JsonElement enabled)
            || enabled.ValueKind != JsonValueKind.True)) throw Missing();
    }

    internal bool SupportsRemoteResolution => _data.TryGetProperty("remote_resolution", out JsonElement remote)
        && remote.ValueKind == JsonValueKind.Object && remote.TryGetProperty("version", out JsonElement version)
        && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int number) && number == 1
        && remote.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "repository_url";

    private static JsonElement Data(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) ? data : root;
    private static ProviderAuthenticationException Missing() => new("provider_capability_missing");
}
