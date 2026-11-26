using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Tests.Authentication;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class BranchProtectionRoutingTests
{
    [Theory]
    [InlineData("success", 1, true, "succeeded")]
    [InlineData("audit-before", 0, false, "not_executed")]
    [InlineData("audit-after", 1, false, "unknown")]
    [InlineData("missing-audit", 0, false, "not_executed")]
    [InlineData("expired-assertion", 1, false, "not_executed")]
    public async Task MutationRequiresDurableAuditAndNeverReplays(string scenario, int calls, bool ok, string outcome)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var projects = new SqliteProjectRepository(fixture.Database);
        var service = new ProjectService(projects, fixture.Clock);
        await service.CreateAsync(new("fixture", "source", RepositoryUrl: "https://github.com/example/repo", RepositoryProvider: "githubie"));
        var provider = new Provider { Scenario = scenario };
        var audit = new Audit { FailureOn = scenario == "audit-before" ? 1 : scenario == "audit-after" ? 2 : 0 };
        var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(fixture.Database), [provider], fixture.Clock,
            operationAudit: scenario == "missing-audit" ? null : audit);
        var result = await routing.ExecuteBranchProtectionAsync("fixture", "main", RepositoryOperation.BranchProtectionUpdate, new(EnforceAdmins: true));
        Assert.Equal(calls, provider.Calls);
        Assert.Equal(ok, result.Ok);
        Assert.Equal(outcome, result.Ok ? audit.Entries[^1].Outcome : result.Error!.Outcome);
        if (scenario == "success")
        {
            Assert.Equal(2, audit.Entries.Count);
            Assert.Equal("pending", audit.Entries[0].Outcome);
            Assert.Equal(provider.OperationId, audit.Entries[0].OperationId);
            Assert.All(audit.Entries, entry => { Assert.Equal(64, entry.BranchFingerprint.Length); Assert.DoesNotContain("main", entry.BranchFingerprint, StringComparison.Ordinal); });
        }
    }

    [Fact]
    public async Task SqliteAuditStoresIntentAndResultAndNoPayload()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var audit = new SqliteRepositoryOperationAudit(fixture.Database, fixture.Clock);
        var intent = new RepositoryOperationAuditEntry("id", Guid.NewGuid(), "githubie", "branch_protection_delete", new string('A', 64), "not_executed", "dispatch_planned");
        await audit.WriteAsync(intent);
        await audit.WriteAsync(intent with { Outcome = "succeeded", Code = "changed" });
        await using var connection = new SqliteConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM repository_operation_audit WHERE operation_id='id';";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('repository_operation_audit') WHERE name IN ('payload','policy','token','branch');";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MissingAuditDatabaseIsNotInitialized()
    {
        string path = Path.Combine(Path.GetTempPath(), "moyai-missing-audit-" + Guid.NewGuid().ToString("N") + ".db");
        var audit = new SqliteRepositoryOperationAudit(new("Data Source=" + path), TimeProvider.System);
        await Assert.ThrowsAsync<SqliteException>(() => audit.WriteAsync(new("id", Guid.NewGuid(), "githubie", "branch_protection_delete", new string('A', 64), "pending", "dispatch_planned")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GenericApplicationEntryCannotBypassAudit()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var projects = new SqliteProjectRepository(fixture.Database);
        await new ProjectService(projects, fixture.Clock).CreateAsync(new("fixture", "source", RepositoryUrl: "https://github.com/example/repo", RepositoryProvider: "githubie"));
        var provider = new Provider();
        var routing = new ProviderRoutingService(projects, new SqliteServiceTokenRepository(fixture.Database), [provider], fixture.Clock);
        var result = await routing.ExecuteAsync("fixture", RepositoryOperation.BranchProtectionUpdate, branch: "main", protectionEdit: new(EnforceAdmins: true));
        Assert.False(result.Ok);
        Assert.Equal("operation_audit_unavailable", result.ErrorCode);
        Assert.Equal(0, provider.Calls);
    }

    private sealed class Audit : IRepositoryOperationAudit
    {
        public int FailureOn { get; init; }
        private int _calls;
        public List<RepositoryOperationAuditEntry> Entries { get; } = [];
        public Task WriteAsync(RepositoryOperationAuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (++_calls == FailureOn) throw new IOException("fixture audit unavailable");
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class Provider : IRepositoryProvider
    {
        public string Name => "githubie";
        public string Scenario { get; init; } = "success";
        public int Calls { get; private set; }
        public string? OperationId { get; private set; }
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            OperationId = request.OperationId;
            return Task.FromResult(Scenario == "expired-assertion"
                ? new RepositoryProviderResult(false, request.Operation.ContractName(), null, "auth_assertion_expired", null)
                : new(true, request.Operation.ContractName(), "{\"ok\":true,\"data\":{\"branch\":\"main\",\"changed\":true,\"policy\":" + BranchProtectionContractTests.Policy + "}}", null, null));
        }
    }
}
