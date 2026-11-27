using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

public sealed partial class McpRepositoryProvider
{
    private async Task<RepositoryProviderResult> ExecuteOwnerOnceAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        WindowsOwnerSession? session = null;
        string tool = RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation);
        bool mutation = !(_options.UsesGithubieUserHost ? GithubieOwnerContract.IsRead(tool) : WindowsOwnerContract.IsRead(tool));
        try
        {
            session = await WindowsOwnerSession.ConnectAsync(_options, _httpClientFactory, request.RepositoryId, cancellationToken).ConfigureAwait(false);
            if (request.Operation == RepositoryOperation.ProviderVersion) return session.Version;
            if (request.Operation == RepositoryOperation.ProviderCapabilities) return session.Capabilities;
            if (IsProtection(request.Operation) && !RepositoryProviderContract.SupportsBranchProtection(session.Capabilities.Output, request.Operation))
                return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit hosting protection capability is required.");
            if (request.Operation is RepositoryOperation.PullRequestGet or RepositoryOperation.PullRequestMerge
                && !RepositoryProviderContract.SupportsPullRequestState(session.Capabilities.Output, request.PullRequestMerge is not null))
                return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit PR state capabilities are required.");
            if (request.Operation is RepositoryOperation.PullRequestGet or RepositoryOperation.PullRequestMerge && request.PullRequestMerge is not null)
                session.RequireTool("github_pr_merge");
            if (IsPublication(request.Operation))
            {
                if (!SupportsPublication(session.Capabilities.Output, request.Operation))
                    return new(false, request.Operation.ContractName(), null, "provider_capability_missing", "Explicit publication capabilities required.");
                if (request.Operation == RepositoryOperation.PullRequestPublishCreate) session.RequireTool("github_pr_publish_status");
            }
            var arguments = RepositoryProviderContract.Arguments(_options.ToolPrefix, request, session.SupportsRemoteResolution);
            CallToolResult response = await session.CallAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
            RepositoryProviderResult parsed = RepositoryProviderResponse.Parse(request.Operation, response);
            string? raw = response.StructuredContent?.GetRawText() ?? response.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            bool unknown = parsed.ResultUnknown || mutation && (OwnerOutcomeUnknown(raw, _options.UsesGithubieUserHost)
                || parsed.ErrorCode == "provider_invalid_response" && session.ToolDispatched);
            return parsed with { Ok = parsed.Ok && !unknown, Output = session.Redact(parsed.Output),
                ErrorMessage = session.Redact(parsed.ErrorMessage), ResultUnknown = unknown,
                ErrorCode = unknown && parsed.ErrorCode is null ? "provider_result_unknown" : parsed.ErrorCode };
        }
        catch (ProviderAuthenticationException exception)
        { return new(false, request.Operation.ContractName(), null, exception.Code, exception.Code); }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or ModelContextProtocol.McpException
            or OperationCanceledException or UserHostConnectionException or JsonException)
        {
            bool unknown = mutation && session is { ToolDispatched: true, RejectedBeforeDispatch: false };
            string code = session?.PreDispatchErrorCode ?? (unknown ? "provider_result_unknown"
                : exception is UserHostConnectionException discovery ? discovery.Code
                : exception is HttpRequestException http && IsAuthenticationRejection(http) ? "provider_authentication_rejected" : "provider_unavailable");
            return new(false, request.Operation.ContractName(), null, code,
                unknown ? "Provider operation outcome is unknown; do not automatically retry." : code, unknown);
        }
        finally { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
    }

    internal static bool OwnerOutcomeUnknown(string? output, bool githubie = false)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(output ?? "null");
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object) return false;
            if (error.TryGetProperty("outcome", out JsonElement outcome) && outcome.ValueKind == JsonValueKind.String && outcome.GetString() == "unknown") return true;
            return githubie && error.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.String
                && code.GetString() is "network_error" or "timeout";
        }
        catch (JsonException) { return false; }
    }
}
