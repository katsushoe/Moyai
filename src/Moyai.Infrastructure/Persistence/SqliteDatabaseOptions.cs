namespace Moyai.Infrastructure.Persistence;

/// <summary>SQLiteデータベース接続設定を表します。</summary>
public sealed record SqliteDatabaseOptions(
    string ConnectionString,
    int BusyTimeoutMilliseconds = 5000,
    string? BackupDirectory = null);
