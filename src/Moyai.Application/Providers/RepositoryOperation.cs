namespace Moyai.Application.Providers;

/// <summary>Repository Providerへ委譲する標準操作を表します。</summary>
public enum RepositoryOperation
{
    ProviderVersion,
    ProviderCapabilities,
    Status,
    Diff,
    Commit,
    Push,
    Pull,
    BranchList,
    BranchCreate,
    BranchDelete,
    TagCreate,
    TagDelete,
    TagPush,
}
