namespace Moyai.Application.Providers;

/// <summary>Repository Providerへの内部要求を表します。</summary>
public sealed record RepositoryProviderRequest(
    string Project,
    string SourcePath,
    string RepositoryUrl,
    string? RemoteName,
    RepositoryOperation Operation,
    string? Message,
    [property: System.Text.Json.Serialization.JsonIgnore] string? ServiceToken,
    string? Branch = null,
    string? Tag = null,
    string? DefaultBranch = null,
    string? GitUserName = null,
    string? GitUserEmail = null,
    string? BranchSource = null,
    Guid ProjectId = default,
    string? OperationId = null,
    bool UseAssertion = false,
    string[]? Paths = null,
    string? StashId = null,
    string? ExpectedHead = null,
    string? Patch = null)
{
    public override string ToString() => $"Repository request: {Operation}";
}
