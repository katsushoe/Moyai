using Moyai.Application.Authentication;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Providers;

public sealed partial class McpRepositoryProvider
{
    private RepositoryProviderResult? ValidatePullRequestState(RepositoryProviderRequest request)
    {
        RepositoryProviderResult Reject(string code) => new(false, request.Operation.ContractName(), null, code, code);
        if (_options.ToolPrefix != "github") return Reject("provider_capability_missing");
        if (request.UseWindowsOwnerAuthentication != _options.UsesWindowsOwnerAuthentication
            || request.UseWindowsOwnerAuthentication && (request.UseAssertion || request.ServiceToken is not null)) return Reject("provider_authentication_mode_mismatch");
        if (!request.UseAssertion && !request.UseWindowsOwnerAuthentication) return Reject("auth_scope_denied");
        if (request.ServiceToken is not null) return Reject("auth_scope_denied");
        try
        {
            if (request.PullRequestRead is not null && request.PullRequestMerge is not null
                && request.PullRequestRead.PullRequestId != request.PullRequestMerge.PullRequestId) return Reject("pull_request_invalid");
            if (request.Operation == RepositoryOperation.PullRequestMerge)
            {
                if (request.PullRequestMerge is null) return Reject("pull_request_invalid");
                request.PullRequestMerge.Validate();
                if (request.PullRequestMerge.Strategy == "fast_forward") return Reject("merge_strategy_invalid");
            }
            _ = RepositoryProviderContract.Arguments(_options.ToolPrefix, request);
            if (request.UseAssertion)
            {
                if (_issuer is null || _capability is null) return Reject("authentication_unavailable");
                _capability.Require(new(_capability.ProviderId, request.ProjectId, RepositoryAssertionPolicy.NormalizeRepository(request.RepositoryUrl),
                    RepositoryProviderContract.ToolName(_options.ToolPrefix, request.Operation),
                    [RepositoryAssertionPolicy.Scope(request.Operation, _capability.ProviderId)], request.OperationId ?? throw new ProviderAuthenticationException("auth_project_mismatch")));
            }
        }
        catch (ArgumentException) { return Reject("pull_request_invalid"); }
        catch (ProviderAuthenticationException exception) { return Reject(exception.Code); }
        return null;
    }

    private async Task<RepositoryProviderResult> ExecutePullRequestMergeAsync(RepositoryProviderRequest request, CancellationToken cancellationToken)
    {
        RepositoryProviderResult? invalid = ValidatePullRequestState(request);
        if (invalid is not null) return invalid;
        PullRequestMergeInput input = request.PullRequestMerge!;
        RepositoryProviderRequest read = request with { Operation = RepositoryOperation.PullRequestGet,
            PullRequestRead = new(input.PullRequestId), OperationId = Guid.NewGuid().ToString("N") };
        RepositoryProviderResult before = await ExecuteOnceAsync(read, cancellationToken).ConfigureAwait(false);
        if (!before.Ok) return before with { Operation = request.Operation.ContractName(), ResultUnknown = false,
            ErrorMessage = System.Text.Json.JsonSerializer.Serialize(new { error = new { code = before.ErrorCode,
                outcome = "not_executed", provider_detail = before.ErrorMessage } }) };
        PullRequestStateResult parsed = PullRequestStateResult.Parse(request.Project, "githubie", input.PullRequestId, before);
        if (!parsed.Ok) return new(false, request.Operation.ContractName(), null, parsed.Error!.Code, parsed.Error.Code);
        PullRequestStateInfo snapshot = parsed.PullRequest!;
        if (!string.Equals(snapshot.SourceCommit, input.ExpectedSourceCommit, StringComparison.OrdinalIgnoreCase))
            return new(false, request.Operation.ContractName(), null, "expected_source_commit_mismatch", "Authenticated source commit differs from the approved SHA.");
        if (snapshot.State == "merged") return before with { Operation = request.Operation.ContractName() };
        if (snapshot.State != "open") return new(false, request.Operation.ContractName(), null, "pull_request_not_open", "PR is not open.");
        if (snapshot.Draft is not false) return new(false, request.Operation.ContractName(), null,
            snapshot.Draft is true ? "pull_request_draft" : "provider_invalid_response", "An explicit non-draft PR is required.");
        if (cancellationToken.IsCancellationRequested)
            return new(false, request.Operation.ContractName(), null, "operation_cancelled", "Cancelled before merge dispatch.");
        RepositoryProviderResult result = await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Ok) return result;
        PullRequestStateResult final = PullRequestStateResult.Parse(request.Project, "githubie", input.PullRequestId, result);
        if (!final.Ok || final.PullRequest is not { State: "merged" } merged
            || !string.Equals(merged.SourceCommit, input.ExpectedSourceCommit, StringComparison.OrdinalIgnoreCase)
            || merged.SourceBranch != snapshot.SourceBranch || merged.DestinationBranch != snapshot.DestinationBranch)
            return new(false, request.Operation.ContractName(), null, "provider_invalid_response", result.Output ?? "Merge result requires independent authenticated PR reconciliation; do not replay.", true);
        return result;
    }
}
