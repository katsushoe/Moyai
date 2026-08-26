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

    public ProviderRoutingService(IProjectRepository projects, IServiceTokenRepository tokens, IEnumerable<IRepositoryProvider> providers, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _projects = projects;
        _tokens = tokens;
        _providers = providers.ToDictionary(static provider => provider.Name, StringComparer.Ordinal);
        _timeProvider = timeProvider;
    }

    /// <summary>Projectに設定されたProviderへ標準Repository操作を委譲します。</summary>
    public async Task<RepositoryProviderResult> ExecuteAsync(string projectName, RepositoryOperation operation, string? message = null, CancellationToken cancellationToken = default)
    {
        Project project = await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false) ?? throw new ProjectNotFoundException(projectName);
        string providerName = ProviderName(project.RepositoryProvider);
        if (!_providers.TryGetValue(providerName, out IRepositoryProvider? provider))
        {
            throw new ProviderRoutingException("provider_unavailable", $"Repository provider '{providerName}' is unavailable.");
        }

        string? tokenValue = null;
        if (IsMutation(operation))
        {
            ServiceToken token = await _tokens.FindByAudienceAsync(providerName, cancellationToken).ConfigureAwait(false)
                ?? throw new ProviderRoutingException("invalid_service_token", $"An active service token for '{providerName}' is required.");
            if (token.ExpiresAt is not null && token.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                throw new ProviderRoutingException("service_token_expired", $"The service token for '{providerName}' has expired.");
            }
            if (!token.Scopes.Contains("repository.write"))
            {
                throw new ProviderRoutingException("service_token_scope_missing", $"The service token for '{providerName}' lacks repository.write scope.");
            }
            tokenValue = token.Token;
        }

        var request = new RepositoryProviderRequest(project.Name, project.SourcePath, project.RepositoryUrl, project.GitRemoteName, operation, message, tokenValue);
        return await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsMutation(RepositoryOperation operation) => operation is RepositoryOperation.Commit or RepositoryOperation.Push or RepositoryOperation.Pull;

    private static string ProviderName(string repositoryProvider) => repositoryProvider switch
    {
        "github" => "githubbie",
        "bitbucket" => "buckettie",
        _ => repositoryProvider,
    };
}
