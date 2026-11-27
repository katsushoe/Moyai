using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class GithubieUserHostResolverTests
{
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ValidProfileResolvesAndRuntimePathIsVerified()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        using UserHostConnection result = GithubieUserHostResolver.Resolve(fixture.Root, fixture.Runtime);
        Assert.Equal(fixture.Credential, result.Credential);
        Assert.Equal(fixture.Generation, result.Generation);
        string otherRuntime = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.Equal("discovery_runtime_mismatch", Assert.Throws<UserHostConnectionException>(() =>
            GithubieUserHostResolver.Resolve(fixture.Root, otherRuntime)).Code);
        Assert.Equal("discovery_runtime_unavailable", Assert.Throws<UserHostConnectionException>(() =>
            GithubieUserHostResolver.Resolve(fixture.Root, Path.Combine(fixture.Root, "missing.exe"))).Code);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void UpdatedRuntimeContentAtSamePathStillResolves()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(new string('C', 64));
        using UserHostConnection result = GithubieUserHostResolver.Resolve(fixture.Root, fixture.Runtime);
        Assert.Equal(fixture.Credential, result.Credential);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ModifiedProfileAndForeignAclAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        File.AppendAllText(Path.Combine(fixture.Root, "profile.json"), " ");
        Assert.Equal("discovery_profile_mismatch", Assert.Throws<UserHostConnectionException>(() =>
            GithubieUserHostResolver.Resolve(fixture.Root, fixture.Runtime)).Code);
        fixture.WriteProfile();
        string path = Path.Combine(fixture.Root, "data", "connection.json");
        var file = new FileInfo(path);
        FileSecurity acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        Assert.Equal("discovery_acl_rejected", Assert.Throws<UserHostConnectionException>(() =>
            GithubieUserHostResolver.Resolve(fixture.Root, fixture.Runtime)).Code);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "moyai-githubie-discovery-" + Guid.NewGuid().ToString("N"));
        public string Runtime { get; }
        public string RuntimeHash { get; }
        public string Credential { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public string Generation { get; } = Guid.NewGuid().ToString("N");
        private Guid ProfileId { get; } = Guid.NewGuid();
        private string Sid { get; }

        public Fixture(string? publishedRuntimeHash = null)
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User!;
            Sid = owner.Value;
            Restrict(Root, owner);
            string data = Path.Combine(Root, "data");
            Restrict(data, owner);
            using Process process = Process.GetCurrentProcess();
            Runtime = process.MainModule!.FileName;
            RuntimeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Runtime)));
            WriteProfile();
            byte[] blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(Credential), "Githubie/UserHost/Connection/v1"u8.ToArray(), DataProtectionScope.CurrentUser);
            byte[] envelope = [.. "Githubie.UserDPAPI.v2\0"u8, .. blob];
            var record = new JsonObject
            {
                ["schema_version"] = 1, ["provider_id"] = "githubie", ["profile_id"] = ProfileId.ToString(), ["owner_sid"] = Sid,
                ["session_id"] = process.SessionId, ["process_id"] = process.Id, ["process_start_utc_ticks"] = process.StartTime.ToUniversalTime().Ticks,
                ["endpoint"] = "http://127.0.0.1:54321/mcp", ["generation"] = Generation, ["runtime_path"] = Runtime,
                ["runtime_sha256"] = publishedRuntimeHash ?? RuntimeHash, ["profile_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "profile.json")))),
                ["protected_credential"] = Convert.ToBase64String(envelope),
            };
            File.WriteAllText(Path.Combine(data, "connection.json"), record.ToJsonString());
        }

        public void WriteProfile() => File.WriteAllText(Path.Combine(Root, "profile.json"), JsonSerializer.Serialize(new { schema_version = 1, profile_id = ProfileId, owner_sid = Sid }));
        private static void Restrict(string path, SecurityIdentifier owner)
        {
            var security = new DirectorySecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(security);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
