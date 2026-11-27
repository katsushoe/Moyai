using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Providers;
using Moyai.OwnerConsumerAcceptance;

[assembly: SupportedOSPlatform("windows")]

// This console owns only its dedicated DB. It has no host/control-pipe or signing services.
try
{
    if (args.Length < 2 || args[0] is not ("prepare" or "read" or "case" or "verify" or "restart" or "wrong-runtime" or "loss-case" or "verify-loss") || !Path.IsPathFullyQualified(args[1]))
        throw new ArgumentException("Use prepare, read, case, verify, restart, wrong-runtime, loss-case or verify-loss with a dedicated absolute profile.");
    string profile = Path.GetFullPath(args[1]);
    string planPath = Path.Combine(profile, "fixture-plan.json");
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    bool prepare = args[0] == "prepare";
    if (prepare)
    {
        if (args.Length != 5 || Directory.Exists(profile) || File.Exists(profile) || !Path.IsPathFullyQualified(args[3]))
            throw new ArgumentException("Prepare requires a new profile and an absolute dedicated Repository path.");
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("No owner.");
        var acl = new DirectorySecurity();
        acl.SetOwner(owner);
        acl.SetAccessRuleProtection(true, false);
        foreach (SecurityIdentifier sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(profile).Create(acl);
    }
    else if (args.Length != (args[0] == "read" ? 3 : 4)) throw new ArgumentException("Invalid argument count.");
    RestartAgreement? restartAgreement = args[0] == "restart" ? RestartAgreement.Load(args[2], args[3], DateTimeOffset.UtcNow) : null;
    if (restartAgreement is not null && !StringComparer.OrdinalIgnoreCase.Equals(profile, restartAgreement.Profile))
        throw new InvalidOperationException("Consumer profile does not match the agreement.");

    var database = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(profile, "fixture.db"), Pooling = false }.ToString());
    // Refuse an unprepared profile before creating any file on execution.
    if (!prepare && (!File.Exists(planPath) || !File.Exists(Path.Combine(profile, "fixture.db"))))
        throw new InvalidOperationException("Dedicated preparation is required.");
    if (restartAgreement is null && args[0] is not ("wrong-runtime" or "loss-case" or "verify-loss")) await new SqliteDatabaseInitializer(database).InitializeAsync();
    var projects = new SqliteProjectRepository(database);
    var projectService = new ProjectService(projects, TimeProvider.System);
    FixturePlan plan;
    if (prepare)
    {
        var project = await projectService.CreateAsync(new("OwnerConsumerFixture", args[3], RepositoryUrl: args[4], RepositoryProvider: "buckettie",
            ActorType: "test", ActorName: "owner-consumer"));
        project = await projectService.SetProviderRepositoryAsync(project.Name, project.Revision, args[2], false, "test", "owner-consumer");
        plan = new(project.Id, project.Name, args[2], Path.GetFullPath(args[3]), args[4]);
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, json));
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, plan.ProjectId, plan.RepositoryId, providerContacted = false, normalHostContacted = false, signerCreated = false }));
        return 0;
    }
    plan = JsonSerializer.Deserialize<FixturePlan>(await File.ReadAllTextAsync(planPath), json) ?? throw new InvalidOperationException("Invalid plan.");
    var persisted = await projectService.GetAsync(plan.ProjectName);
    if (persisted.Id != plan.ProjectId || persisted.ProviderRepositoryId != plan.RepositoryId || persisted.SourcePath != plan.SourcePath)
        throw new InvalidOperationException("Consumer Project identity changed.");

    if (args[0] == "wrong-runtime")
    {
        await WrongRuntimeProbe.RunAsync(plan.ProjectId, plan.ProjectName, plan.RepositoryId, projects, database, args[2], args[3]);
        return 0;
    }
    if (restartAgreement is not null)
    {
        await RestartAcceptance.RunAsync(plan, projects, database, restartAgreement);
        return 0;
    }

    var journal = new FixtureJournal(profile);
    if (args[0] is "verify" or "verify-loss")
    {
        journal.RequirePending(args[2]);
        FixtureEvidence evidence = JsonSerializer.Deserialize<FixtureEvidence>(await File.ReadAllTextAsync(journal.EvidencePath(args[2])), json)
            ?? throw new InvalidOperationException("Missing case evidence.");
        if (evidence.Input.CaseId != args[2] || evidence.ProjectId != plan.ProjectId || evidence.RepositoryId != plan.RepositoryId
            || evidence.Input.Validate() == RepositoryOperation.SettingsGet) throw new InvalidOperationException("Evidence identity mismatch.");
        FixtureObservation observation = JsonSerializer.Deserialize<FixtureObservation>(await File.ReadAllTextAsync(args[3]), json)
            ?? throw new InvalidOperationException("Missing independent observation.");
        FixtureCheck check = args[0] == "verify-loss" ? FixtureValidation.VerifyLostResponse(evidence, observation) : FixtureValidation.Verify(evidence, observation);
        string verified = JsonSerializer.Serialize(new { ok = check.Passed, mode = args[0], caseId = args[2], plan.ProjectId, plan.RepositoryId,
            check.ErrorCode, check.MismatchedFields, check.ReturnedCommitSha, normalHostContacted = false, providerContacted = false }, json);
        await File.WriteAllTextAsync(journal.EvidencePath(args[2]) + ".verification.json", verified);
        if (check.Passed) journal.Complete(args[2]);
        Console.WriteLine(verified);
        return check.Passed ? 0 : 1;
    }

    // Public owner-authenticated generation readiness must name this exact Consumer and Repository.
    using JsonDocument ready = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    JsonElement root = ready.RootElement;
    BuckettieUserHostResolver.ValidateProperties(root);
    FixtureValidation.RequireWindow(root, DateTimeOffset.UtcNow);
    if (root.GetProperty("ready").ValueKind != JsonValueKind.True || root.GetProperty("provider_id").GetString() != "buckettie"
        || root.GetProperty("authentication_mode").GetString() != "windows_owner" || root.GetProperty("owner_protocol_version").GetInt32() != 1
        || root.GetProperty("project_uuid").GetGuid() != plan.ProjectId || root.GetProperty("repository_id").GetString() != plan.RepositoryId
        || !Version.TryParse(root.GetProperty("version").GetString(), out Version? version) || version < new Version(2, 0, 1, 0))
        throw new InvalidOperationException("Provider is not ready for this dedicated Consumer.");
    string runtime = root.GetProperty("runtime").GetString() ?? throw new InvalidOperationException("Missing runtime.");
    JsonElement generation = root.GetProperty("generation");
    int processId = generation.GetProperty("process_id").GetInt32();
    long processTicks = generation.GetProperty("process_start_utc_ticks").GetInt64();
    if (!Path.IsPathFullyQualified(runtime) || root.GetProperty("profile_id").GetString() != BuckettieUserHostResolver.ProfileId(runtime)
        || processId <= 0 || processTicks <= 0)
        throw new InvalidOperationException("Provider generation/runtime mismatch.");
    using WindowsIdentity currentOwner = WindowsIdentity.GetCurrent();
    using System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
    string expectedGeneration = $"{currentOwner.User?.Value}|{currentProcess.SessionId}|{processId}|{processTicks}";
    var options = new McpRepositoryProviderOptions("buckettie", null, "bitbucket")
    { AuthenticationMode = "windows_owner", ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = runtime,
        ExpectedGeneration = expectedGeneration };
    bool lossCase = args[0] == "loss-case";
    using var factory = new FixtureHttpClientFactory(lossCase);
    var provider = new McpRepositoryProvider(options, factory);
    var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(database), [provider], TimeProvider.System);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var results = new List<object>();
    bool success = true;
    bool verificationPending = false;
    if (args[0] == "read")
    {
        foreach (RepositoryOperation operation in new[] { RepositoryOperation.ProviderVersion, RepositoryOperation.ProviderCapabilities, RepositoryOperation.Status, RepositoryOperation.Diff })
        {
            RepositoryProviderResult result = await routing.ExecuteAsync(plan.ProjectName, operation, cancellationToken: timeout.Token);
            results.Add(new { result.Operation, result.Ok, result.ErrorCode, result.ResultUnknown });
            success &= result.Ok;
            if (!result.Ok) break;
        }
    }
    else
    {
        // Owner-prepared case inputs are explicit. The Provider must confirm each case in its fresh ready record.
        byte[] caseBytes = await File.ReadAllBytesAsync(args[3]);
        using JsonDocument caseDocument = JsonDocument.Parse(caseBytes);
        BuckettieUserHostResolver.ValidateProperties(caseDocument.RootElement);
        FixtureCase input = JsonSerializer.Deserialize<FixtureCase>(caseBytes, json) ?? throw new InvalidOperationException("Invalid case.");
        RepositoryOperation operation = input.Validate();
        if (lossCase && (operation != RepositoryOperation.Commit || !input.CaseId.StartsWith("owner-response-loss-", StringComparison.Ordinal)))
            throw new InvalidOperationException("Response-loss acceptance requires its own explicit local commit case.");
        string caseSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(caseBytes));
        if (!root.GetProperty("approved_cases").EnumerateArray().Any(value => value.GetString() == input.CaseId)
            || !string.Equals(root.GetProperty("approved_case_sha256").GetProperty(input.CaseId).GetString(), caseSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Case is not approved for this fixture.");
        FixtureValidation.RequireWindow(root, DateTimeOffset.UtcNow);
        journal.Reserve(input.CaseId);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        RepositoryProviderResult result = await routing.ExecuteAsync(plan.ProjectName, operation, message: input.Message, branch: input.Branch,
            expectedHead: input.ExpectedHead, patch: input.Patch, cancellationToken: timeout.Token);
        FixtureCheck check = lossCase
            ? new FixtureCheck(!result.Ok && result.ResultUnknown && factory.Loss?.MutationDispatches == 1 && factory.Loss.ResponsesDiscarded == 1,
                result.ResultUnknown ? null : "response_loss_not_observed", [])
            : FixtureValidation.Inspect(input, result);
        var evidence = new FixtureEvidence(input, plan.ProjectId, plan.RepositoryId, started, DateTimeOffset.UtcNow,
            result.Ok, result.ErrorCode, result.ResultUnknown, check);
        await File.WriteAllTextAsync(journal.EvidencePath(input.CaseId), JsonSerializer.Serialize(evidence, json));
        verificationPending = check.Passed && operation != RepositoryOperation.SettingsGet;
        success = check.Passed && !verificationPending;
        results.Add(new { input.CaseId, result.Operation, result.Ok, result.ErrorCode, result.ResultUnknown,
            validation = check, verificationPending, expectedResultMatched = check.Passed,
            mutationDispatches = factory.Loss?.MutationDispatches, responsesDiscarded = factory.Loss?.ResponsesDiscarded });
        if (success) journal.Complete(input.CaseId);
    }
    string safe = JsonSerializer.Serialize(new { ok = success, mode = args[0], plan.ProjectId, plan.RepositoryId, normalHostContacted = false, signerCreated = false, results }, json);
    await File.WriteAllTextAsync(Path.Combine(profile, args[0] + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-result.json"), safe);
    Console.WriteLine(safe);
    return success ? 0 : verificationPending ? 2 : 1;
}
catch (Exception exception)
{
    // Transport text, config and discovery content may contain secrets; only stable error/type is emitted.
    Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = exception switch
    { ProviderAuthenticationException auth => auth.Code, UserHostConnectionException discovery => discovery.Code, _ => exception.GetType().Name } }));
    return 1;
}

internal sealed record FixturePlan(Guid ProjectId, string ProjectName, string RepositoryId, string SourcePath, string RepositoryUrl);
internal sealed class FixtureHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly SocketsHttpHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false };
    internal ResponseLossHandler? Loss { get; }
    internal FixtureHttpClientFactory(bool discardCommitResponse = false)
    {
        if (discardCommitResponse) Loss = new ResponseLossHandler("bitbucket_repository_commit", _handler);
    }
    public HttpClient CreateClient(string name) => new(Loss is null ? _handler : Loss, false) { Timeout = TimeSpan.FromSeconds(30) };
    public void Dispose() { Loss?.Dispose(); _handler.Dispose(); }
}
