using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Domain.Authentication;
using Moyai.Domain.Projects;

namespace Moyai.Application.Lifecycle;

/// <summary>Project設定からBuild、Release、DeployのProviderを選択します。</summary>
public sealed class LifecycleService
{
    private readonly IProjectRepository _projects;
    private readonly IServiceTokenRepository _tokens;
    private readonly Dictionary<string, ILifecycleProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ILifecycleEventWriter _events;

    public LifecycleService(IProjectRepository projects, IServiceTokenRepository tokens, IEnumerable<ILifecycleProvider> providers, ILifecycleEventWriter events, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(events);
        _projects = projects;
        _tokens = tokens;
        _providers = providers.ToDictionary(static provider => provider.Name, StringComparer.Ordinal);
        _timeProvider = timeProvider;
        _events = events;
    }

    /// <summary>Project設定に従ってLifecycle操作を委譲します。</summary>
    public async Task<LifecycleResult> ExecuteAsync(string projectName, LifecycleAction action, string actorType, string actorName, string? version = null, string? artifactPath = null, string? notes = null, IReadOnlyList<string>? artifactPaths = null, long? providerReleaseId = null, string? tagName = null, string? commitHash = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        Project project = await _projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
        if (action is LifecycleAction.Build or LifecycleAction.BuildClean) project.RequireConfiguration("sourcePath", "buildProvider");
        else if (action is LifecycleAction.ReleaseCreate or LifecycleAction.ReleasePublish or LifecycleAction.ReleaseWithdraw) project.RequireConfiguration("repositoryUrl", "repositoryProvider");
        else if (action is LifecycleAction.Deploy or LifecycleAction.DeployRollback)
        {
            project.RequireConfiguration("sourcePath", "deployMode");
            if (project.DeployMode == "local") project.RequireConfiguration("installPath");
        }
        ValidateInput(action, version, artifactPath);
        string providerName = ResolveProvider(project, action);
        if (!_providers.TryGetValue(providerName, out ILifecycleProvider? provider)) throw new ProviderRoutingException("provider_unavailable", $"Lifecycle provider '{providerName}' is unavailable.");
        string? token = await ResolveTokenAsync(project, action, cancellationToken).ConfigureAwait(false);
        var request = new LifecycleRequest(project.Name, project.SourcePath, project.InstallPath, action, version, artifactPath, notes, token, artifactPaths, providerReleaseId, tagName, commitHash);
        LifecycleResult result = await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        await _events.WriteAsync(project.Id, action, result, actorType, actorName, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<string?> ResolveTokenAsync(Project project, LifecycleAction action, CancellationToken cancellationToken)
    {
        string? scope = action switch
        {
            LifecycleAction.ReleaseCreate or LifecycleAction.ReleasePublish or LifecycleAction.ReleaseWithdraw => "release.write",
            LifecycleAction.Deploy or LifecycleAction.DeployRollback => "deploy.write",
            _ => null,
        };
        if (scope is null) return null;
        string audience = action is LifecycleAction.Deploy or LifecycleAction.DeployRollback ? project.DeployMode : RepositoryProviderName(project.RepositoryProvider);
        ServiceToken token = await _tokens.FindByAudienceAsync(audience, cancellationToken).ConfigureAwait(false)
            ?? throw new ProviderRoutingException("invalid_service_token", $"An active service token for '{audience}' is required.");
        if (token.ExpiresAt is not null && token.ExpiresAt <= _timeProvider.GetUtcNow()) throw new ProviderRoutingException("service_token_expired", $"The service token for '{audience}' has expired.");
        if (!token.Scopes.Contains(scope)) throw new ProviderRoutingException("service_token_scope_missing", $"The service token for '{audience}' lacks {scope} scope.");
        return token.Token;
    }

    private static string ResolveProvider(Project project, LifecycleAction action) => action switch
    {
        LifecycleAction.Build or LifecycleAction.BuildClean => project.BuildProvider,
        LifecycleAction.ReleaseCreate or LifecycleAction.ReleasePublish or LifecycleAction.ReleaseWithdraw => RepositoryProviderName(project.RepositoryProvider),
        LifecycleAction.Deploy or LifecycleAction.DeployRollback => project.DeployMode,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static string RepositoryProviderName(string provider) => provider switch { "github" => "githubbie", "bitbucket" => "buckettie", _ => provider };

    private static void ValidateInput(LifecycleAction action, string? version, string? artifactPath)
    {
        if (action is LifecycleAction.ReleaseCreate or LifecycleAction.ReleasePublish or LifecycleAction.ReleaseWithdraw) ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (action is LifecycleAction.Deploy or LifecycleAction.DeployRollback) ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
    }
}
