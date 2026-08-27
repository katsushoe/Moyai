using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Application.WorkItems;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.Mcp.Tools;

string databasePath = Environment.GetEnvironmentVariable("MOYAI_DB_PATH") ?? throw new InvalidOperationException("MOYAI_DB_PATH is required.");
string serverUrl = Environment.GetEnvironmentVariable("MOYAI_MCP_URL") ?? throw new InvalidOperationException("MOYAI_MCP_URL is required.");
var uri = new Uri(serverUrl, UriKind.Absolute);
if (!uri.IsLoopback) throw new InvalidOperationException("MOYAI_MCP_URL must use a loopback host.");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(serverUrl);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<SqliteProjectRepository>();
builder.Services.AddSingleton<SqliteWorkItemRepository>();
builder.Services.AddSingleton<SqliteServiceTokenRepository>();
builder.Services.AddSingleton<SqliteLifecycleEventWriter>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ProjectService>(serviceProvider => new ProjectService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<WorkItemService>(serviceProvider => new WorkItemService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<AuthIntrospectionService>(serviceProvider => new AuthIntrospectionService(serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ServiceTokenLifecycleService>(serviceProvider => new ServiceTokenLifecycleService(serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddHttpClient();
RegisterProvider(builder.Services, "githubbie", "GITHUBIE_MCP_URL", "github");
RegisterProvider(builder.Services, "buckettie", "BUCKETTIE_MCP_URL", "bitbucket");
RegisterOptionalLifecycleProvider(builder.Services, "MOYAI_BUILD_PROVIDER_NAME", "MOYAI_BUILD_PROVIDER_URL", "MOYAI_BUILD_PROVIDER_PREFIX");
RegisterOptionalLifecycleProvider(builder.Services, "MOYAI_DEPLOY_PROVIDER_NAME", "MOYAI_DEPLOY_PROVIDER_URL", "MOYAI_DEPLOY_PROVIDER_PREFIX");
builder.Services.AddSingleton<ProviderRoutingService>(serviceProvider => new ProviderRoutingService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<IRepositoryProvider>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<LifecycleService>(serviceProvider => new LifecycleService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<ILifecycleProvider>(), serviceProvider.GetRequiredService<SqliteLifecycleEventWriter>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddMcpServer()
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<MoyaiTools>();

var app = builder.Build();
await new SqliteDatabaseInitializer(options).InitializeAsync(app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<ServiceTokenLifecycleService>().DeleteExpiredAsync("system", "startup", app.Lifetime.ApplicationStopping);
app.MapMcp("/mcp");
await app.RunAsync();

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
