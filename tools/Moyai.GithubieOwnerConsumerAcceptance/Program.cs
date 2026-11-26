using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.GithubieOwnerConsumerAcceptance;

[assembly: SupportedOSPlatform("windows")]

// A separate profile and database are created before any Provider connection is permitted.
try
{
    if (args.Length == 8 && args[0] is ("read" or "metadata")) return await ReadAsync(args);
    if (args.Length != 5 || args[0] != "prepare" || !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3]))
        throw new ArgumentException("Use prepare <new-profile> <provider-repository-id> <fixture-source> <repository-url>.");
    string profile = Path.GetFullPath(args[1]);
    string source = Path.GetFullPath(args[3]);
    if (Directory.Exists(profile) || File.Exists(profile) || string.IsNullOrWhiteSpace(args[2]))
        throw new InvalidOperationException("A new profile and explicit Provider repository ID are required.");
    if (!Uri.TryCreate(args[4], UriKind.Absolute, out Uri? repositoryUrl)
        || repositoryUrl.Scheme is not ("https" or "http") || repositoryUrl.UserInfo.Length != 0)
        throw new ArgumentException("Repository URL must be an absolute HTTP(S) URL without credentials.");
    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
    SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("A Windows owner is required.");
    var acl = new DirectorySecurity();
    acl.SetOwner(owner);
    acl.SetAccessRuleProtection(true, false);
    foreach (SecurityIdentifier sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    new DirectoryInfo(profile).Create(acl);
    var database = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(profile, "fixture.db"), Pooling = false }.ToString());
    await new SqliteDatabaseInitializer(database).InitializeAsync();
    var projects = new ProjectService(new SqliteProjectRepository(database), TimeProvider.System);
    var project = await projects.CreateAsync(new("GithubieOwnerConsumerFixture", source, RepositoryUrl: args[4], RepositoryProvider: "githubie",
        ActorType: "test", ActorName: "owner-consumer"));
    project = await projects.SetProviderRepositoryAsync(project.Name, project.Revision, args[2], false, "test", "owner-consumer");
    var plan = new { schemaVersion = 1, projectId = project.Id, projectName = project.Name, repositoryId = args[2], sourcePath = source, repositoryUrl = args[4] };
    await File.WriteAllTextAsync(Path.Combine(profile, "fixture-plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, projectId = project.Id, repositoryId = args[2], providerContacted = false, normalHostContacted = false, signerCreated = false }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = exception switch
    { AcceptanceException acceptance => acceptance.Code, UserHostConnectionException discovery => discovery.Code, _ => exception.GetType().Name } }));
    return 1;
}

static async Task<int> ReadAsync(string[] args)
{
    foreach (int index in new[] { 1, 2, 3, 5 })
        if (!Path.IsPathFullyQualified(args[index])) throw new ArgumentException("Absolute pinned paths are required.");
    string consumer = Path.GetFullPath(args[1]);
    string readyPath = Path.GetFullPath(args[2]);
    string runtime = Path.GetFullPath(args[3]);
    string expectedRuntimeHash = args[4];
    string profileRoot = Path.GetFullPath(args[5]);
    string expectedProfileHash = args[6];
    string expectedServerHash = args[7];
    string planPath = Path.Combine(consumer, "fixture-plan.json");
    string dbPath = Path.Combine(consumer, "fixture.db");
    if (!File.Exists(planPath) || !File.Exists(dbPath)) throw new InvalidOperationException("Prepared private Consumer profile is required.");
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    FixturePlan plan = JsonSerializer.Deserialize<FixturePlan>(await File.ReadAllBytesAsync(planPath), json) ?? throw new InvalidOperationException("Invalid plan.");
    byte[] readyBytes = await File.ReadAllBytesAsync(readyPath);
    if (readyBytes.Length > 65536) throw new InvalidOperationException("Invalid ready record.");
    using JsonDocument readyDocument = JsonDocument.Parse(readyBytes);
    JsonElement ready = readyDocument.RootElement;
    BuckettieUserHostResolver.ValidateProperties(ready);
    string runRoot = Path.GetDirectoryName(readyPath)!;
    string actualUserProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akatsukisoft", "Githubie");
    if (string.Equals(profileRoot, actualUserProfile, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(profileRoot, Path.Combine(runRoot, "profile"), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(plan.SourcePath, Path.Combine(runRoot, "repository"), StringComparison.OrdinalIgnoreCase)
        || File.Exists(Path.Combine(runRoot, "stopped.json"))) throw new InvalidOperationException("Not an isolated active fixture.");
    if (ready.GetProperty("ready").ValueKind != JsonValueKind.True || ready.GetProperty("schema_version").GetInt32() != 1
        || ready.GetProperty("authentication_mode").GetString() != "windows_owner"
        || !Version.TryParse(ready.GetProperty("version").GetString(), out Version? version) || version < new Version(2, 1, 0, 0)
        || ready.GetProperty("repository_id").GetString() != plan.RepositoryId
        || !PathEquals(ready.GetProperty("repository_path").GetString(), plan.SourcePath)
        || !PathEquals(ready.GetProperty("profile_root").GetString(), profileRoot)
        || !PathEquals(ready.GetProperty("discovery_path").GetString(), Path.Combine(profileRoot, "data", "connection.json"))
        || !PathEquals(ready.GetProperty("runtime_path").GetString(), runtime)
        || ready.GetProperty("runtime_sha256").GetString() != expectedRuntimeHash
        || ready.GetProperty("server_sha256").GetString() != expectedServerHash
        || ready.GetProperty("profile_sha256").GetString() != expectedProfileHash
        || ready.GetProperty("expires_utc").GetDateTimeOffset() - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(60))
        throw new AcceptanceException("ready_not_valid");
    string generation = ready.GetProperty("generation").GetString() ?? throw new InvalidOperationException("Missing generation.");
    if (!Guid.TryParseExact(generation, "N", out _)
        || Hash(runtime) != expectedRuntimeHash || Hash(Path.Combine(Path.GetDirectoryName(runtime)!, "Githubie.Server.dll")) != expectedServerHash
        || Hash(Path.Combine(profileRoot, "profile.json")) != expectedProfileHash)
        throw new InvalidOperationException("Provider pins do not match.");
    using JsonDocument identity = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(profileRoot, "profile.json")));
    if (identity.RootElement.GetProperty("profile_id").GetGuid() != ready.GetProperty("profile_id").GetGuid())
        throw new InvalidOperationException("Provider profile identity does not match.");
    var database = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
    var projects = new SqliteProjectRepository(database);
    var projectService = new ProjectService(projects, TimeProvider.System);
    var project = await projectService.GetAsync(plan.ProjectName);
    if (project.Id != plan.ProjectId || project.ProviderRepositoryId != plan.RepositoryId || !PathEquals(project.SourcePath, plan.SourcePath))
        throw new InvalidOperationException("Consumer project differs from the prepared plan.");
    var options = new McpRepositoryProviderOptions("githubie", null, "github")
    {
        AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1", RuntimeExecutable = runtime,
        RuntimeSha256 = expectedRuntimeHash, ExpectedGeneration = generation,
        ConnectionResolver = (_, _) => GithubieUserHostResolver.Resolve(profileRoot, runtime),
    };
    using var factory = new FixtureFactory();
    if (args[0] == "metadata")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int toolCount = await GithubieMetadataProbe.RunAsync(options, factory, timeout.Token);
        string evidence = JsonSerializer.Serialize(new { ok = true, mode = "metadata", plan.ProjectId, plan.RepositoryId,
            providerContacted = factory.Requests > 0, requestCount = factory.Requests, toolCount, businessCalls = 0,
            normalHostContacted = false, signerCreated = false, generation }, new JsonSerializerOptions { WriteIndented = true });
        string evidencePath = Path.Combine(consumer, "metadata-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        await File.WriteAllTextAsync(evidencePath, evidence);
        Console.WriteLine(evidence);
        return 0;
    }
    var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(database), [new McpRepositoryProvider(options, factory)], TimeProvider.System);
    var results = new List<object>();
    bool success = true;
    foreach (RepositoryOperation operation in new[] { RepositoryOperation.ProviderVersion, RepositoryOperation.ProviderCapabilities, RepositoryOperation.Status })
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        RepositoryProviderResult result = await routing.ExecuteAsync(plan.ProjectName, operation, cancellationToken: timeout.Token);
        results.Add(new { result.Operation, result.Ok, result.ErrorCode, result.ResultUnknown });
        success &= result.Ok;
        if (!result.Ok) break;
    }
    string resultJson = JsonSerializer.Serialize(new { ok = success, plan.ProjectId, plan.RepositoryId, providerContacted = factory.Requests > 0, requestCount = factory.Requests,
        normalHostContacted = false, signerCreated = false, generation, results }, new JsonSerializerOptions { WriteIndented = true });
    string resultPath = Path.Combine(consumer, "read-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + ".json");
    await File.WriteAllTextAsync(resultPath, resultJson);
    Console.WriteLine(resultJson);
    return success ? 0 : 1;
}

static bool PathEquals(string? left, string right) => left is not null && Path.IsPathFullyQualified(left)
    && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

internal sealed record FixturePlan(int SchemaVersion, Guid ProjectId, string ProjectName, string RepositoryId, string SourcePath, string RepositoryUrl);
internal sealed class AcceptanceException(string code) : Exception(code) { public string Code { get; } = code; }
internal sealed class FixtureFactory : IHttpClientFactory, IDisposable
{
    private readonly Counter _handler = new();
    public int Requests => _handler.Requests;
    public HttpClient CreateClient(string name) => new(_handler, false) { Timeout = TimeSpan.FromSeconds(15) };
    public void Dispose() => _handler.Dispose();
    private sealed class Counter : DelegatingHandler
    {
        public int Requests { get; private set; }
        public Counter() : base(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return base.SendAsync(request, cancellationToken); }
    }
}
