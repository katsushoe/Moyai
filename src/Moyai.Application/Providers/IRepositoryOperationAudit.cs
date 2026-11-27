namespace Moyai.Application.Providers;

/// <summary>request本文や資格を含めない、ホスティング操作の監査記録です。</summary>
public sealed record RepositoryOperationAuditEntry(string OperationId, Guid ProjectId, string Provider, string Operation,
    string BranchFingerprint, string Outcome, string Code);

/// <summary>署名方式とは独立した永続操作監査です。</summary>
public interface IRepositoryOperationAudit
{
    Task WriteAsync(RepositoryOperationAuditEntry entry, CancellationToken cancellationToken = default);
}
