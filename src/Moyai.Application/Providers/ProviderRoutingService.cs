using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Domain.Authentication;
using Moyai.Domain.Projects;

namespace Moyai.Application.Providers;

/// <summary>Project設定に基づきRepository操作をProviderへ安全に委譲します。</summary>
public sealed class ProviderRoutingService
{
    private readonly IProjectRepository _projects;
    private readonly IServiceTokenRepository _tokens;
    private readonly Dictionary<string, IRepositoryProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly RepositoryAuthentication _authentication;

    public ProviderRoutingService(IProjectRepository projects, IServiceTokenRepository tokens, IEnumerable<IRepositoryProvider> providers, TimeProvider timeProvider, RepositoryAuthentication? authentication = null)
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
    }

    /// <summary>Projectに設定されたProviderへ標準Repository操作を委譲します。</summary>
    public async Task<RepositoryProviderResult> ExecuteAsync(string projectName, RepositoryOperation operation, string? message = null, string? branch = null, string? tag = null, string? source = null, CancellationToken cancellationToken = default)
    {
        Project project = await _projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
        project.RequireConfiguration("sourcePath", "repositoryUrl", "repositoryProvider");
        string providerName = ProviderName(project.RepositoryProvider);
        if (!_providers.TryGetValue(providerName, out IRepositoryProvider? provider))
        {
            return Failure(operation, "provider_unavailable", $"Repository provider '{providerName}' is unavailable.");
        }

        string? tokenValue = null;
        bool legacy;
        try { legacy = _authentication.UseLegacy(_timeProvider); }
        catch (ProviderAuthenticationException exception) { return Failure(operation, exception.Code, exception.Code); }
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

        ValidateArguments(operation, message, branch, tag, source);
        var request = new RepositoryProviderRequest(project.Name, project.SourcePath, project.RepositoryUrl, project.GitRemoteName, operation, message, tokenValue, branch, tag, project.GitDefaultBranch, project.GitUserName, project.GitUserEmail, source, project.Id, Guid.NewGuid().ToString("N"), !legacy);
        RepositoryProviderResult result = await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!legacy && result.ErrorCode == "auth_assertion_expired")
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

    private static RepositoryProviderResult Failure(RepositoryOperation operation, string code, string message) =>
        new(false, operation.ContractName(), null, code, message);

    private static bool IsMutation(RepositoryOperation operation) => operation is RepositoryOperation.Commit or RepositoryOperation.Push or RepositoryOperation.Pull or RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete or RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush;

    private static void ValidateArguments(RepositoryOperation operation, string? message, string? branch, string? tag, string? source)
    {
        if (operation == RepositoryOperation.Commit) ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (operation is RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete) ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        if (operation == RepositoryOperation.BranchCreate) ValidateBranchSource(source);
        if (operation is RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush) ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (operation == RepositoryOperation.TagCreate) ValidateBranchSource(source);
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
