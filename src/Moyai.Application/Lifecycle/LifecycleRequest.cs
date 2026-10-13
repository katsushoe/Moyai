namespace Moyai.Application.Lifecycle;

/// <summary>Lifecycle Providerへの標準要求を表します。</summary>
public sealed record LifecycleRequest(
    string Project,
    string SourcePath,
    string? InstallPath,
    LifecycleAction Action,
    string? Version,
    string? ArtifactPath,
    string? Notes,
    string? ServiceToken,
    IReadOnlyList<string>? ArtifactPaths = null,
    long? ProviderReleaseId = null,
    string? TagName = null,
    string? CommitHash = null,
    Guid? ProjectId = null,
    Guid? DeploymentId = null,
    string? KelpieTarget = null,
    string? DestinationPath = null,
    string? ArtifactSha256 = null,
    string? RepositoryUrl = null,
    Func<CancellationToken, Task<bool>>? RevalidateContext = null,
    string? ProviderRepositoryId = null)
{
    /// <summary>Repository Provider登録IDです。未設定時は従来のProject名です。</summary>
    public string RepositoryId => ProviderRepositoryId ?? Project;
}
