using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;
using Moyai.Application.Builds;
using Moyai.Application.Deployments;
using Moyai.Application.Diagnostics;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.Releases;
using Moyai.Application.WorkItems;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.Mcp.Tools;
using Moyai.Presentation.Windows;

GlobalExceptionHandler.Register(ReportFatalError);
return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        string databasePath = Environment.GetEnvironmentVariable("MOYAI_DB_PATH") ?? throw new InvalidOperationException("MOYAI_DB_PATH is required.");
        string serverUrl = Environment.GetEnvironmentVariable("MOYAI_MCP_URL") ?? throw new InvalidOperationException("MOYAI_MCP_URL is required.");
        var uri = new Uri(serverUrl, UriKind.Absolute);
        if (!uri.IsLoopback) throw new InvalidOperationException("MOYAI_MCP_URL must use a loopback host.");

        var builder = WebApplication.CreateBuilder(arguments);
        builder.WebHost.UseUrls(serverUrl);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SqliteProjectRepository>();
        builder.Services.AddSingleton<SqliteWorkItemRepository>();
        builder.Services.AddSingleton<SqliteWorkItemCollaborationRepository>();
        builder.Services.AddSingleton<SqliteProjectQueryRepository>();
        builder.Services.AddSingleton<SqliteServiceTokenRepository>();
        builder.Services.AddSingleton<SqliteReleaseRepository>();
        builder.Services.AddSingleton<SqliteReleaseContentRepository>();
        builder.Services.AddSingleton<SqliteBuildRepository>();
        builder.Services.AddSingleton<SqliteDeploymentRepository>();
        builder.Services.AddSingleton<SqliteLifecycleEventWriter>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<ProjectService>(serviceProvider => new ProjectService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<WorkItemService>(serviceProvider => new WorkItemService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<WorkItemCollaborationService>(serviceProvider => new WorkItemCollaborationService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemCollaborationRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ProjectQueryService>(serviceProvider => new ProjectQueryService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteProjectQueryRepository>()));
        builder.Services.AddSingleton<ReleaseService>(serviceProvider => new ReleaseService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteReleaseRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ReleaseContentService>(serviceProvider => new ReleaseContentService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemRepository>(), serviceProvider.GetRequiredService<SqliteReleaseRepository>(), serviceProvider.GetRequiredService<SqliteReleaseContentRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<AuthIntrospectionService>(serviceProvider => new AuthIntrospectionService(serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ServiceTokenLifecycleService>(serviceProvider => new ServiceTokenLifecycleService(serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddHttpClient();
        string? externalBuildProvider = Environment.GetEnvironmentVariable("MOYAI_BUILD_PROVIDER_NAME");
        foreach (string name in new[] { "csharp", "node", "php" }) if (!string.Equals(name, externalBuildProvider, StringComparison.Ordinal)) builder.Services.AddSingleton<ILifecycleProvider>(new StandardBuildProvider(name));
        RegisterProvider(builder.Services, "githubbie", "GITHUBIE_MCP_URL", "github");
        RegisterProvider(builder.Services, "buckettie", "BUCKETTIE_MCP_URL", "bitbucket");
        RegisterOptionalLifecycleProvider(builder.Services, "MOYAI_BUILD_PROVIDER_NAME", "MOYAI_BUILD_PROVIDER_URL", "MOYAI_BUILD_PROVIDER_PREFIX");
        RegisterOptionalLifecycleProvider(builder.Services, "MOYAI_DEPLOY_PROVIDER_NAME", "MOYAI_DEPLOY_PROVIDER_URL", "MOYAI_DEPLOY_PROVIDER_PREFIX");
        builder.Services.AddSingleton<ProviderRoutingService>(serviceProvider => new ProviderRoutingService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<IRepositoryProvider>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<LifecycleService>(serviceProvider => new LifecycleService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<ILifecycleProvider>(), serviceProvider.GetRequiredService<SqliteLifecycleEventWriter>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<BuildService>(serviceProvider => new BuildService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteBuildRepository>(), serviceProvider.GetRequiredService<ProviderRoutingService>(), serviceProvider.GetRequiredService<LifecycleService>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<DeploymentService>(serviceProvider => new DeploymentService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteBuildRepository>(), serviceProvider.GetRequiredService<SqliteReleaseRepository>(), serviceProvider.GetRequiredService<SqliteDeploymentRepository>(), serviceProvider.GetRequiredService<LifecycleService>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ReleaseOrchestrationService>();
        builder.Services.AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<MoyaiTools>();

        var app = builder.Build();
        await new SqliteDatabaseInitializer(options).InitializeAsync(app.Lifetime.ApplicationStopping);
        await app.Services.GetRequiredService<ServiceTokenLifecycleService>().DeleteExpiredAsync("system", "startup", app.Lifetime.ApplicationStopping);
        app.MapMcp("/mcp");
        await app.RunAsync();
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

static void WriteError(Exception exception, bool fatal) => Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, fatal, error = new { code = exception.GetType().Name.Replace("Exception", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), message = exception.Message } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

static void ReportFatalError(Exception exception)
{
    WriteError(exception, true);
    ErrorDialog.Show("Moyai MCP", exception);
}

static void RegisterProvider(IServiceCollection services, string name, string environmentVariable, string toolPrefix)
{
    string? endpoint = Environment.GetEnvironmentVariable(environmentVariable);
    if (string.IsNullOrWhiteSpace(endpoint)) return;
    services.AddSingleton<IRepositoryProvider>(serviceProvider => new McpRepositoryProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), toolPrefix), serviceProvider.GetRequiredService<IHttpClientFactory>()));
    services.AddSingleton<ILifecycleProvider>(serviceProvider => new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), toolPrefix), serviceProvider.GetRequiredService<IHttpClientFactory>()));
}

static void RegisterOptionalLifecycleProvider(IServiceCollection services, string nameVariable, string urlVariable, string prefixVariable)
{
    string? name = Environment.GetEnvironmentVariable(nameVariable);
    string? endpoint = Environment.GetEnvironmentVariable(urlVariable);
    string? prefix = Environment.GetEnvironmentVariable(prefixVariable);
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(prefix)) return;
    services.AddSingleton<ILifecycleProvider>(serviceProvider => new McpLifecycleProvider(new McpRepositoryProviderOptions(name, new Uri(endpoint, UriKind.Absolute), prefix), serviceProvider.GetRequiredService<IHttpClientFactory>()));
}
