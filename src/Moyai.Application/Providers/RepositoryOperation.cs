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

/// <summary>Repository操作のProvider契約名を提供します。</summary>
public static class RepositoryOperationNames
{
    /// <summary>Repository操作をProvider契約名へ変換します。</summary>
    public static string ContractName(this RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.ProviderVersion => "provider_version",
        RepositoryOperation.ProviderCapabilities => "provider_capabilities",
        RepositoryOperation.Status => "repository_status",
        RepositoryOperation.Diff => "repository_diff",
        RepositoryOperation.Commit => "repository_commit",
        RepositoryOperation.Push => "push",
        RepositoryOperation.Pull => "pull",
        RepositoryOperation.BranchList => "branch_list",
        RepositoryOperation.BranchCreate => "branch_create",
        RepositoryOperation.BranchDelete => "branch_delete",
        RepositoryOperation.TagCreate => "tag_create",
        RepositoryOperation.TagDelete => "tag_delete",
        RepositoryOperation.TagPush => "tag_push",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
