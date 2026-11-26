using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

public sealed partial class McpRepositoryProvider
{
    private static bool IsPublication(RepositoryOperation operation) =>
        operation is RepositoryOperation.PullRequestPublishCreate or RepositoryOperation.PullRequestPublishStatus;

    private RepositoryProviderResult? ValidatePublication(RepositoryProviderRequest request)
    {
        RepositoryProviderResult Reject(string code) => new(false, request.Operation.ContractName(), null, code, code);
        if (_options.ToolPrefix != "github") return Reject("provider_capability_missing");
        if (!request.UseAssertion && !request.UseWindowsOwnerAuthentication || request.ServiceToken is not null) return Reject("auth_scope_denied");
        try
        {
            _ = RepositoryProviderContract.Arguments(_options.ToolPrefix, request);
            if (request.UseAssertion)
            {
                if (_capability is null || _issuer is null) return Reject("authentication_unavailable");
                _capability.Require(new(_capability.ProviderId, request.ProjectId,
                    RepositoryAssertionPolicy.NormalizeRepository(request.RepositoryUrl),
                    RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation),
                    RepositoryAssertionPolicy.Scopes(request.Operation, _capability.ProviderId),
                    request.OperationId ?? throw new ProviderAuthenticationException("auth_project_mismatch")));
            }
        }
        catch (ArgumentException) { return Reject("publication_invalid"); }
        catch (ProviderAuthenticationException exception) { return Reject(exception.Code); }
        return null;
    }

    private static bool SupportsPublication(string? output, RepositoryOperation operation)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(output ?? "null");
            BuckettieUserHostResolver.ValidateProperties(document.RootElement);
            JsonElement root = document.RootElement, data = root.GetProperty("data");
            return root.GetProperty("ok").ValueKind == JsonValueKind.True
                && data.GetProperty("pull_request_publish_status").ValueKind == JsonValueKind.True
                && (operation != RepositoryOperation.PullRequestPublishCreate
                    || data.GetProperty("pull_request_publish_create").ValueKind == JsonValueKind.True);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or UserHostConnectionException) { return false; }
    }
}

public static partial class PublicationResponseParser
{
    public static RepositoryProviderResult Parse(RepositoryOperation operation, CallToolResult result, string? output, string? text)
    {
        bool mutation = operation == RepositoryOperation.PullRequestPublishCreate;
        try
        {
            if (result.StructuredContent is not null && text is not null
                && !JsonNode.DeepEquals(JsonNode.Parse(output!), JsonNode.Parse(text))) throw new JsonException();
            using JsonDocument document = JsonDocument.Parse(output ?? "null");
            JsonElement root = document.RootElement;
            bool ok = root.GetProperty("ok").GetBoolean();
            if (root.GetProperty("operation").GetString() != operation.ContractName() || ok && result.IsError is true) throw new JsonException();
            string? code = ok ? null : root.GetProperty("error").GetProperty("code").GetString();
            bool unknown = !ok && root.GetProperty("error").GetProperty("outcome").GetString() == "unknown";
            return new(ok, operation.ContractName(), output, code, ok ? null : output, unknown);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(false, operation.ContractName(), null, "provider_invalid_response", "Invalid publication response.", mutation); }
    }
}

