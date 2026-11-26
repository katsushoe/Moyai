using Moyai.Application.Authentication;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

public sealed partial class McpRepositoryProvider
{
    private RepositoryProviderResult NormalizeProtectionCapabilities(RepositoryProviderResult result)
    {
        if (!result.Ok) return result;
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(result.Output ?? "null") as System.Text.Json.Nodes.JsonObject;
            if (root?["data"] is not System.Text.Json.Nodes.JsonObject data) return result;
            bool Enabled(string name) => _options.ToolPrefix == "github" && data[name] is System.Text.Json.Nodes.JsonValue value
                && value.TryGetValue<bool>(out bool flag) && flag;
            bool read = Enabled("branch_protection_read");
            bool write = Enabled("branch_protection_write");
            data["branch_protection_read"] = read;
            data["branch_protection_write"] = write;
            return result with { Output = root.ToJsonString() };
        }
        catch (System.Text.Json.JsonException) { return result; }
    }

    private static bool IsProtection(RepositoryOperation operation) => operation is RepositoryOperation.BranchProtectionGet
        or RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete;
    private static bool IsProtectionMutation(RepositoryOperation operation) => operation is RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete;

    private RepositoryProviderResult? ValidateBranchProtection(RepositoryProviderRequest request)
    {
        RepositoryProviderResult Reject(string code) => new(false, request.Operation.ContractName(), null, code, code);
        if (_options.ToolPrefix != "github") return Reject("provider_capability_missing");
        if (!request.UseAssertion && !request.UseWindowsOwnerAuthentication || request.ServiceToken is not null) return Reject("auth_scope_denied");
        try { _ = RepositoryProviderContract.Arguments(_options.ToolPrefix, request); }
        catch (ArgumentException) { return Reject("branch_protection_invalid_input"); }
        return null;
    }
}
