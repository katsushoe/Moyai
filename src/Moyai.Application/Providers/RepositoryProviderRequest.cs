namespace Moyai.Application.Providers;

/// <summary>Repository Providerへの内部要求を表します。</summary>
public sealed record RepositoryProviderRequest(
    string Project,
    string SourcePath,
    string RepositoryUrl,
    string RemoteName,
    RepositoryOperation Operation,
    string? Message,
    string? ServiceToken);
