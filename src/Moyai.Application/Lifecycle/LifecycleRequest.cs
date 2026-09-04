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
    long? ProviderReleaseId = null);
