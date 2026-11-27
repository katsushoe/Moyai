using System.Security.Cryptography;
using System.Text;

namespace Moyai.Application.Providers;

public sealed partial class ProviderRoutingService
{
    private static bool IsBranchProtection(RepositoryOperation operation) => operation is RepositoryOperation.BranchProtectionGet
        or RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete;

    /// <summary>ブランチ保護を型付きで委譲し、変更のintentと結果を永続監査します。</summary>
    public async Task<BranchProtectionResult> ExecuteBranchProtectionAsync(string projectName, string branch, RepositoryOperation operation,
        BranchProtectionEdit? edit = null, bool createIfMissing = false, CancellationToken cancellationToken = default)
    {
        if (!IsBranchProtection(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        try
        {
            BranchProtectionEdit.ValidateBranch(branch);
            if (operation == RepositoryOperation.BranchProtectionUpdate) (edit ?? throw new ArgumentException("Edit required.")).Validate();
            else if (edit is not null || createIfMissing) throw new ArgumentException("Edit belongs to update only.");
        }
        catch (ArgumentException) { return BranchProtectionResult.Failure(projectName, null, operation, "branch_protection_invalid_input"); }
        var project = await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false);
        if (project is null) return BranchProtectionResult.Failure(projectName, null, operation, "project_not_found");
        string provider = ProviderName(project.RepositoryProvider);
        if (provider is not ("githubie" or "githubbie")) return BranchProtectionResult.Failure(projectName, provider, operation, "provider_capability_missing");
        provider = "githubie";
        RepositoryProviderResult raw = await ExecuteAsync(projectName, operation, branch: branch, protectionEdit: edit,
            createProtectionIfMissing: createIfMissing, cancellationToken: cancellationToken).ConfigureAwait(false);
        return BranchProtectionResult.Parse(projectName, provider, branch, operation, raw);
    }

    private async Task<RepositoryProviderResult> ExecuteProtectionAuditedAsync(RepositoryProviderRequest request, IRepositoryProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            BranchProtectionEdit.ValidateBranch(request.Branch);
            if (request.Operation == RepositoryOperation.BranchProtectionUpdate) (request.ProtectionEdit ?? throw new ArgumentException("Edit required.")).Validate();
            else if (request.ProtectionEdit is not null || request.CreateProtectionIfMissing) throw new ArgumentException("Unexpected edit.");
        }
        catch (ArgumentException) { return Failure(request.Operation, "branch_protection_invalid_input", "Invalid protection input."); }
        var entry = new RepositoryOperationAuditEntry(request.OperationId!, request.ProjectId, "githubie", request.Operation.ContractName(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Branch!))), "pending", "dispatch_planned");
        if (_operationAudit is null || !await TryAuditAsync(entry, cancellationToken).ConfigureAwait(false))
            return Failure(request.Operation, "operation_audit_unavailable", "Operation audit is unavailable; request not dispatched.");
        RepositoryProviderResult raw = await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        BranchProtectionResult result = BranchProtectionResult.Parse(request.Project, "githubie", request.Branch!, request.Operation, raw);
        if (!await TryAuditAsync(entry with { Outcome = result.Ok ? "succeeded" : result.Error!.Outcome,
            Code = result.Ok ? result.Protection!.Changed == true ? "changed" : "unchanged" : result.Error!.Code }, CancellationToken.None).ConfigureAwait(false))
            return new(false, request.Operation.ContractName(), null, "operation_audit_result_unknown", "Final operation audit failed; independently inspect state.", true);
        if (!result.Ok && raw.Ok)
            return new(false, request.Operation.ContractName(), null, result.Error!.Code, "Provider mutation result could not be confirmed.", result.Error.Outcome == "unknown");
        return raw;
    }

    private async Task<bool> TryAuditAsync(RepositoryOperationAuditEntry entry, CancellationToken cancellationToken)
    {
        try { await _operationAudit!.WriteAsync(entry, cancellationToken).ConfigureAwait(false); return true; }
        catch (Exception exception) when (exception is System.Data.Common.DbException or IOException or UnauthorizedAccessException)
        { return false; }
    }
}
