namespace Moyai.Application.Providers;

/// <summary>Repository Providerへ委譲する標準操作を表します。</summary>
public enum RepositoryOperation
{
    Status,
    Diff,
    Commit,
    Push,
    Pull,
}
