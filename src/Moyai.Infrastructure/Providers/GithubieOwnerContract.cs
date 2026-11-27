using System.Text.Json;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Providers;

/// <summary>Githubieの所有ユーザー契約と操作能力を明示的に検証します。</summary>
internal sealed class GithubieOwnerContract
{
    private static readonly Dictionary<string, string?> Operations = new(StringComparer.Ordinal)
    {
        ["github_repository_status"] = null, ["github_repository_diff"] = "repository_diff",
        ["github_repository_commit"] = "repository_commit", ["github_repository_commit_selected"] = "repository_commit_selected",
        ["github_repository_settings_get"] = "repository_settings_read", ["github_repository_settings_update"] = "repository_settings_update",
        ["github_repository_stash_push"] = "repository_stash_push", ["github_repository_stash_list"] = "repository_stash_list",
        ["github_repository_stash_apply"] = "repository_stash_apply", ["github_repository_stash_drop"] = "repository_stash_drop",
        ["github_branch_create"] = "branch_create", ["github_branch_delete"] = "branch_delete",
        ["github_branch_switch"] = "branch_switch", ["github_local_branch_create"] = "local_branch_create",
        ["github_branch_list"] = "branch_list", ["github_branch_get"] = null,
        ["github_branch_protection_get"] = "branch_protection_read",
        ["github_branch_protection_update"] = "branch_protection_write",
        ["github_branch_protection_delete"] = "branch_protection_write",
        ["github_tag_create"] = "tag_create", ["github_tag_delete"] = "tag_delete",
        ["github_tag_push"] = "tag_push", ["github_tag_list"] = null, ["github_tag_get"] = null,
        ["github_pr_create"] = "pr_create", ["github_pr_list"] = null, ["github_pr_diff"] = null,
        ["github_pr_publish_create"] = "pull_request_publish_create", ["github_pr_publish_status"] = "pull_request_publish_status",
        ["github_pr_get"] = "pr_get", ["github_pr_merge"] = "pr_merge",
        ["github_fetch"] = null, ["github_pull"] = null, ["github_push"] = null,
        ["github_release_create"] = null, ["github_release_get"] = null,
        ["github_release_publish"] = null, ["github_release_withdraw"] = null, ["github_release_update"] = null,
        ["github_release_list"] = null,
    };

    private readonly JsonElement _data;
    private readonly HashSet<string> _ownerOperations;
    private readonly HashSet<string> _availableTools;

    private GithubieOwnerContract(JsonElement data, HashSet<string> ownerOperations, HashSet<string> availableTools)
    { _data = data; _ownerOperations = ownerOperations; _availableTools = availableTools; }

    internal static GithubieOwnerContract Parse(string versionJson, string capabilitiesJson, IEnumerable<string> availableTools)
    {
        try
        {
            using JsonDocument versionDocument = JsonDocument.Parse(versionJson);
            BuckettieUserHostResolver.ValidateProperties(versionDocument.RootElement);
            JsonElement versionData = Data(versionDocument.RootElement);
            string? version = versionData.ValueKind == JsonValueKind.String ? versionData.GetString() : versionData.GetProperty("version").GetString();
            if (!Version.TryParse(version, out Version? parsed) || parsed.Revision < 0 || parsed < new Version(2, 1, 0, 0)) throw Missing();
            using JsonDocument capabilityDocument = JsonDocument.Parse(capabilitiesJson);
            BuckettieUserHostResolver.ValidateProperties(capabilityDocument.RootElement);
            JsonElement data = Data(capabilityDocument.RootElement);
            JsonElement authentication = data.GetProperty("authentication");
            JsonElement owner = authentication.GetProperty("owner_authentication");
            if (authentication.GetProperty("integration_mode").GetString() != "windows_owner"
                || authentication.GetProperty("direct_connection").GetString() != "unrestricted"
                || owner.GetProperty("version").GetInt32() != 1
                || owner.GetProperty("mode").GetString() != "windows_owner"
                || owner.GetProperty("transport").GetString() != "user_host_schema1"
                || owner.GetProperty("principal").GetString() != "windows_owner"
                || owner.GetProperty("assertion_required").ValueKind != JsonValueKind.False
                || owner.GetProperty("project_binding_required").ValueKind != JsonValueKind.False
                || owner.GetProperty("legacy_token_required").ValueKind != JsonValueKind.False
                || owner.GetProperty("credential_header").GetString() != "X-Githubie-Session"
                || owner.GetProperty("generation_header").GetString() != "X-Githubie-Generation") throw Missing();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in owner.GetProperty("operations").EnumerateArray())
            {
                string? name = item.GetString();
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name)) throw Missing();
            }
            return new(data.Clone(), names, new HashSet<string>(availableTools, StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or UserHostConnectionException) { throw Missing(); }
    }

    internal void Require(string tool)
    {
        if (!_ownerOperations.Contains(tool) || !_availableTools.Contains(tool) || !Operations.TryGetValue(tool, out string? flag)) throw Missing();
        if (flag is not null && (!_data.TryGetProperty(flag, out JsonElement enabled) || enabled.ValueKind != JsonValueKind.True)) throw Missing();
    }

    internal bool SupportsRemoteResolution => _data.TryGetProperty("remote_resolution", out JsonElement remote)
        && remote.ValueKind == JsonValueKind.Object && remote.TryGetProperty("version", out JsonElement version)
        && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int number) && number == 1
        && remote.TryGetProperty("mode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "repository_url";

    internal static bool IsRead(string tool) => tool is "get_version" or "github_provider_capabilities" or "github_repository_status"
        or "github_repository_diff" or "github_repository_settings_get" or "github_repository_stash_list" or "github_branch_get"
        or "github_branch_list" or "github_tag_get" or "github_tag_list" or "github_pr_get" or "github_pr_list" or "github_pr_diff"
        or "github_pr_publish_status" or "github_release_get" or "github_release_list" or "github_branch_protection_get";

    private static JsonElement Data(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) ? data : root;
    private static ProviderAuthenticationException Missing() => new("provider_capability_missing");
}
