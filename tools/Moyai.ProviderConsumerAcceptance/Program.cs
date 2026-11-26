using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

[assembly: SupportedOSPlatform("windows")]

// Dedicated console entry: never hosts ASP.NET, opens a control pipe, or calls host lifecycle APIs.
try
{
    if (args.Length is < 2 or > 3 || args[0] is not ("prepare" or "read" or "verify" or "rotation") || !Path.IsPathFullyQualified(args[1]))
        throw new ArgumentException("Use prepare <new-absolute-profile> or read|verify|rotation <profile> --provider-ready-confirmed.");
    string directory = Path.GetFullPath(args[1]);
    string planPath = Path.Combine(directory, "fixture-plan.json");
    FixturePlan plan;
    bool prepare = args[0] == "prepare";
    if (prepare)
    {
        if (args.Length != 2 || Directory.Exists(directory) || File.Exists(directory))
            throw new InvalidOperationException("Preparation requires a new private profile.");
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("No current owner.");
        var acl = new DirectorySecurity();
        acl.SetOwner(owner);
        acl.SetAccessRuleProtection(true, false);
        foreach (SecurityIdentifier sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).Create(acl);
        plan = new(Guid.NewGuid(), "moyai:consumer-test-" + Guid.NewGuid().ToString("N"),
            "buckettieconsumeraccept20261024", "https://bitbucket.org/buckettie-acceptance/user-host-fixture.git");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions()));
    }
    else
    {
        if (args.Length != 3 || args[2] != "--provider-ready-confirmed")
            throw new InvalidOperationException("Provider ready acknowledgement is required before connection.");
        plan = JsonSerializer.Deserialize<FixturePlan>(await File.ReadAllTextAsync(planPath), JsonOptions())
            ?? throw new InvalidOperationException("Missing fixture plan.");
    }
    var database = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder
    { DataSource = Path.Combine(directory, "fixture.db"), Pooling = false }.ToString());
    await new SqliteDatabaseInitializer(database).InitializeAsync();
    var protector = new FixtureDpapiProtector();
    var ring = new SqliteSigningKeyRing(database, new SqliteSecretEnvelopeStore(database, "moyai", plan.Issuer),
        new SecretEnvelopeCryptor(protector), plan.Issuer, TimeProvider.System);
    if (prepare)
    {
        AssertionTrustKey next = await ring.PrepareAsync();
        // This fixture's ring is also its local trust store; this does not claim Provider distribution.
        await ring.ActivateAsync(next.Kid, 360, trustDistributionConfirmed: true);
        AssertionTrustKey active = await ring.GetActiveKeyAsync();
        string publicPath = Path.Combine(directory, "public-trust.json");
        await File.WriteAllTextAsync(publicPath, JsonSerializer.Serialize(new
        {
            test_only = true, provider_distribution_confirmed = false, project_uuid = plan.ProjectId,
            issuer = active.Issuer, kid = active.Kid, algorithm = active.Algorithm,
            curve = "P-256", x = active.X, y = active.Y,
            not_before_utc = active.NotBeforeUtc, not_after_utc = active.NotAfterUtc, status = "active",
        }, JsonOptions()));
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, mode = "prepare", publicTrustPath = publicPath,
            sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(publicPath))), plan.ProjectId,
            plan.Issuer, providerContacted = false, normalHostContacted = false }));
        return 0;
    }
    // The Provider owner supplies a public, acknowledged ready record; never use an unconfirmed reservation.
    using JsonDocument ready = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "provider-ready.json")));
    JsonElement root = ready.RootElement;
    if (!root.TryGetProperty("ready", out JsonElement readyValue) || readyValue.ValueKind != JsonValueKind.True
        || root.GetProperty("schema_version").GetInt32() != 1
        || root.GetProperty("provider_id").GetString() != "buckettie"
        || root.GetProperty("project_uuid").GetGuid() != plan.ProjectId)
        throw new InvalidOperationException("Provider ready record does not match this fixture.");
    string runtime = root.GetProperty("runtime_directory").GetString() ?? throw new InvalidOperationException("Missing runtime.");
    if (!Path.IsPathFullyQualified(runtime)) throw new InvalidOperationException("Runtime must be absolute.");
    string publicTrustHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, "public-trust.json"))));
    if (root.GetProperty("public_trust_sha256").GetString() != publicTrustHash
        || root.GetProperty("profile_id").GetString() != BuckettieUserHostResolver.ProfileId(runtime))
        throw new InvalidOperationException("Provider ready record does not confirm this trust/runtime.");
    var options = new McpRepositoryProviderOptions("buckettie", null, "bitbucket")
    { ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = runtime, ExpectedMcpPath = "/mcp" };
    var capability = new AssertionCapability("buckettie", "buckettie", "1", "ES256", true,
        new Dictionary<string, string[]> { ["bitbucket_repository_status"] = ["repository.read"] });
    var issuer = new Es256AssertionIssuer(ring, new AssertionOptions(plan.Issuer), TimeProvider.System);
    var provider = new McpRepositoryProvider(options, new FixtureHttpClientFactory(), issuer, capability,
        new SqliteAssertionAudit(database, TimeProvider.System));
    var results = new List<object>();
    bool success = true;
    if (args[0] == "rotation")
    {
        using var rotationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        InteropChecks.Check rotation = await InteropChecks.RotationAsync(options, rotationTimeout.Token);
        results.Add(rotation);
        success &= rotation.Passed;
    }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    foreach (RepositoryOperation operation in new[] { RepositoryOperation.ProviderVersion, RepositoryOperation.ProviderCapabilities, RepositoryOperation.Status })
    {
        RepositoryProviderResult result = await provider.ExecuteAsync(new("Consumer fixture", directory, plan.RepositoryUrl, null,
            operation, null, null, ProjectId: plan.ProjectId, OperationId: Guid.NewGuid().ToString("N"), UseAssertion: true,
            ProviderRepositoryId: plan.RepositoryId), timeout.Token);
        success &= result.Ok;
        results.Add(new { result.Operation, result.Ok, result.ErrorCode, result.ResultUnknown });
    }
    if (args[0] == "verify" && success)
    {
        IReadOnlyList<InteropChecks.Check> checks = await InteropChecks.VerifyAsync(options, plan, issuer, timeout.Token);
        results.AddRange(checks);
        success &= checks.All(check => check.Passed);
    }
    string resultPath = Path.Combine(directory, args[0] + "-result.json");
    string safeResult = JsonSerializer.Serialize(new { ok = success, mode = args[0], normalHostContacted = false, results }, JsonOptions());
    await File.WriteAllTextAsync(resultPath, safeResult);
    Console.WriteLine(safeResult);
    return success ? 0 : 1;
}
catch (Exception exception)
{
    // Never print credential-bearing transport exception text or input files.
    Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = exception switch
    { ProviderAuthenticationException auth => auth.Code, UserHostConnectionException discovery => discovery.Code, _ => exception.GetType().Name } }));
    return 1;
}

static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web) { WriteIndented = true };

internal sealed record FixturePlan(Guid ProjectId, string Issuer, string RepositoryId, string RepositoryUrl);

/// <summary>専用試験DBのDEKだけをCurrentUser DPAPIで保護します。</summary>
internal sealed class FixtureDpapiProtector : IKeyEncryptionKeyProvider
{
    public Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        byte[] copy = dek.ToArray();
        try { return Task.FromResult(new WrappedDataKey(ProtectedData.Protect(copy, context.ToArray(), DataProtectionScope.CurrentUser), "fixture-dpapi")); }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }
    public Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectedData.Unprotect(key.WrappedDek, context.ToArray(), DataProtectionScope.CurrentUser));
    public Task<string> RotateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

internal sealed class FixtureHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
}
