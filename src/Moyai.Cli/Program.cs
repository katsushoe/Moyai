using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Moyai.Application.Builds;
using Moyai.Application.Deployments;
using Moyai.Application.Diagnostics;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.Releases;
using Moyai.Application.WorkItems;
using Moyai.Domain.Releases;
using Moyai.Domain.WorkItems;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

string executedCommand = FormatCommand(args);
GlobalExceptionHandler.Register(exception => ReportFatalError(exception, executedCommand));
return await RunAsync(args, executedCommand);

static async Task<int> RunAsync(string[] arguments, string executedCommand)
{
    try
    {
        if (arguments.Length == 0) throw new ArgumentException("A command is required.");
        if (string.Equals(arguments[0], "version", StringComparison.Ordinal))
        {
            WriteJson(new { name = "Moyai", version = typeof(ProjectService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0" });
            return 0;
        }

        string databasePath = Environment.GetEnvironmentVariable("MOYAI_DB_PATH") ?? throw new InvalidOperationException("MOYAI_DB_PATH is required.");
        var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await new SqliteDatabaseInitializer(options).InitializeAsync(CancellationToken.None);
        var repository = new SqliteProjectRepository(options);
        var tokenRepository = new SqliteServiceTokenRepository(options);
        var projects = new ProjectService(repository, TimeProvider.System);
        var queries = new ProjectQueryService(repository, new SqliteProjectQueryRepository(options));
        var itemRepository = new SqliteWorkItemRepository(options);
        var items = new WorkItemService(repository, itemRepository, TimeProvider.System);
        var collaboration = new WorkItemCollaborationService(repository, itemRepository, new SqliteWorkItemCollaborationRepository(options), TimeProvider.System);
        var releases = new ReleaseService(repository, new SqliteReleaseRepository(options), TimeProvider.System);
        var releaseContent = new ReleaseContentService(repository, itemRepository, new SqliteReleaseRepository(options), new SqliteReleaseContentRepository(options), TimeProvider.System);
        using ServiceProvider providerServices = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var routing = new ProviderRoutingService(repository, tokenRepository, CreateProviders(providerServices.GetRequiredService<IHttpClientFactory>()), TimeProvider.System);
        var lifecycle = new LifecycleService(repository, tokenRepository, CreateLifecycleProviders(providerServices.GetRequiredService<IHttpClientFactory>()), new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
        var releaseOrchestration = new ReleaseOrchestrationService(releases, releaseContent, lifecycle);
        var builds = new BuildService(repository, new SqliteBuildRepository(options), routing, lifecycle, TimeProvider.System);
        var deployments = new DeploymentService(repository, new SqliteBuildRepository(options), new SqliteReleaseRepository(options), new SqliteDeploymentRepository(options), lifecycle, TimeProvider.System);
        var tokens = new Moyai.Application.Authentication.ServiceTokenLifecycleService(tokenRepository, TimeProvider.System);
        IReadOnlyDictionary<string, string?> values = ParseOptions(arguments[1..]);

        object? result = arguments[0] switch
        {
            "project-list" => await projects.ListAsync(HasFlag(values, "include-archived")),
            "project-get" => await projects.GetAsync(Required(values, "name")),
            "project-create" => await projects.CreateAsync(new CreateProjectCommand(Required(values, "name"), Required(values, "source-path"), Optional(values, "install-path"), Required(values, "repository-url"), Optional(values, "repository-provider"), Required(values, "build-provider"), Required(values, "deploy-mode"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "project-update" => await projects.UpdateAsync(new UpdateProjectCommand(Required(values, "current-name"), Required(values, "name"), Optional(values, "repository-url"), Optional(values, "repository-provider"), Optional(values, "description"), Optional(values, "build-config-json"), Optional(values, "git-user-name"), Optional(values, "git-user-email"), Required(values, "git-remote-name"), Optional(values, "git-default-branch"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "project-set-archived" => await projects.SetArchivedAsync(Required(values, "name"), Revision(values), RequiredBoolean(values, "archived"), Required(values, "actor-type"), Required(values, "actor-name")),
            "project-overview" => await queries.GetOverviewAsync(Required(values, "project"), OptionalInt(values, "recent-limit") ?? 10),
            "project-changes-since" => await queries.GetChangesSinceAsync(Required(values, "project"), RequiredDate(values, "since"), OptionalInt(values, "offset") ?? 0, OptionalInt(values, "limit") ?? 50),
            "work-item-list" => await items.ListAsync(Required(values, "project"), HasFlag(values, "include-deleted")),
            "work-item-get" => await items.GetAsync(Required(values, "project"), Required(values, "key"), HasFlag(values, "include-deleted")),
            "work-item-create" => await items.CreateAsync(new CreateWorkItemCommand(Required(values, "project"), Enum.Parse<WorkItemType>(Required(values, "type"), true), Required(values, "title"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "work-item-update" => await items.UpdateAsync(new UpdateWorkItemCommand(Required(values, "project"), Required(values, "key"), Required(values, "title"), Optional(values, "description"), Enum.Parse<WorkItemPriority>(Required(values, "priority"), true), OptionalEnum<WorkItemSeverity>(values, "severity"), Optional(values, "owner"), Optional(values, "metadata-json"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "work-item-set-deleted" => await items.SetDeletedAsync(Required(values, "project"), Required(values, "key"), Revision(values), RequiredBoolean(values, "deleted"), Required(values, "actor-type"), Required(values, "actor-name")),
            "work-item-transition" => await items.TransitionAsync(new TransitionWorkItemCommand(Required(values, "project"), Required(values, "key"), Required(values, "next-status"), long.Parse(Required(values, "expected-revision"), CultureInfo.InvariantCulture), Required(values, "actor-type"), Required(values, "actor-name"))),
            "work-item-history" => await collaboration.ListHistoryAsync(Required(values, "project"), Required(values, "key")),
            "item-search" => await queries.SearchAsync(new WorkItemSearchRequest(Required(values, "project"), Required(values, "query"), OptionalEnum<WorkItemType>(values, "type"), Optional(values, "status"), OptionalEnum<WorkItemPriority>(values, "priority"), Optional(values, "owner"), OptionalDate(values, "created-after"), OptionalDate(values, "updated-after"), OptionalInt(values, "offset") ?? 0, OptionalInt(values, "limit") ?? 50)),
            "relation-add" => await collaboration.AddRelationAsync(Required(values, "project"), Required(values, "source-key"), Required(values, "target-key"), Required(values, "relation"), Required(values, "actor-type"), Required(values, "actor-name")),
            "relation-remove" => await collaboration.RemoveRelationAsync(Required(values, "project"), RequiredGuid(values, "relation-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "relation-list" => await collaboration.ListRelationsAsync(Required(values, "project"), Required(values, "key")),
            "comment-add" => await collaboration.AddCommentAsync(Required(values, "project"), Required(values, "key"), Required(values, "body"), Required(values, "actor-type"), Required(values, "actor-name")),
            "comment-list" => await collaboration.ListCommentsAsync(Required(values, "project"), Required(values, "key")),
            "task-link-add" => await collaboration.AddTaskLinkAsync(Required(values, "project"), Required(values, "key"), Required(values, "task-system"), Required(values, "task-id"), Required(values, "relation"), Required(values, "actor-type"), Required(values, "actor-name")),
            "task-link-remove" => await collaboration.RemoveTaskLinkAsync(Required(values, "project"), RequiredGuid(values, "link-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "task-link-list" => await collaboration.ListTaskLinksAsync(Required(values, "project"), Required(values, "key")),
            "commit-link-add" => await collaboration.AddCommitLinkAsync(Required(values, "project"), Required(values, "key"), Required(values, "commit-hash"), Required(values, "relation"), Required(values, "actor-type"), Required(values, "actor-name")),
            "commit-link-remove" => await collaboration.RemoveCommitLinkAsync(Required(values, "project"), RequiredGuid(values, "link-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "commit-link-list" => await collaboration.ListCommitLinksAsync(Required(values, "project"), Required(values, "key")),
            "repository-status" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Status),
            "provider-version" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.ProviderVersion),
            "provider-capabilities" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.ProviderCapabilities),
            "repository-diff" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Diff),
            "repository-commit" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Commit, Required(values, "message")),
            "repository-push" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Push),
            "repository-pull" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Pull),
            "branch-list" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.BranchList),
            "branch-create" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.BranchCreate, branch: Required(values, "branch")),
            "branch-delete" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.BranchDelete, branch: Required(values, "branch")),
            "tag-create" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.TagCreate, tag: Required(values, "tag")),
            "tag-delete" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.TagDelete, tag: Required(values, "tag")),
            "tag-push" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.TagPush, tag: Required(values, "tag")),
            "token-issue" => await tokens.IssueAsync(Required(values, "audience"), Required(values, "scopes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), OptionalDate(values, "expires-at"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-rotate" => await tokens.RotateAsync(Required(values, "audience"), Required(values, "scopes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), OptionalDate(values, "expires-at"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-revoke" => await tokens.RevokeAsync(Required(values, "audience"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-cleanup" => await tokens.DeleteExpiredAsync(Required(values, "actor-type"), Required(values, "actor-name")),
            "build" => await builds.StartAsync(Required(values, "project"), Optional(values, "configuration") ?? "Release", Required(values, "actor-type"), Required(values, "actor-name")),
            "build-start" => await builds.StartAsync(Required(values, "project"), Optional(values, "configuration") ?? "Release", Required(values, "actor-type"), Required(values, "actor-name")),
            "build-get" => await builds.GetAsync(Required(values, "project"), RequiredGuid(values, "build-id")),
            "build-list" => await builds.ListAsync(Required(values, "project")),
            "build-artifacts" => await builds.ListArtifactsAsync(Required(values, "project"), RequiredGuid(values, "build-id")),
            "build-clean" => await builds.CleanAsync(Required(values, "project"), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-create" => await releases.CreateAsync(new CreateReleaseCommand(Required(values, "project"), Required(values, "version"), Enum.Parse<ReleaseChannel>(Required(values, "channel"), true), Optional(values, "notes"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "release-get" => await releases.GetAsync(Required(values, "project"), Required(values, "version"), HasFlag(values, "include-deleted")),
            "release-list" => await releases.ListAsync(Required(values, "project"), HasFlag(values, "include-deleted")),
            "release-update" => await releases.UpdateAsync(new UpdateReleaseCommand(Required(values, "project"), Required(values, "version"), Enum.Parse<ReleaseChannel>(Required(values, "channel"), true), Optional(values, "tag-name"), Optional(values, "commit-hash"), Optional(values, "notes"), OptionalDate(values, "planned-at"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "release-transition" => await releases.TransitionAsync(new TransitionReleaseCommand(Required(values, "project"), Required(values, "version"), Enum.Parse<ReleaseStatus>(Required(values, "next-status"), true), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "release-add-item" => await releaseContent.AddItemAsync(new AddReleaseItemCommand(Required(values, "project"), Required(values, "version"), Required(values, "work-item-key"), Required(values, "relation"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "release-remove-item" => await releaseContent.RemoveItemAsync(Required(values, "project"), Required(values, "version"), RequiredGuid(values, "relation-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-list-items" => await releaseContent.ListItemsAsync(Required(values, "project"), Required(values, "version")),
            "release-add-artifact" => await releaseContent.AddArtifactAsync(new AddReleaseArtifactCommand(Required(values, "project"), Required(values, "version"), OptionalGuid(values, "build-artifact-id"), Required(values, "name"), Required(values, "artifact-type"), Required(values, "platform"), Required(values, "architecture"), Required(values, "file-name"), Optional(values, "file-path"), Optional(values, "download-url"), OptionalLong(values, "file-size"), Optional(values, "sha256"), Optional(values, "signature-path"), Optional(values, "signature-url"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "release-remove-artifact" => await releaseContent.RemoveArtifactAsync(Required(values, "project"), Required(values, "version"), RequiredGuid(values, "artifact-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-list-artifacts" => await releaseContent.ListArtifactsAsync(Required(values, "project"), Required(values, "version")),
            "release-prepare" => await releaseOrchestration.PrepareAsync(Required(values, "project"), Required(values, "version"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-mark-ready" => await releaseOrchestration.MarkReadyAsync(Required(values, "project"), Required(values, "version"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-publish" => await releaseOrchestration.PublishAsync(Required(values, "project"), Required(values, "version"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-retry" => await releaseOrchestration.RetryAsync(Required(values, "project"), Required(values, "version"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-withdraw" => await releaseOrchestration.WithdrawAsync(Required(values, "project"), Required(values, "version"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "release-latest" => await releaseOrchestration.LatestAsync(Required(values, "project")),
            "release-overview" => await releaseOrchestration.OverviewAsync(Required(values, "project"), Required(values, "version")),
            "deployment-target-get" => await deployments.GetTargetAsync(Required(values, "project")),
            "deployment-target-update" => await deployments.UpdateTargetAsync(Required(values, "project"), Required(values, "name"), Required(values, "mode"), Required(values, "destination-path"), Optional(values, "kelpie-target"), Optional(values, "config-json"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name")),
            "deploy" => await deployments.StartAsync(Required(values, "project"), RequiredGuid(values, "build-id"), RequiredGuid(values, "artifact-id"), Optional(values, "version"), Required(values, "actor-type"), Required(values, "actor-name")),
            "deploy-start" => await deployments.StartAsync(Required(values, "project"), RequiredGuid(values, "build-id"), RequiredGuid(values, "artifact-id"), Optional(values, "version"), Required(values, "actor-type"), Required(values, "actor-name")),
            "deploy-get" => await deployments.GetAsync(Required(values, "project"), RequiredGuid(values, "deployment-id")),
            "deploy-list" => await deployments.ListAsync(Required(values, "project")),
            "deploy-status" => await deployments.GetAsync(Required(values, "project"), RequiredGuid(values, "deployment-id")),
            "deploy-retry" => await deployments.RetryAsync(Required(values, "project"), RequiredGuid(values, "deployment-id"), RequiredGuid(values, "artifact-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            "deploy-rollback" => await deployments.RollbackAsync(Required(values, "project"), RequiredGuid(values, "deployment-id"), Required(values, "actor-type"), Required(values, "actor-name")),
            _ => throw new ArgumentException($"Unknown command '{arguments[0]}'."),
        };
        WriteJson(result);
        return 0;
    }
    catch (Exception exception)
    {
        if (exception is ArgumentException or InvalidOperationException)
        {
            WriteError(exception, false, executedCommand);
        }
        else
        {
            ReportFatalError(exception, executedCommand);
        }
        return 1;
    }
}

static Dictionary<string, string?> ParseOptions(string[] arguments)
{
    var values = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (int index = 0; index < arguments.Length; index++)
    {
        string argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{argument}'.");
        string name = argument[2..];
        string? value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++index] : null;
        if (!values.TryAdd(name, value)) throw new ArgumentException($"Option '--{name}' was specified more than once.");
    }
    return values;
}

static string Required(IReadOnlyDictionary<string, string?> values, string name) =>
    values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Option '--{name}' is required.");

static string? Optional(IReadOnlyDictionary<string, string?> values, string name) => values.TryGetValue(name, out string? value) ? value : null;
static T? OptionalEnum<T>(IReadOnlyDictionary<string, string?> values, string name) where T : struct => Optional(values, name) is string value ? Enum.Parse<T>(value, true) : null;
static long Revision(IReadOnlyDictionary<string, string?> values) => long.Parse(Required(values, "expected-revision"), CultureInfo.InvariantCulture);
static Guid RequiredGuid(IReadOnlyDictionary<string, string?> values, string name) => Guid.Parse(Required(values, name), CultureInfo.InvariantCulture);
static Guid? OptionalGuid(IReadOnlyDictionary<string, string?> values, string name) => Optional(values, name) is string value ? Guid.Parse(value, CultureInfo.InvariantCulture) : null;
static long? OptionalLong(IReadOnlyDictionary<string, string?> values, string name) => Optional(values, name) is string value ? long.Parse(value, CultureInfo.InvariantCulture) : null;
static bool RequiredBoolean(IReadOnlyDictionary<string, string?> values, string name) => bool.Parse(Required(values, name));
static DateTimeOffset? OptionalDate(IReadOnlyDictionary<string, string?> values, string name) => Optional(values, name) is string value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) : null;
static DateTimeOffset RequiredDate(IReadOnlyDictionary<string, string?> values, string name) => DateTimeOffset.Parse(Required(values, name), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
static int? OptionalInt(IReadOnlyDictionary<string, string?> values, string name) => Optional(values, name) is string value ? int.Parse(value, CultureInfo.InvariantCulture) : null;
static bool HasFlag(IReadOnlyDictionary<string, string?> values, string name) => values.TryGetValue(name, out string? value) && value is null;
static string ErrorCode(Exception exception) => exception.GetType().Name.Replace("Exception", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
static void ReportFatalError(Exception exception, string executedCommand)
{
    WriteError(exception, true, executedCommand);
}
static void WriteError(Exception exception, bool fatal, string executedCommand) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        command = executedCommand,
        summary = $"{exception.GetType().Name}: {exception.Message}",
        ok = false,
        fatal,
        error = new { code = ErrorCode(exception), message = exception.Message },
    }, SerializerOptions()));

static string FormatCommand(IReadOnlyList<string> arguments)
{
    string executable = Path.GetFileName(Environment.ProcessPath) ?? "Moyai.Cli";
    return string.Join(' ', new[] { executable }.Concat(arguments.Select(QuoteArgument)));
}

static string QuoteArgument(string argument) =>
    argument.Length == 0 || argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal)
        ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
        : argument;

static void WriteJson(object? value) => Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions()));
static JsonSerializerOptions SerializerOptions() => new(JsonSerializerDefaults.Web) { WriteIndented = true };

static IReadOnlyList<IRepositoryProvider> CreateProviders(IHttpClientFactory httpClientFactory)
{
    var providers = new List<IRepositoryProvider>();
    AddProvider(providers, httpClientFactory, "githubbie", "GITHUBIE_MCP_URL", "github");
    AddProvider(providers, httpClientFactory, "buckettie", "BUCKETTIE_MCP_URL", "bitbucket");
    return providers;
}

static IReadOnlyList<ILifecycleProvider> CreateLifecycleProviders(IHttpClientFactory httpClientFactory)
{
    var providers = new List<ILifecycleProvider>();
    string? externalBuildProvider = Environment.GetEnvironmentVariable("MOYAI_BUILD_PROVIDER_NAME");
    foreach (string name in new[] { "csharp", "node", "php" }) if (!string.Equals(name, externalBuildProvider, StringComparison.Ordinal)) providers.Add(new StandardBuildProvider(name));
    AddLifecycleProvider(providers, httpClientFactory, "githubbie", "GITHUBIE_MCP_URL", "github");
    AddLifecycleProvider(providers, httpClientFactory, "buckettie", "BUCKETTIE_MCP_URL", "bitbucket");
    AddOptionalLifecycleProvider(providers, httpClientFactory, "MOYAI_BUILD_PROVIDER_NAME", "MOYAI_BUILD_PROVIDER_URL", "MOYAI_BUILD_PROVIDER_PREFIX");
    AddOptionalLifecycleProvider(providers, httpClientFactory, "MOYAI_DEPLOY_PROVIDER_NAME", "MOYAI_DEPLOY_PROVIDER_URL", "MOYAI_DEPLOY_PROVIDER_PREFIX");
    return providers;
}

static void AddProvider(List<IRepositoryProvider> providers, IHttpClientFactory httpClientFactory, string name, string environmentVariable, string toolPrefix)
{
    string? endpoint = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(endpoint)) providers.Add(new McpRepositoryProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), toolPrefix), httpClientFactory));
}

static void AddLifecycleProvider(List<ILifecycleProvider> providers, IHttpClientFactory httpClientFactory, string name, string environmentVariable, string toolPrefix)
{
    string? endpoint = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(endpoint)) providers.Add(new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), toolPrefix), httpClientFactory));
}

static void AddOptionalLifecycleProvider(List<ILifecycleProvider> providers, IHttpClientFactory httpClientFactory, string nameVariable, string urlVariable, string prefixVariable)
{
    string? name = Environment.GetEnvironmentVariable(nameVariable);
    string? endpoint = Environment.GetEnvironmentVariable(urlVariable);
    string? prefix = Environment.GetEnvironmentVariable(prefixVariable);
    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(prefix)) providers.Add(new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), prefix), httpClientFactory));
}
