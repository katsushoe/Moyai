namespace Moyai.Application.Persistence;

/// <summary>作成済みDatabase Backupの情報を表します。</summary>
public sealed record DatabaseBackupInfo(
    string Path,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    bool IsValid);
