using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Domain.Authentication;
using Moyai.Domain.Projects;

namespace Moyai.Application.Providers;

/// <summary>Project設定に基づきRepository操作をProviderへ安全に委譲します。</summary>
public sealed partial class ProviderRoutingService
{
    private readonly IProjectRepository _projects;
    private readonly IServiceTokenRepository _tokens;
    private readonly Dictionary<string, IRepositoryProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly RepositoryAuthentication _authentication;
    private readonly IRepositoryOperationAudit? _operationAudit;

    public ProviderRoutingService(IProjectRepository projects, IServiceTokenRepository tokens, IEnumerable<IRepositoryProvider> providers, TimeProvider timeProvider, RepositoryAuthentication? authentication = null, IRepositoryOperationAudit? operationAudit = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _projects = projects;
        _tokens = tokens;
        _providers = providers.ToDictionary(static provider => provider.Name, StringComparer.Ordinal);
        _timeProvider = timeProvider;
        _authentication = authentication ?? new RepositoryAuthentication();
        _operationAudit = operationAudit;
    }

    /// <summary>Projectに設定されたProviderへ標準Repository操作を委譲します。</summary>
    public async Task<RepositoryProviderResult> ExecuteAsync(string projectName, RepositoryOperation operation, string? message = null, string? branch = null, string? tag = null, string? source = null, string[]? paths = null, string? stashId = null, string? expectedHead = null, string? patch = null, RepositorySettingsChanges? settingsChanges = null, PullRequestInput? pullRequest = null, PullRequestReadInput? pullRequestRead = null, PullRequestMergeInput? pullRequestMerge = null, BranchProtectionEdit? protectionEdit = null, bool createProtectionIfMissing = false, PullRequestPublicationInput? publication = null, string? publicationOperationId = null, CancellationToken cancellationToken = default)
    {
        Project project = await _projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
        project.RequireConfiguration("sourcePath", "repositoryUrl", "repositoryProvider");
        string providerName = ProviderName(project.RepositoryProvider);
        bool publicationOperation = operation is RepositoryOperation.PullRequestPublishCreate or RepositoryOperation.PullRequestPublishStatus;
        if (publicationOperation && providerName is not ("githubie" or "githubbie"))
            return Failure(operation, "provider_capability_missing", "Publication is only agreed for Githubie.");
        bool protection = IsBranchProtection(operation);
        if (protection && providerName is not ("githubie" or "githubbie")) return Failure(operation, "provider_capability_missing", "Hosting protection contract is not enabled for this Provider.");
        bool settingsOperation = operation is RepositoryOperation.SettingsGet or RepositoryOperation.SettingsUpdate;
        if (settingsOperation && providerName is not ("githubie" or "githubbie" or "buckettie")) return Failure(operation, "provider_capability_missing", "Settings contract is not confirmed for this Provider.");
        if (operation == RepositoryOperation.SettingsUpdate && settingsChanges is null) return Failure(operation, "repository_settings_invalid", "Settings changes are required.");
        if (operation == RepositoryOperation.SettingsUpdate && providerName is not ("githubie" or "githubbie")
            && settingsChanges!.Values.Keys.Any(static name => name is "local_commit_branches" or "pr_publication_source_branches" or "pull_request_routes"))
            return Failure(operation, "repository_settings_invalid", "Githubie-specific settings are not agreed for this Provider.");
        if (!_providers.TryGetValue(providerName, out IRepositoryProvider? provider))
        {
            return Failure(operation, "provider_unavailable", $"Repository provider '{providerName}' is unavailable. Use runtime_diagnostics for this Project to inspect loaded Providers and repository roles.");
        }

        string? tokenValue = null;
        bool ownerAuthentication = provider.UsesWindowsOwnerAuthentication;
        bool legacy;
        try { legacy = !ownerAuthentication && _authentication.UseLegacy(_timeProvider); }
        catch (ProviderAuthenticationException exception) { return Failure(operation, exception.Code, exception.Code); }
        if (publicationOperation && legacy) return Failure(operation, "auth_scope_denied", "Publication requires an authenticated route.");
        if (settingsOperation && legacy) return Failure(operation, "auth_scope_denied", "Settings operations require Project-bound assertions.");
        if (protection && legacy) return Failure(operation, "auth_scope_denied", "Hosting protection requires an authenticated Provider route.");
        if (operation == RepositoryOperation.BranchSwitch && legacy) return Failure(operation, "auth_scope_denied", "Branch switching requires Project-bound assertions.");
        if (operation == RepositoryOperation.BranchCreateLocal && legacy) return Failure(operation, "auth_scope_denied", "Local branch creation requires Project-bound assertions.");
        if (operation == RepositoryOperation.PullRequestCreate && legacy) return Failure(operation, "auth_scope_denied", "PR creation requires Project-bound assertions.");
        if (operation is RepositoryOperation.PullRequestGet or RepositoryOperation.PullRequestMerge && legacy)
            return Failure(operation, "auth_scope_denied", "PR state operations require an authenticated Provider route.");
        if (legacy && IsMutation(operation))
        {
            ServiceToken? token = await _tokens.FindByAudienceAsync(providerName, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return Failure(operation, "invalid_service_token", $"An active service token for '{providerName}' is required.");
            }
            if (token.ExpiresAt is not null && token.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                return Failure(operation, "service_token_expired", $"The service token for '{providerName}' has expired.");
            }
            if (!token.Scopes.Contains("repository.write"))
            {
                return Failure(operation, "service_token_scope_missing", $"The service token for '{providerName}' lacks repository.write scope.");
            }
            tokenValue = token.Token;
        }

        ValidateArguments(operation, message, branch, tag, source, paths, stashId, expectedHead, patch);
        if (operation == RepositoryOperation.PullRequestCreate)
        {
            if (pullRequest is null) return Failure(operation, "pull_request_invalid", "PR input is required.");
            try { pullRequest.Validate(); }
            catch (ArgumentException) { return Failure(operation, "pull_request_invalid", "PR input is invalid."); }
        }
        var request = new RepositoryProviderRequest(project.Name, project.SourcePath, project.RepositoryUrl, project.GitRemoteName, operation, message, tokenValue, branch, tag, project.GitDefaultBranch, project.GitUserName, project.GitUserEmail, source, project.Id, Guid.NewGuid().ToString("N"), !legacy && !ownerAuthentication, paths, stashId, expectedHead, patch, project.ProviderRepositoryId, settingsChanges, pullRequest, ownerAuthentication, pullRequestRead, pullRequestMerge, protectionEdit, createProtectionIfMissing, publication, publicationOperationId);
        if (publicationOperation)
        {
            string id = publication?.OperationId ?? publicationOperationId ?? "";
            try { PullRequestPublicationInput.ValidateOperationId(id); }
            catch (ArgumentException) { return Failure(operation, "publication_invalid", "An explicit operation UUID is required."); }
            request = request with { OperationId = Guid.Parse(id).ToString("D") };
        }
        RepositoryProviderResult result = protection && operation != RepositoryOperation.BranchProtectionGet
            ? await ExecuteProtectionAuditedAsync(request, provider, cancellationToken).ConfigureAwait(false)
            : await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.ErrorCode == "authentication_unavailable") result = result with
        { ErrorMessage = "Provider authentication is unavailable. Use runtime_diagnostics for this Project to distinguish owner, assertion/key and legacy readiness." };
        if (!publicationOperation && operation != RepositoryOperation.PullRequestMerge && operation is not (RepositoryOperation.BranchProtectionUpdate or RepositoryOperation.BranchProtectionDelete) && request.UseAssertion && !result.ResultUnknown && result.ErrorCode == "auth_assertion_expired")
        {
            // Re-read persisted authorization; retry only if the complete execution context remains unchanged.
            Project current = await _projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
            if (current.Id != project.Id || current.Revision != project.Revision || current.RepositoryUrl != request.RepositoryUrl
                || ProviderName(current.RepositoryProvider) != providerName)
                return Failure(operation, "auth_project_mismatch", "Authorization context changed.");
            result = await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>明示PR経路をProviderへ渡し、成功情報を共通化します。</summary>
    public async Task<PullRequestResult> ExecutePullRequestAsync(string projectName, PullRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        try { input.Validate(); }
        catch (ArgumentException) { return PullRequestResult.Failure(projectName, null, "pull_request_invalid"); }
        Project? project = await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false);
        if (project is null) return PullRequestResult.Failure(projectName, null, "project_not_found");
        string provider = ProviderName(project.RepositoryProvider);
        RepositoryProviderResult result = await ExecuteAsync(projectName, RepositoryOperation.PullRequestCreate, cancellationToken: cancellationToken, pullRequest: input).ConfigureAwait(false);
        return PullRequestResult.Parse(projectName, provider, result);
    }

    private static RepositoryProviderResult Failure(RepositoryOperation operation, string code, string message) =>
        new(false, operation.ContractName(), null, code, message);

    /// <summary>登録Projectを解決し、PR状態または明示SHA付きマージを委譲します。</summary>
    public async Task<PullRequestStateResult> ExecutePullRequestStateAsync(string projectName, PullRequestReadInput read, PullRequestMergeInput? merge = null, CancellationToken cancellationToken = default)
    {
        RepositoryOperation operation = merge is null ? RepositoryOperation.PullRequestGet : RepositoryOperation.PullRequestMerge;
        try
        {
            ArgumentNullException.ThrowIfNull(read);
            read.Validate();
            merge?.Validate();
            if (merge is not null && merge.PullRequestId != read.PullRequestId) throw new ArgumentException("PR identity mismatch.");
        }
        catch (ArgumentException) { return PullRequestStateResult.Failure(projectName, null, operation, "pull_request_invalid"); }
        Project? project = await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false);
        if (project is null) return PullRequestStateResult.Failure(projectName, null, operation, "project_not_found");
        string provider = ProviderName(project.RepositoryProvider);
        RepositoryProviderResult result = await ExecuteAsync(projectName, operation, cancellationToken: cancellationToken,
            pullRequestRead: read, pullRequestMerge: merge).ConfigureAwait(false);
        return PullRequestStateResult.Parse(projectName, provider, read.PullRequestId, result);
    }

    /// <summary>設定要求を検証し、秘密のない型付き結果を返します。</summary>
    public async Task<RepositorySettingsResult> ExecuteSettingsAsync(string project, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? changes = null, CancellationToken cancellationToken = default)
    {
        RepositoryOperation operation = changes is null ? RepositoryOperation.SettingsGet : RepositoryOperation.SettingsUpdate;
        RepositorySettingsChanges? validated;
        try { validated = changes is null ? null : RepositorySettingsChanges.Create(changes); }
        catch (ArgumentException) { return RepositorySettingsResult.Failure(project, operation, "repository_settings_invalid"); }
        Project registered = await _projects.GetRequiredAsync(project, cancellationToken).ConfigureAwait(false);
        string provider = ProviderName(registered.RepositoryProvider);
        RepositoryProviderResult result = await ExecuteAsync(project, operation, cancellationToken: cancellationToken, settingsChanges: validated).ConfigureAwait(false);
        return RepositorySettingsResult.Parse(project, operation, result, provider);
    }

    private static bool IsMutation(RepositoryOperation operation) => operation is RepositoryOperation.Commit or RepositoryOperation.CommitSelected or RepositoryOperation.Push or RepositoryOperation.Pull or RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete or RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush or RepositoryOperation.StashPush or RepositoryOperation.StashApply or RepositoryOperation.StashDrop;

    private static void ValidateArguments(RepositoryOperation operation, string? message, string? branch, string? tag, string? source, string[]? paths, string? stashId, string? expectedHead, string? patch)
    {
        if (operation == RepositoryOperation.Commit) ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (operation == RepositoryOperation.CommitSelected)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ArgumentNullException.ThrowIfNull(expectedHead);
            ArgumentException.ThrowIfNullOrWhiteSpace(patch);
            if (message.Length > 4096 || patch.Length > 4 * 1024 * 1024
                || (expectedHead is not null && expectedHead.Length != 0 && (expectedHead.Length is < 40 or > 64 || !expectedHead.All(Uri.IsHexDigit))))
                throw new ArgumentException("Selected commit arguments are invalid.");
        }
        if (operation == RepositoryOperation.StashPush) ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (operation is RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete or RepositoryOperation.BranchSwitch) ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        if (operation == RepositoryOperation.BranchCreate) ValidateBranchSource(source);
        if (operation == RepositoryOperation.BranchCreateLocal)
        {
            ValidateLocalRef(branch);
            ValidateLocalRef(source);
        }
        if (operation is RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush) ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (operation == RepositoryOperation.TagCreate) ValidateBranchSource(source);
        if (operation == RepositoryOperation.StashPush && message is { Length: > 256 }) throw new ArgumentException("Stash message must be 256 characters or fewer.", nameof(message));
        if (operation is RepositoryOperation.StashApply or RepositoryOperation.StashDrop)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stashId);
            if (stashId.Length != 40 || !stashId.All(Uri.IsHexDigit)) throw new ArgumentException("Stash ID must be a full 40-character object ID.", nameof(stashId));
        }
        if (operation == RepositoryOperation.StashPush && paths is { Length: > 128 }) throw new ArgumentException("At most 128 paths may be selected.", nameof(paths));
    }

    private static void ValidateLocalRef(string? value)
    {
        ValidateBranchSource(value);
        if (value is "HEAD" or "@" || value!.StartsWith("refs/", StringComparison.Ordinal)
            || value.Split('/').Any(static part => part.Length == 0 || part.StartsWith('.') || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use a literal local branch/tag name or a full 40-character commit SHA.", nameof(value));
    }

    private static void ValidateBranchSource(string? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Length == 40 && source.All(Uri.IsHexDigit)) return;

        bool invalid = source.StartsWith('-')
            || source.StartsWith('/')
            || source.EndsWith('/')
            || source.EndsWith('.')
            || source.Contains("..", StringComparison.Ordinal)
            || source.Contains("@{", StringComparison.Ordinal)
            || source.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || source.Any(static character => char.IsWhiteSpace(character) || "~^:?*[\\".Contains(character, StringComparison.Ordinal));
        if (invalid) throw new ArgumentException("Branch source must be a literal branch name or a full 40-character commit SHA.", nameof(source));
    }

    private static string ProviderName(string repositoryProvider) => repositoryProvider switch
    {
        "github" => "githubbie",
        "bitbucket" => "buckettie",
        _ => repositoryProvider,
    };
}
