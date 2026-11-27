using System.Security.Cryptography;
using System.Text.Json;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Verifies the complete dedicated Consumer payload against an agreed manifest.</summary>
internal static class RestartCandidateManifest
{
    internal static void Verify(string root, string manifest, string manifestSha256, int fileCount, string consoleName, string consoleSha256)
    {
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(manifest) || fileCount <= 0 || fileCount > 1024)
            throw new InvalidOperationException("Invalid candidate pins.");
        string basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string prefix = basePath + Path.DirectorySeparatorChar;
        CheckNoReparse(new DirectoryInfo(basePath));
        byte[] manifestBytes = File.ReadAllBytes(manifest);
        if (!DigestMatches(SHA256.HashData(manifestBytes), manifestSha256)) throw new InvalidOperationException("Manifest hash mismatch.");
        using JsonDocument document = JsonDocument.Parse(manifestBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != fileCount)
            throw new InvalidOperationException("Manifest count mismatch.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? console = null;
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string relative = entry.GetProperty("path").GetString() ?? throw new InvalidOperationException("Missing candidate path.");
            string full = SafePath(basePath, prefix, relative);
            if (!expected.Add(full)) throw new InvalidOperationException("Duplicate candidate path.");
            var file = new FileInfo(full);
            if (!file.Exists || file.Length != entry.GetProperty("bytes").GetInt64()) throw new InvalidOperationException("Candidate length mismatch.");
            CheckNoReparse(file);
            if (!HashMatches(full, entry.GetProperty("sha256").GetString())) throw new InvalidOperationException("Candidate hash mismatch.");
            if (string.Equals(relative, consoleName, StringComparison.OrdinalIgnoreCase)) console = full;
        }
        if (console is null || !HashMatches(console, consoleSha256)) throw new InvalidOperationException("Consumer executable mismatch.");
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>();
        directories.Push(basePath);
        while (directories.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Candidate contains a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                else observed.Add(Path.GetFullPath(path));
            }
        }
        if (!observed.SetEquals(expected)) throw new InvalidOperationException("Candidate file set mismatch.");
    }

    private static string SafePath(string root, string prefix, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Split(['/', '\\']).Any(part => part is "" or "." or ".."))
            throw new InvalidOperationException("Invalid candidate path.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Candidate path escapes root.");
        return full;
    }

    private static void CheckNoReparse(FileSystemInfo entry)
    {
        for (FileSystemInfo? current = entry; current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Candidate path contains a reparse point.");
    }

    private static bool HashMatches(string path, string? expected)
    {
        using var input = File.OpenRead(path);
        return DigestMatches(SHA256.HashData(input), expected);
    }

    private static bool DigestMatches(byte[] actual, string? expected) => expected is { Length: 64 }
        && expected.All(Uri.IsHexDigit) && CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
}
