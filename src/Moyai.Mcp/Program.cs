using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Infrastructure.Persistence;
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
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ProjectService>(serviceProvider => new ProjectService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<WorkItemService>(serviceProvider => new WorkItemService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteWorkItemRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<AuthIntrospectionService>(serviceProvider => new AuthIntrospectionService(serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddMcpServer()
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<MoyaiTools>();

var app = builder.Build();
await new SqliteDatabaseInitializer(options).InitializeAsync(app.Lifetime.ApplicationStopping);
app.MapMcp("/mcp");
await app.RunAsync();
