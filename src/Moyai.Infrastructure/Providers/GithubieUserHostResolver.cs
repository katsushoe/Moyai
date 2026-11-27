using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Moyai.Infrastructure.Providers;

/// <summary>固定された実行ファイルとユーザーprofileの接続情報を検証します。</summary>
internal static class GithubieUserHostResolver
{
    /// <remarks>実行ファイルの内容ハッシュは照合しません。Githubieの更新後も同じパスで動作し、パス・所有者・セッション・生存・ACLの検証で接続先を確認します。</remarks>
    internal static UserHostConnection Resolve(string profileDirectory, string runtimePath)
    {
        if (!OperatingSystem.IsWindows()) throw new UserHostConnectionException("discovery_runtime_unavailable");
        return ResolveWindows(profileDirectory, runtimePath);
    }

    [SupportedOSPlatform("windows")]
    private static UserHostConnection ResolveWindows(string profileDirectory, string runtimePath)
    {
        string root = Path.GetFullPath(profileDirectory);
        string data = Path.Combine(root, "data");
        string profile = Path.Combine(root, "profile.json");
        string discovery = Path.Combine(data, "connection.json");
        string selectedRuntime = Path.GetFullPath(runtimePath);
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new UserHostConnectionException("discovery_profile_mismatch");
            ValidatePrivate(root, owner, true);
            ValidatePrivate(data, owner, true);
            ValidatePrivate(profile, owner, false);
            ValidatePrivate(discovery, owner, false);
            try { ValidatePath(selectedRuntime); }
            catch (FileNotFoundException) { throw new UserHostConnectionException("discovery_runtime_unavailable"); }
            catch (DirectoryNotFoundException) { throw new UserHostConnectionException("discovery_runtime_unavailable"); }
            if (new FileInfo(profile).Length is < 1 or > 65536) throw new UserHostConnectionException("discovery_invalid");
            byte[] profileBytes = File.ReadAllBytes(profile);
            using JsonDocument profileDocument = JsonDocument.Parse(profileBytes, new JsonDocumentOptions { MaxDepth = 8 });
            BuckettieUserHostResolver.ValidateProperties(profileDocument.RootElement);
            JsonElement profileRoot = profileDocument.RootElement;
            if (profileRoot.GetProperty("schema_version").GetInt32() != 1
                || profileRoot.GetProperty("owner_sid").GetString() != owner.Value
                || !Guid.TryParse(profileRoot.GetProperty("profile_id").GetString(), out Guid profileId) || profileId == Guid.Empty)
                throw new UserHostConnectionException("discovery_profile_mismatch");
            using var stream = new FileStream(discovery, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is < 1 or > 65536) throw new UserHostConnectionException("discovery_invalid");
            byte[] json = new byte[checked((int)stream.Length)];
            stream.ReadExactly(json);
            GithubieDiscoveryRecord record = GithubieDiscoveryRecord.Parse(json);
            using Process current = Process.GetCurrentProcess();
            if (record.ProfileId != profileId || record.OwnerSid != owner.Value || record.SessionId != current.SessionId
                || record.ProfileSha256 != Convert.ToHexString(SHA256.HashData(profileBytes)))
                throw new UserHostConnectionException("discovery_profile_mismatch");
            if (!string.Equals(record.RuntimePath, selectedRuntime, StringComparison.OrdinalIgnoreCase))
                throw new UserHostConnectionException("discovery_runtime_mismatch");
            ValidateProcess(record, selectedRuntime, owner);
            byte[] protectedBytes = Convert.FromBase64String(record.ProtectedCredential);
            byte[]? plaintext = null;
            try
            {
                ReadOnlySpan<byte> prefix = "Githubie.UserDPAPI.v2\0"u8;
                if (!protectedBytes.AsSpan().StartsWith(prefix)) throw new UserHostConnectionException("discovery_credential_invalid");
                plaintext = ProtectedData.Unprotect(protectedBytes[prefix.Length..], "Githubie/UserHost/Connection/v1"u8.ToArray(), DataProtectionScope.CurrentUser);
                string credential = new UTF8Encoding(false, true).GetString(plaintext);
                byte[] decoded = Convert.FromBase64String(credential);
                try { if (decoded.Length != 32 || Convert.ToBase64String(decoded) != credential) throw new UserHostConnectionException("discovery_credential_invalid"); }
                finally { CryptographicOperations.ZeroMemory(decoded); }
                ValidateProcess(record, selectedRuntime, owner);
                ValidatePrivate(root, owner, true);
                ValidatePrivate(data, owner, true);
                ValidatePrivate(profile, owner, false);
                ValidatePrivate(discovery, owner, false);
                if (HashFile(profile) != record.ProfileSha256)
                    throw new UserHostConnectionException("discovery_profile_mismatch");
                return new UserHostConnection(record.Endpoint, credential, record.Generation);
            }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (UserHostConnectionException exception) when (exception.Code == "host_discovery_invalid") { throw new UserHostConnectionException("discovery_invalid"); }
        catch (FileNotFoundException) { throw new UserHostConnectionException("discovery_missing"); }
        catch (DirectoryNotFoundException) { throw new UserHostConnectionException("discovery_missing"); }
        catch (UnauthorizedAccessException) { throw new UserHostConnectionException("discovery_acl_rejected"); }
        catch (CryptographicException) { throw new UserHostConnectionException("discovery_credential_invalid"); }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or DecoderFallbackException or OverflowException)
        { throw new UserHostConnectionException("discovery_invalid"); }
    }

    private static string HashFile(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    [SupportedOSPlatform("windows")]
    private static void ValidateProcess(GithubieDiscoveryRecord record, string runtime, SecurityIdentifier owner)
    {
        try
        {
            using Process process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited || process.SessionId != record.SessionId || process.StartTime.ToUniversalTime().Ticks != record.ProcessStartUtcTicks)
                throw new UserHostConnectionException("discovery_host_stale");
            if (!string.Equals(process.MainModule?.FileName, runtime, StringComparison.OrdinalIgnoreCase))
                throw new UserHostConnectionException("discovery_runtime_mismatch");
            if (!OpenProcessToken(process.Handle, 8, out IntPtr token)) throw new UserHostConnectionException("discovery_runtime_unavailable");
            try { using var processIdentity = new WindowsIdentity(token); if (processIdentity.User is not { } processOwner || !owner.Equals(processOwner)) throw new UserHostConnectionException("discovery_profile_mismatch"); }
            finally { CloseHandle(token); }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { throw new UserHostConnectionException("discovery_host_stale"); }
        catch (System.ComponentModel.Win32Exception) { throw new UserHostConnectionException("discovery_runtime_unavailable"); }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidatePrivate(string path, SecurityIdentifier owner, bool directory)
    {
        ValidatePath(path);
        FileSystemSecurity security = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        if (!owner.Equals(security.GetOwner(typeof(SecurityIdentifier))) || directory && !security.AreAccessRulesProtected)
            throw new UserHostConnectionException("discovery_acl_rejected");
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (rules.Length == 0 || rules.Any(rule => !owner.Equals(rule.IdentityReference) || rule.AccessControlType != AccessControlType.Allow))
            throw new UserHostConnectionException("discovery_acl_rejected");
    }

    private static void ValidatePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new UserHostConnectionException("discovery_reparse_rejected");
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
