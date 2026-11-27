using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class UserHostDiscoveryTests
{
    [Fact]
    public void MissingRuntimeIsSafeAndIsNotCreated()
    {
        string path = Path.Combine(Path.GetTempPath(), "moyai-missing-" + Guid.NewGuid().ToString("N"));
        string expected = OperatingSystem.IsWindows() ? "user_host_not_running" : "host_discovery_unavailable";
        Assert.Equal(expected, Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(path, "/mcp")).Code);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void ValidRecordAndAtomicReplacementUseNewCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        fixture.Write();
        using UserHostConnection first = BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp");
        Assert.Equal(fixture.Credential, first.Credential);
        string old = fixture.Credential;
        fixture.Rotate();
        fixture.Write(atomic: true);
        using UserHostConnection second = BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp");
        Assert.Equal(fixture.Credential, second.Credential);
        Assert.NotEqual(old, second.Credential);
        Assert.Equal(first.Generation, second.Generation); // File publication race within one synthetic live process.
    }

    [Theory]
    [InlineData("schema_version", "2", "host_discovery_version_unsupported")]
    [InlineData("schema_version", "null", "host_discovery_version_unsupported")]
    [InlineData("provider_id", "\"githubie\"", "host_provider_mismatch")]
    [InlineData("profile_id", "\"other\"", "host_profile_mismatch")]
    [InlineData("owner_sid", "\"S-1-5-18\"", "host_owner_session_mismatch")]
    [InlineData("session_id", "0", "host_owner_session_mismatch")]
    [InlineData("process_id", "-1", "host_discovery_stale")]
    [InlineData("process_start_utc_ticks", "1", "host_discovery_stale")]
    [InlineData("process_start_utc_ticks", "1.5", "host_discovery_invalid")]
    [InlineData("endpoint", "\"https://127.0.0.1:54321/mcp\"", "host_discovery_invalid")]
    [InlineData("endpoint", "\"http://example.com:54321/mcp\"", "host_discovery_invalid")]
    [InlineData("endpoint", "\"http://user:pass@127.0.0.1:54321/mcp\"", "host_discovery_invalid")]
    [InlineData("endpoint", "\"http://127.0.0.1:54321/other\"", "host_discovery_invalid")]
    [InlineData("endpoint", "\"http://127.0.0.1:54321/mcp?x=1\"", "host_discovery_invalid")]
    [InlineData("endpoint", "\"http://127.0.0.1:54321/a/../mcp\"", "host_discovery_invalid")]
    [InlineData("protected_credential", "\"not-base64\"", "host_discovery_invalid")]
    [InlineData("protected_credential", "\"AQID\"", "host_discovery_unavailable")]
    public void InvalidRecordIsRejectedBeforeHttp(string property, string json, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        fixture.Record[property] = JsonSerializer.Deserialize<JsonElement>(json);
        fixture.Write();
        Assert.Equal(expected, Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp")).Code);
    }

    [Theory]
    [InlineData("profile_id", "\"other\"", "host_profile_mismatch")]
    [InlineData("owner_sid", "\"S-1-5-18\"", "host_owner_session_mismatch")]
    [InlineData("session_id", "0", "host_owner_session_mismatch")]
    public async Task IdentityMismatchThroughProviderNeverCreatesHttpClient(string property, string json, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        fixture.Record[property] = JsonSerializer.Deserialize<JsonElement>(json);
        // 不一致は資格の復号前に拒否されるため、実Hostの資格は不要です。
        fixture.Record["protected_credential"] = "not-a-real-credential";
        fixture.Write();
        var factory = new RejectingHttpFactory();
        var options = new McpRepositoryProviderOptions("buckettie", null, "bitbucket")
        {
            ConnectionMode = "buckettie_user_host_v1", RuntimeDirectory = fixture.Directory
        };
        var provider = new McpRepositoryProvider(options, factory);
        var request = new RepositoryProviderRequest("Fixture", "fixture", "https://bitbucket.org/example/repo.git",
            null, RepositoryOperation.Status, null, null, OperationId: Guid.NewGuid().ToString("N"));

        RepositoryProviderResult result = await provider.ExecuteAsync(request);

        Assert.False(result.Ok);
        Assert.False(result.ResultUnknown);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(0, factory.Created);
    }

    private sealed class RejectingHttpFactory : IHttpClientFactory
    {
        public int Created { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Created++;
            throw new InvalidOperationException("Identity rejection must precede HTTP client creation.");
        }
    }

    [Theory]
    [InlineData("schema_version", "host_discovery_version_unsupported")]
    [InlineData("owner_sid", "host_discovery_invalid")]
    [InlineData("protected_credential", "host_discovery_invalid")]
    public void RequiredFieldsCannotBeOmitted(string property, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        fixture.Record.Remove(property);
        fixture.Write();
        Assert.Equal(expected, Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp")).Code);
    }

    [Fact]
    public void ForeignFileAllowEntryAndUnprotectedRuntimeAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        fixture.Write();
        var file = new FileInfo(Path.Combine(fixture.Directory, "host.json"));
        FileSecurity acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        Assert.Equal("host_discovery_unavailable", Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp")).Code);
        File.Delete(file.FullName);
        fixture.Write();
        var directory = new DirectoryInfo(fixture.Directory);
        DirectorySecurity directoryAcl = directory.GetAccessControl();
        directoryAcl.SetAccessRuleProtection(false, true);
        directory.SetAccessControl(directoryAcl);
        Assert.Equal("host_discovery_unavailable", Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp")).Code);
    }

    [Theory]
    [InlineData("wrong-length")]
    [InlineData("whitespace")]
    [InlineData("duplicate")]
    [InlineData("too-large")]
    [InlineData("too-deep")]
    [InlineData("invalid-json")]
    public void MalformedCredentialAndBoundedRecordAreRejected(string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new DiscoveryFixture();
        if (kind == "wrong-length") fixture.Protect("AQID");
        if (kind == "whitespace") fixture.Protect(" " + fixture.Credential);
        fixture.Write();
        string path = Path.Combine(fixture.Directory, "host.json");
        string text = File.ReadAllText(path);
        if (kind == "duplicate") File.WriteAllText(path, text[..^1] + ",\"schema_version\":1}");
        if (kind == "too-large") File.WriteAllText(path, new string(' ', 65537));
        if (kind == "too-deep") File.WriteAllText(path, new string('[', 10) + "0" + new string(']', 10));
        if (kind == "invalid-json") File.WriteAllText(path, "{");
        Assert.Equal("host_discovery_invalid", Assert.Throws<UserHostConnectionException>(() => BuckettieUserHostResolver.Resolve(fixture.Directory, "/mcp")).Code);
    }

    private sealed class DiscoveryFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "moyai-discovery-" + Guid.NewGuid().ToString("N"));
        public string Credential { get; private set; } = "";
        public Dictionary<string, object> Record { get; }
        [SupportedOSPlatform("windows")]
        public DiscoveryFixture()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User!;
            var acl = new DirectorySecurity();
            acl.SetOwner(owner);
            acl.SetAccessRuleProtection(true, false);
            foreach (SecurityIdentifier sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Directory).Create(acl);
            using Process process = Process.GetCurrentProcess();
            Record = new()
            {
                ["schema_version"] = 1, ["provider_id"] = "buckettie", ["profile_id"] = BuckettieUserHostResolver.ProfileId(Directory),
                ["owner_sid"] = owner.Value, ["session_id"] = process.SessionId, ["process_id"] = process.Id,
                ["process_start_utc_ticks"] = process.StartTime.ToUniversalTime().Ticks,
                ["endpoint"] = "http://127.0.0.1:54321/mcp",
            };
            Rotate();
        }
        [SupportedOSPlatform("windows")]
        public void Rotate() { Credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); Protect(Credential); }
        [SupportedOSPlatform("windows")]
        public void Protect(string value) => Record["protected_credential"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), "Buckettie/UserProfile/v2"u8.ToArray(), DataProtectionScope.CurrentUser));
        public void Write(bool atomic = false)
        {
            string path = Path.Combine(Directory, "host.json");
            File.WriteAllText(atomic ? path + ".tmp" : path, JsonSerializer.Serialize(Record));
            if (atomic) File.Move(path + ".tmp", path, true);
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
