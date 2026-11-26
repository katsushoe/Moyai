using System.Security.Cryptography;

namespace Moyai.Configuration;

/// <summary>秘密値や比較ハッシュを含まない、読み込み済み設定の状態です。</summary>
public sealed record HostConfigurationState(string ConfigPath, string DatabasePath, DateTimeOffset LoadedAtUtc,
    DateTimeOffset? LoadedFileWriteTimeUtc, DateTimeOffset? CurrentFileWriteTimeUtc, string Status, bool? RestartRequired);

/// <summary>実際に読み込んだ内容を内部比較し、設定の自動再読込は行いません。</summary>
public sealed class LoadedConfigurationSnapshot
{
    private const int MaximumComparisonBytes = 4 * 1024 * 1024;
    private readonly string _path;
    private readonly string _databasePath;
    private readonly string _apiToken;
    private readonly byte[] _fingerprint;
    private readonly DateTimeOffset _loadedAt;
    private readonly DateTimeOffset? _loadedWriteTime;

    internal LoadedConfigurationSnapshot(string path, string databasePath, ReadOnlySpan<byte> loadedBytes, string apiToken)
    {
        _path = path;
        _databasePath = databasePath;
        _apiToken = apiToken;
        _fingerprint = SHA256.HashData(loadedBytes);
        _loadedAt = DateTimeOffset.UtcNow;
        try { _loadedWriteTime = File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { _loadedWriteTime = null; }
    }

    /// <summary>読取結果だけを返し、現在ファイルの秘密値やfingerprintを公開しません。</summary>
    public HostConfigurationState Read()
    {
        DateTimeOffset? currentWrite = null;
        HostConfigurationState Result(string status, bool? restart) => new(Redact(_path), Redact(_databasePath), _loadedAt, _loadedWriteTime, currentWrite, status, restart);
        try
        {
            _ = File.GetAttributes(_path);
            currentWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(_path));
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > MaximumComparisonBytes) return Result("unavailable", null);
            byte[] bytes = new byte[checked((int)file.Length)];
            try
            {
                file.ReadExactly(bytes);
                if (file.ReadByte() != -1) return Result("unavailable", null);
                bool same = CryptographicOperations.FixedTimeEquals(_fingerprint, SHA256.HashData(bytes));
                return Result(same ? "unchanged" : "restart_required", !same);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (FileNotFoundException) { return Result("missing", null); }
        catch (DirectoryNotFoundException) { return Result("missing", null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Result("unavailable", null); }
    }

    /// <summary>公開メタデータに含まれた既知のAPI tokenも伏せます。</summary>
    public string Redact(string value) => string.IsNullOrEmpty(_apiToken) ? value : value.Replace(_apiToken, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
}
