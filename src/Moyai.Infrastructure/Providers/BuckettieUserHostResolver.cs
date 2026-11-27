using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Moyai.Infrastructure.Providers;

/// <summary>Providerの接続情報を安全なエラーコードで拒否します。</summary>
internal sealed class UserHostConnectionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>接続世代ごとの秘密を保持します。文字列表現には秘密を含めません。</summary>
internal sealed class UserHostConnection(Uri endpoint, string credential, string generation) : IDisposable
{
    public Uri Endpoint { get; } = endpoint;
    public string Credential { get; private set; } = credential;
    public string Generation { get; } = generation;
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(Credential)) return text;
        string escaped = JsonSerializer.Serialize(Credential)[1..^1];
        return text?.Replace(Credential, "[REDACTED]", StringComparison.Ordinal).Replace(escaped, "[REDACTED]", StringComparison.Ordinal);
    }
    public void Dispose() => Credential = string.Empty;
    public override string ToString() => nameof(UserHostConnection);
}

/// <summary>Buckettie discovery schema 1を読み取ります。ファイルやACLを変更しません。</summary>
internal static class BuckettieUserHostResolver
{
    internal static void ValidateMcpPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
            || path.IndexOfAny(['?', '#', '\\']) >= 0
            || !Uri.TryCreate("http://127.0.0.1" + path, UriKind.Absolute, out Uri? uri) || uri.AbsolutePath != path)
            throw new ArgumentException("Invalid expected MCP path.", nameof(path));
    }

    internal static string ProfileId(string directory) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant()))).ToLowerInvariant();

    internal static UserHostConnection Resolve(string directory, string expectedPath)
    {
        if (!OperatingSystem.IsWindows()) throw new UserHostConnectionException("host_discovery_unavailable");
        return ResolveWindows(directory, expectedPath);
    }

    [SupportedOSPlatform("windows")]
    private static UserHostConnection ResolveWindows(string directory, string expectedPath)
    {
        ValidateMcpPath(expectedPath);
        string path = Path.Combine(directory, "host.json");
        try
        {
            // Exists() masks access-denied failures as missing; preserve the stable unavailable error.
            if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
                throw new UserHostConnectionException("host_discovery_invalid");
            _ = File.GetAttributes(path);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new UserHostConnectionException("host_owner_session_mismatch");
            using Process self = Process.GetCurrentProcess();
            ValidateSecurity(directory, path, owner);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is < 1 or > 65536) throw new UserHostConnectionException("host_discovery_invalid");
            byte[] json = new byte[checked((int)stream.Length)];
            stream.ReadExactly(json);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            ValidateProperties(root);
            if (root.ValueKind != JsonValueKind.Object) throw new UserHostConnectionException("host_discovery_invalid");
            if (!root.TryGetProperty("schema_version", out JsonElement version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int schema) || schema != 1) throw new UserHostConnectionException("host_discovery_version_unsupported");
            string provider = ReadString(root, "provider_id");
            string profile = ReadString(root, "profile_id");
            string sid = ReadString(root, "owner_sid");
            int session = ReadInt32(root, "session_id");
            int pid = ReadInt32(root, "process_id");
            if (!root.TryGetProperty("process_start_utc_ticks", out JsonElement ticksElement) || !ticksElement.TryGetInt64(out long ticks))
                throw new UserHostConnectionException("host_discovery_invalid");
            string endpoint = ReadString(root, "endpoint");
            string protectedCredential = ReadString(root, "protected_credential");
            if (provider != "buckettie") throw new UserHostConnectionException("host_provider_mismatch");
            if (profile != ProfileId(directory)) throw new UserHostConnectionException("host_profile_mismatch");
            if (sid != owner.Value || self.SessionId <= 0 || session != self.SessionId)
                throw new UserHostConnectionException("host_owner_session_mismatch");
            ValidateProcess(pid, session, ticks);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != "http" || !uri.IsLoopback
                || uri.Port <= 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || uri.AbsolutePath != expectedPath || uri.OriginalString.Contains('\\'))
                throw new UserHostConnectionException("host_discovery_invalid");
            // Reject URI normalization of dot segments, including escaped dot segments.
            int pathStart = endpoint.IndexOf('/', endpoint.IndexOf("://", StringComparison.Ordinal) + 3);
            if (pathStart < 0 || !string.Equals(endpoint[pathStart..], expectedPath, StringComparison.Ordinal))
                throw new UserHostConnectionException("host_discovery_invalid");
            byte[] cipher = CanonicalBase64(protectedCredential, null);
            byte[]? plain = null;
            try
            {
                plain = ProtectedData.Unprotect(cipher, "Buckettie/UserProfile/v2"u8.ToArray(), DataProtectionScope.CurrentUser);
                string credential = new UTF8Encoding(false, true).GetString(plain);
                byte[] decoded = CanonicalBase64(credential, 32);
                CryptographicOperations.ZeroMemory(decoded);
                ValidateProcess(pid, session, ticks);
                ValidateSecurity(directory, path, owner);
                return new UserHostConnection(uri, credential, $"{sid}|{session}|{pid}|{ticks}");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cipher);
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (UserHostConnectionException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or DecoderFallbackException or OverflowException or EndOfStreamException)
        { throw new UserHostConnectionException("host_discovery_invalid"); }
        catch (FileNotFoundException) { throw new UserHostConnectionException("user_host_not_running"); }
        catch (DirectoryNotFoundException) { throw new UserHostConnectionException("user_host_not_running"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or System.Security.SecurityException)
        { throw new UserHostConnectionException("host_discovery_unavailable"); }
    }

    internal static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new UserHostConnectionException("host_discovery_invalid");
                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) ValidateProperties(item);
        else if (element.ValueKind == JsonValueKind.Undefined) throw new UserHostConnectionException("host_discovery_invalid");
    }

    private static string ReadString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())
        ? value.GetString()! : throw new UserHostConnectionException("host_discovery_invalid");

    private static int ReadInt32(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : throw new UserHostConnectionException("host_discovery_invalid");

    private static byte[] CanonicalBase64(string text, int? length)
    {
        byte[] bytes = Convert.FromBase64String(text);
        if ((length is not null && bytes.Length != length) || Convert.ToBase64String(bytes) != text)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new UserHostConnectionException("host_discovery_invalid");
        }
        return bytes;
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateProcess(int pid, int session, long ticks)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (pid <= 0 || process.HasExited || process.SessionId != session || process.StartTime.ToUniversalTime().Ticks != ticks)
                throw new UserHostConnectionException("host_discovery_stale");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { throw new UserHostConnectionException("host_discovery_stale"); }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateSecurity(string directory, string file, SecurityIdentifier owner)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(directory)); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException();
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException();
        DirectorySecurity directoryAcl = new DirectoryInfo(directory).GetAccessControl();
        FileSecurity fileAcl = new FileInfo(file).GetAccessControl();
        if (!directoryAcl.AreAccessRulesProtected) throw new UnauthorizedAccessException();
        foreach (FileSystemSecurity acl in new FileSystemSecurity[] { directoryAcl, fileAcl })
        {
            if (!owner.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) throw new UnauthorizedAccessException();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !owner.Equals(rule.IdentityReference)
                    && !new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(rule.IdentityReference))
                    throw new UnauthorizedAccessException();
        }
        bool ownerFullControl = directoryAcl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow && owner.Equals(rule.IdentityReference)
                && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
        if (!ownerFullControl) throw new UnauthorizedAccessException();
    }
}
