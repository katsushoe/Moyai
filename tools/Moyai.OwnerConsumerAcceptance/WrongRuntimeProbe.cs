using System.Security.Cryptography;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

internal static class WrongRuntimeProbe
{
    internal static async Task RunAsync(Guid projectId, string projectName, string repositoryId,
        SqliteProjectRepository projects, SqliteDatabaseOptions database, string casePath, string caseHash)
    {
        byte[] bytes = File.ReadAllBytes(casePath);
        if (caseHash.Length != 64 || !caseHash.All(Uri.IsHexDigit)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(caseHash)))
            throw new InvalidOperationException("Case hash mismatch.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        BuckettieUserHostResolver.ValidateProperties(root);
        string runtime = root.GetProperty("runtime").GetString() ?? "";
        string expectedRuntime = root.GetProperty("expected_runtime").GetString() ?? "";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (root.GetProperty("case_id").GetString() != "owner-wrong-runtime-20261029-01"
            || root.GetProperty("project_uuid").GetGuid() != projectId || root.GetProperty("repository_id").GetString() != repositoryId
            || root.GetProperty("starts_at_utc").GetDateTimeOffset() > now
            || root.GetProperty("expires_at_utc").GetDateTimeOffset() <= now.AddSeconds(30)
            || !Path.IsPathFullyQualified(runtime) || !Path.IsPathFullyQualified(expectedRuntime)
            || string.Equals(Path.GetFullPath(runtime), Path.GetFullPath(expectedRuntime), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(runtime) || Directory.EnumerateFileSystemEntries(runtime).Any())
            throw new InvalidOperationException("Invalid isolated wrong-runtime case.");
        using var clients = new NoHttpClients();
        var options = new McpRepositoryProviderOptions("buckettie", null, "bitbucket")
        { AuthenticationMode = "windows_owner", ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = runtime };
        var provider = new McpRepositoryProvider(options, clients);
        var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(database), [provider], TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        RepositoryProviderResult result = await routing.ExecuteAsync(projectName, RepositoryOperation.Status, cancellationToken: timeout.Token);
        bool passed = !result.Ok && !result.ResultUnknown && result.ErrorCode == "user_host_not_running" && clients.Created == 0;
        Console.WriteLine(JsonSerializer.Serialize(new { caseId = "owner-wrong-runtime-20261029-01", ok = passed,
            result.ErrorCode, result.ResultUnknown, httpClientsCreated = clients.Created, httpRequests = 0,
            evidenceScope = "empty isolated runtime; not a live mismatched profile or another Windows user" }));
        if (!passed) throw new InvalidOperationException("Wrong-runtime rejection did not match.");
    }

    private sealed class NoHttpClients : IHttpClientFactory, IDisposable
    {
        public int Created { get; private set; }
        public HttpClient CreateClient(string name) { Created++; throw new InvalidOperationException("HTTP is forbidden in this case."); }
        public void Dispose() { }
    }
}
