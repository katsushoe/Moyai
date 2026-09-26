using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;
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
using Moyai.Configuration;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.Infrastructure.Authentication;
using System.Security.Cryptography.X509Certificates;
using Moyai.Mcp;
using Moyai.Mcp.Tools;
using Moyai.Presentation.Windows;

GlobalExceptionHandler.Register(ReportFatalError);
return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        string configPath = arguments.Length switch
        {
            0 => MoyaiSettings.DefaultPath,
            2 when arguments[0] == "--config" => Path.GetFullPath(arguments[1]),
            _ => throw new ArgumentException("Usage: Moyai.Mcp.exe [--config <moyai.json>]."),
        };
        MoyaiSettings settings = MoyaiSettings.Load(configPath);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls(settings.ServerUrl);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Services.AddWindowsService(options => options.ServiceName = "Moyai");
        builder.Services.AddSingleton<ServiceAdmission>();
        if (WindowsServiceHelpers.IsWindowsService()) builder.Services.AddSingleton<IHostLifetime, PausableServiceLifetime>();
        builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(settings => settings.SourceName = "Application");
        var options = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = settings.DatabasePath }.ToString());
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
        ProviderAuthenticationSettings auth = settings.ProviderAuthentication;
        IKeyEncryptionKeyProvider? protector = null;
        SqliteSigningKeyRing? signingKeys = null;
        if (auth.Issuer.Length != 0)
        {
            if (auth.ProtectorMode == "cng" && OperatingSystem.IsWindows())
                protector = new SqliteKeyEncryptionProvider(options, auth.KeyNamespace, auth.ActiveKeyVersion, version => new CngKeyEncryptionProvider(auth.KeyNamespace, version));
            else if (auth.ProtectorMode == "keychain" && OperatingSystem.IsMacOS())
            {
                var nativeStore = new MacKeychainStore(auth.KeyNamespace);
                protector = new SqliteKeyEncryptionProvider(options, auth.KeyNamespace, auth.ActiveKeyVersion, version => new NativeSecretKeyProtector(nativeStore, version));
            }
            else if (auth.ProtectorMode == "secret-service" && OperatingSystem.IsLinux())
            {
                var nativeStore = new SecretServiceKeyStore(auth.SecretToolPath, auth.KeyNamespace);
                protector = new SqliteKeyEncryptionProvider(options, auth.KeyNamespace, auth.ActiveKeyVersion, version => new NativeSecretKeyProtector(nativeStore, version));
            }
            else if (auth.ProtectorMode == "broker")
            {
                builder.Services.AddHttpClient("key-protector", client => { client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds); client.MaxResponseContentBufferSize = 65536; })
                    .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() =>
                    {
                        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                        store.Open(OpenFlags.ReadOnly);
                        X509Certificate2Collection certificates = store.Certificates.Find(X509FindType.FindByThumbprint, auth.BrokerCertificateThumbprint, true);
                        if (certificates.Count != 1 || !certificates[0].HasPrivateKey) throw new InvalidOperationException("Key protector client certificate is unavailable.");
                        var handler = new HttpClientHandler { AllowAutoRedirect = false };
                        handler.ClientCertificates.Add(certificates[0]);
                        return handler;
                    });
            }
            if (protector is not null)
            {
                var envelopeStore = new SqliteSecretEnvelopeStore(options, "moyai", auth.Issuer);
                signingKeys = new SqliteSigningKeyRing(options, envelopeStore, new SecretEnvelopeCryptor(protector), auth.Issuer, TimeProvider.System);
                builder.Services.AddSingleton<IAssertionIssuer>(new Es256AssertionIssuer(signingKeys, new AssertionOptions(auth.Issuer, auth.LifetimeSeconds, auth.ClockSkewSeconds), TimeProvider.System));
            }
        }
        if (auth.Issuer.Length != 0 && auth.ProtectorMode == "broker")
        {
            builder.Services.AddSingleton<IKeyEncryptionKeyProvider>(services => new BrokerKeyEncryptionProvider(services.GetRequiredService<IHttpClientFactory>(), "key-protector", new Uri(auth.BrokerEndpoint)));
            builder.Services.AddSingleton<SqliteSigningKeyRing>(services => new SqliteSigningKeyRing(options,
                new SqliteSecretEnvelopeStore(options, "moyai", auth.Issuer), new SecretEnvelopeCryptor(services.GetRequiredService<IKeyEncryptionKeyProvider>()), auth.Issuer, TimeProvider.System));
            builder.Services.AddSingleton<IAssertionIssuer>(services => new Es256AssertionIssuer(services.GetRequiredService<SqliteSigningKeyRing>(), new AssertionOptions(auth.Issuer, auth.LifetimeSeconds, auth.ClockSkewSeconds), TimeProvider.System));
            builder.Services.AddSingleton<IAssertionAdministration>(services => new AssertionAdministration(services.GetRequiredService<SqliteSigningKeyRing>(), services.GetRequiredService<IKeyEncryptionKeyProvider>()));
        }
        else builder.Services.AddSingleton<IAssertionAdministration>(new AssertionAdministration(signingKeys, protector));
        foreach (string name in new[] { "csharp", "node", "php" })
            if (!settings.Providers.Any(provider => provider.Name == name)) builder.Services.AddSingleton<ILifecycleProvider>(new StandardBuildProvider(name));
        foreach (ProviderSettings provider in settings.Providers)
        {
            builder.Services.AddHttpClient(provider.Name, client => client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
                .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            var providerOptions = new McpRepositoryProviderOptions(provider.Name, new Uri(provider.Endpoint), provider.ToolPrefix);
            if (provider.Repository) builder.Services.AddSingleton<IRepositoryProvider>(services => new McpRepositoryProvider(providerOptions, services.GetRequiredService<IHttpClientFactory>(), services.GetService<IAssertionIssuer>(),
                new AssertionCapability(provider.AssertionProviderId, provider.AssertionProviderId, "1", "ES256", true, provider.AssertionToolScopes), new SqliteAssertionAudit(options, TimeProvider.System)));
            builder.Services.AddSingleton<ILifecycleProvider>(services => new McpLifecycleProvider(
                providerOptions,
                services.GetRequiredService<IHttpClientFactory>(),
                services.GetService<IAssertionIssuer>(),
                new SqliteAssertionAudit(options, TimeProvider.System),
                auth.Mode == "assertion" ? new AssertionCapability(provider.AssertionProviderId, provider.AssertionProviderId, "1", "ES256", true, provider.AssertionToolScopes) : null));
        }
        builder.Services.AddSingleton<ProviderRoutingService>(serviceProvider => new ProviderRoutingService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<IRepositoryProvider>(), serviceProvider.GetRequiredService<TimeProvider>(), new RepositoryAuthentication(auth.Mode, auth.LegacyStartedAt, auth.LegacyUntil)));
        builder.Services.AddSingleton<LifecycleService>(serviceProvider => new LifecycleService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteServiceTokenRepository>(), serviceProvider.GetServices<ILifecycleProvider>(), serviceProvider.GetRequiredService<SqliteLifecycleEventWriter>(), serviceProvider.GetRequiredService<TimeProvider>(), new RepositoryAuthentication(auth.Mode, auth.LegacyStartedAt, auth.LegacyUntil)));
        builder.Services.AddSingleton<BuildService>(serviceProvider => new BuildService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteBuildRepository>(), serviceProvider.GetRequiredService<ProviderRoutingService>(), serviceProvider.GetRequiredService<LifecycleService>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<DeploymentService>(serviceProvider => new DeploymentService(serviceProvider.GetRequiredService<SqliteProjectRepository>(), serviceProvider.GetRequiredService<SqliteBuildRepository>(), serviceProvider.GetRequiredService<SqliteReleaseRepository>(), serviceProvider.GetRequiredService<SqliteDeploymentRepository>(), serviceProvider.GetRequiredService<LifecycleService>(), serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ReleaseOrchestrationService>();
        builder.Services.AddMcpServer(options => options.ServerInstructions = "Before every project operation, call list_projects and select the registered project name that matches the user's conversation context. Do not guess or synthesize project names.")
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<MoyaiTools>()
            .WithTools<AssertionTools>();

        var app = builder.Build();
        ServiceAdmission admission = app.Services.GetRequiredService<ServiceAdmission>();
        app.Use(async (context, next) =>
        {
            if (admission.IsPaused)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { ok = false, error = "service_paused" });
                return;
            }
            await next(context);
        });
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

static void WriteError(Exception exception, bool fatal)
{
    string message = JsonSerializer.Serialize(new { ok = false, fatal, error = new { code = exception.GetType().Name.Replace("Exception", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), message = exception.Message } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Console.Error.WriteLine(message);
    if (WindowsServiceHelpers.IsWindowsService())
    {
        try { EventLog.WriteEntry("Application", "Moyai MCP: " + message, EventLogEntryType.Error); }
        catch (Exception loggingException) { Console.Error.WriteLine(loggingException); }
    }
}

static void ReportFatalError(Exception exception)
{
    WriteError(exception, true);
    // Console and service hosts report structured errors without interactive dialogs.
}
