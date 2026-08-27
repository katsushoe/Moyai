using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Moyai.Application.Diagnostics;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.WorkItems;
using Moyai.Domain.WorkItems;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.Presentation.Windows;

GlobalExceptionHandler.Register(ReportFatalError);
return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
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
        var items = new WorkItemService(repository, new SqliteWorkItemRepository(options), TimeProvider.System);
        using ServiceProvider providerServices = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var routing = new ProviderRoutingService(repository, tokenRepository, CreateProviders(providerServices.GetRequiredService<IHttpClientFactory>()), TimeProvider.System);
        var lifecycle = new LifecycleService(repository, tokenRepository, CreateLifecycleProviders(providerServices.GetRequiredService<IHttpClientFactory>()), new SqliteLifecycleEventWriter(options, TimeProvider.System), TimeProvider.System);
        var tokens = new Moyai.Application.Authentication.ServiceTokenLifecycleService(tokenRepository, TimeProvider.System);
        IReadOnlyDictionary<string, string?> values = ParseOptions(arguments[1..]);

        object result = arguments[0] switch
        {
            "project-list" => await projects.ListAsync(HasFlag(values, "include-archived")),
            "project-get" => await projects.GetAsync(Required(values, "name")),
            "project-create" => await projects.CreateAsync(new CreateProjectCommand(Required(values, "name"), Required(values, "source-path"), Optional(values, "install-path"), Required(values, "repository-url"), Optional(values, "repository-provider"), Required(values, "build-provider"), Required(values, "deploy-mode"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "project-update" => await projects.UpdateAsync(new UpdateProjectCommand(Required(values, "current-name"), Required(values, "name"), Optional(values, "description"), Optional(values, "build-config-json"), Optional(values, "git-user-name"), Optional(values, "git-user-email"), Required(values, "git-remote-name"), Optional(values, "git-default-branch"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "project-set-archived" => await projects.SetArchivedAsync(Required(values, "name"), Revision(values), RequiredBoolean(values, "archived"), Required(values, "actor-type"), Required(values, "actor-name")),
            "work-item-list" => await items.ListAsync(Required(values, "project"), HasFlag(values, "include-deleted")),
            "work-item-get" => await items.GetAsync(Required(values, "project"), Required(values, "key"), HasFlag(values, "include-deleted")),
            "work-item-create" => await items.CreateAsync(new CreateWorkItemCommand(Required(values, "project"), Enum.Parse<WorkItemType>(Required(values, "type"), true), Required(values, "title"), Required(values, "actor-type"), Required(values, "actor-name"))),
            "work-item-update" => await items.UpdateAsync(new UpdateWorkItemCommand(Required(values, "project"), Required(values, "key"), Required(values, "title"), Optional(values, "description"), Enum.Parse<WorkItemPriority>(Required(values, "priority"), true), OptionalEnum<WorkItemSeverity>(values, "severity"), Optional(values, "owner"), Optional(values, "metadata-json"), Revision(values), Required(values, "actor-type"), Required(values, "actor-name"))),
            "work-item-set-deleted" => await items.SetDeletedAsync(Required(values, "project"), Required(values, "key"), Revision(values), RequiredBoolean(values, "deleted"), Required(values, "actor-type"), Required(values, "actor-name")),
            "work-item-transition" => await items.TransitionAsync(new TransitionWorkItemCommand(Required(values, "project"), Required(values, "key"), Required(values, "next-status"), long.Parse(Required(values, "expected-revision"), CultureInfo.InvariantCulture), Required(values, "actor-type"), Required(values, "actor-name"))),
            "repository-status" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Status),
            "repository-diff" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Diff),
            "repository-commit" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Commit, Required(values, "message")),
            "repository-push" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Push),
            "repository-pull" => await routing.ExecuteAsync(Required(values, "project"), RepositoryOperation.Pull),
            "token-issue" => await tokens.IssueAsync(Required(values, "audience"), Required(values, "scopes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), OptionalDate(values, "expires-at"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-rotate" => await tokens.RotateAsync(Required(values, "audience"), Required(values, "scopes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), OptionalDate(values, "expires-at"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-revoke" => await tokens.RevokeAsync(Required(values, "audience"), Required(values, "actor-type"), Required(values, "actor-name")),
            "token-cleanup" => await tokens.DeleteExpiredAsync(Required(values, "actor-type"), Required(values, "actor-name")),
            "build" => await lifecycle.ExecuteAsync(Required(values, "project"), LifecycleAction.Build, Required(values, "actor-type"), Required(values, "actor-name")),
            "release-create" => await lifecycle.ExecuteAsync(Required(values, "project"), LifecycleAction.ReleaseCreate, Required(values, "actor-type"), Required(values, "actor-name"), Required(values, "version"), notes: Optional(values, "notes")),
            "release-publish" => await lifecycle.ExecuteAsync(Required(values, "project"), LifecycleAction.ReleasePublish, Required(values, "actor-type"), Required(values, "actor-name"), Required(values, "version")),
            "release-withdraw" => await lifecycle.ExecuteAsync(Required(values, "project"), LifecycleAction.ReleaseWithdraw, Required(values, "actor-type"), Required(values, "actor-name"), Required(values, "version")),
            "deploy" => await lifecycle.ExecuteAsync(Required(values, "project"), LifecycleAction.Deploy, Required(values, "actor-type"), Required(values, "actor-name"), Optional(values, "version"), Required(values, "artifact-path")),
            _ => throw new ArgumentException($"Unknown command '{arguments[0]}'."),
        };
        WriteJson(result);
        return 0;
    }
    catch (Exception exception)
    {
        if (exception is ArgumentException or InvalidOperationException)
        {
            WriteError(exception, false);
        }
        else
        {
            ReportFatalError(exception);
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
static bool RequiredBoolean(IReadOnlyDictionary<string, string?> values, string name) => bool.Parse(Required(values, name));
static DateTimeOffset? OptionalDate(IReadOnlyDictionary<string, string?> values, string name) => Optional(values, name) is string value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) : null;
static bool HasFlag(IReadOnlyDictionary<string, string?> values, string name) => values.TryGetValue(name, out string? value) && value is null;
static string ErrorCode(Exception exception) => exception.GetType().Name.Replace("Exception", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
static void ReportFatalError(Exception exception)
{
    WriteError(exception, true);
    ErrorDialog.Show("Moyai CLI", exception);
}
static void WriteError(Exception exception, bool fatal) => Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, fatal, error = new { code = ErrorCode(exception), message = exception.Message } }, SerializerOptions()));
static void WriteJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions()));
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
