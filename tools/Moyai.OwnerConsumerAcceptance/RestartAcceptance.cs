using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Runs one bounded, read-only two-generation acceptance in a single process.</summary>
internal static class RestartAcceptance
{
    internal const string CaseId = RestartAgreement.CaseId;

    internal static async Task RunAsync(FixturePlan plan, SqliteProjectRepository projects, SqliteDatabaseOptions database,
        RestartAgreement agreement)
    {
        string profile = agreement.Profile, readyPath = agreement.ReadyPath, phaseAPath = agreement.PhaseAPath;
        RestartCandidatePins pins = agreement.Candidate;
        if (plan.ProjectId != agreement.ProjectId || plan.RepositoryId != agreement.RepositoryId)
            throw new InvalidOperationException("Consumer project does not match the agreement.");
        agreement.RequireRuntime(RuntimeInformation.FrameworkDescription);
        if (!Path.IsPathFullyQualified(readyPath) || !Path.IsPathFullyQualified(phaseAPath))
            throw new InvalidOperationException("Absolute readiness paths are required.");
        RestartCandidateManifest.Verify(pins.Root, pins.Manifest, pins.ManifestSha256, pins.FileCount, pins.ConsoleName, pins.ConsoleSha256);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""),
            Path.GetFullPath(Path.Combine(pins.Root, pins.ConsoleName))))
            throw new InvalidOperationException("The running executable is not the pinned candidate.");
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Snapshot a = ReadReady(readyPath, plan, "owner_restart_phase_a", pins);
        if (!string.Equals(BuckettieUserHostResolver.ProfileId(a.Runtime), agreement.ProviderProfileId, StringComparison.OrdinalIgnoreCase)
            || a.Expires != agreement.Expires) throw new InvalidOperationException("Phase A does not match the agreement.");
        TimeSpan remaining = a.Expires - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.FromMinutes(30)) overall.CancelAfter(remaining);
        var sequence = new RestartSequence(a.Expires);
        using (JsonDocument state = JsonDocument.Parse(await File.ReadAllTextAsync(phaseAPath, overall.Token)))
        {
            JsonElement root = state.RootElement;
            BuckettieUserHostResolver.ValidateProperties(root);
            if (root.GetProperty("case_id").GetString() != CaseId || !root.GetProperty("read_only").GetBoolean()
                || !root.GetProperty("ready").GetBoolean() || !GenerationMatches(root.GetProperty("generation"), a))
                throw new InvalidOperationException("Phase A case is not ready.");
        }
        using UserHostConnection oldConnection = BuckettieUserHostResolver.Resolve(a.Runtime, "/mcp");
        if (oldConnection.Generation != a.Generation) throw new InvalidOperationException("Phase A generation changed.");
        using FileStream reservation = RestartReservation.Reserve(profile, CaseId);
        using var clients = new CountingClients();
        Snapshot? b = null;
        RestartProbeResult? rejection = null;
        Snapshot CurrentB() => b ?? throw new InvalidOperationException("Phase B is not ready.");
        await RestartWorkflow.RunAsync(sequence,
        [
            async () =>
            {
                await ReadAsync(plan, projects, database, a, clients, overall.Token);
                Console.WriteLine(JsonSerializer.Serialize(new { phase = "a", readPassed = true, caseId = CaseId, processId = a.ProcessId, processStartUtcTicks = a.ProcessTicks }));
                await Console.Out.FlushAsync(overall.Token);
            },
            async () =>
            {
                // The owner's stopped receipt and absent discovery must be observed before B.
                while (true)
                {
                    overall.Token.ThrowIfCancellationRequested();
                    if (DateTimeOffset.UtcNow >= a.Expires) throw new InvalidOperationException("Agreement window expired.");
                    if (File.Exists(phaseAPath))
                    {
                        using JsonDocument state = JsonDocument.Parse(await File.ReadAllTextAsync(phaseAPath, overall.Token));
                        JsonElement root = state.RootElement;
                        BuckettieUserHostResolver.ValidateProperties(root);
                        if (root.TryGetProperty("stopped", out JsonElement stopped) && stopped.ValueKind == JsonValueKind.True
                            && root.GetProperty("case_id").GetString() == CaseId && root.GetProperty("stop_cli_exit").GetInt32() == 0
                            && root.GetProperty("discovery_removed").GetBoolean() && GenerationMatches(root.GetProperty("generation"), a)
                            && !File.Exists(Path.Combine(a.Runtime, "host.json"))) break;
                    }
                    await Task.Delay(250, overall.Token);
                }
                Console.WriteLine(JsonSerializer.Serialize(new { phase = "a", stopped = true, discoveryRemoved = true, caseId = CaseId }));
                await Console.Out.FlushAsync(overall.Token);
            },
            async () =>
            {
                while (true)
                {
                    overall.Token.ThrowIfCancellationRequested();
                    if (DateTimeOffset.UtcNow >= a.Expires) throw new InvalidOperationException("Agreement window expired.");
                    using JsonDocument ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyPath, overall.Token));
                    if (ready.RootElement.GetProperty("status").GetString() == "owner_restart_phase_b")
                    {
                        b = ReadReady(readyPath, plan, "owner_restart_phase_b", pins);
                        break;
                    }
                    await Task.Delay(250, overall.Token);
                }
                if (!StringComparer.OrdinalIgnoreCase.Equals(a.Runtime, b.Runtime) || a.Generation == b.Generation || b.Expires != a.Expires)
                    throw new InvalidOperationException("Phase B does not match the agreement.");
            },
            () =>
            {
                int before = clients.Created;
                try
                {
                    using var stale = new ProviderConnection(Options(CurrentB(), a.Generation), clients);
                    throw new InvalidOperationException("Stale generation was accepted.");
                }
                catch (UserHostConnectionException exception) when (exception.Code == "host_discovery_stale") { }
                if (clients.Created != before) throw new InvalidOperationException("Stale generation reached HTTP.");
                return Task.CompletedTask;
            },
            async () =>
            {
                Snapshot currentB = CurrentB();
                if (ReadReady(readyPath, plan, "owner_restart_phase_b", pins) != currentB)
                    throw new InvalidOperationException("Phase B readiness changed before the credential check.");
                using UserHostConnection current = BuckettieUserHostResolver.Resolve(currentB.Runtime, "/mcp");
                if (current.Generation != currentB.Generation) throw new InvalidOperationException("Phase B generation changed.");
                rejection = await new RestartCredentialProbe().CheckAsync(oldConnection, current, overall.Token);
            },
            async () =>
            {
                Snapshot currentB = CurrentB();
                if (ReadReady(readyPath, plan, "owner_restart_phase_b", pins) != currentB)
                    throw new InvalidOperationException("Phase B readiness changed before the read.");
                await ReadAsync(plan, projects, database, currentB, clients, overall.Token);
            }
        ], () => DateTimeOffset.UtcNow);
        if (rejection is null) throw new InvalidOperationException("Missing credential check result.");
        Console.WriteLine(JsonSerializer.Serialize(new { phase = "b", caseId = CaseId, staleGenerationPreDispatch = true,
            rejection.HttpStatus, rejection.ErrorCode, rejection.Outcome, rejection.RequestCount, readPassed = true }));
    }

    private static async Task ReadAsync(FixturePlan plan, SqliteProjectRepository projects, SqliteDatabaseOptions database,
        Snapshot ready, CountingClients clients, CancellationToken cancellationToken)
    {
        var provider = new McpRepositoryProvider(Options(ready, ready.Generation), clients);
        var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(database), [provider], TimeProvider.System);
        RepositoryProviderResult result = await routing.ExecuteAsync(plan.ProjectName, RepositoryOperation.Status, cancellationToken: cancellationToken);
        if (!result.Ok || result.ResultUnknown) throw new InvalidOperationException("Read-only routing failed.");
    }

    private static McpRepositoryProviderOptions Options(Snapshot ready, string expected) => new("buckettie", null, "bitbucket")
    { AuthenticationMode = "windows_owner", ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = ready.Runtime, ExpectedGeneration = expected };

    private static Snapshot ReadReady(string path, FixturePlan plan, string status, RestartCandidatePins pins)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        RestartReadyFields fields = RestartReadyValidator.Validate(document.RootElement, plan.ProjectId, plan.RepositoryId, status, pins, DateTimeOffset.UtcNow);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        using Process process = Process.GetCurrentProcess();
        string expected = $"{identity.User?.Value}|{process.SessionId}|{fields.ProcessId}|{fields.ProcessTicks}";
        return new(fields.Runtime, expected, fields.ProcessId, fields.ProcessTicks, fields.Expires);
    }

    private static bool GenerationMatches(JsonElement value, Snapshot expected) => value.GetProperty("process_id").GetInt32() == expected.ProcessId
        && value.GetProperty("process_start_utc_ticks").GetInt64() == expected.ProcessTicks;

    private sealed record Snapshot(string Runtime, string Generation, int ProcessId, long ProcessTicks, DateTimeOffset Expires);

    private sealed class CountingClients : IHttpClientFactory, IDisposable
    {
        private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        public int Created { get; private set; }
        public HttpClient CreateClient(string name) { Created++; return new HttpClient(_handler, false) { Timeout = TimeSpan.FromSeconds(15) }; }
        public void Dispose() => _handler.Dispose();
    }
}
