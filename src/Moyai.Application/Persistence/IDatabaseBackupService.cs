namespace Moyai.Application.Persistence;

/// <summary>データベースのBackupを作成します。</summary>
public interface IDatabaseBackupService
{
    /// <summary>現在のデータベースをBackupし、作成先を返します。</summary>
    Task<string> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>設定済みBackup領域にあるBackupを返します。</summary>
    Task<IReadOnlyList<DatabaseBackupInfo>> ListAsync(CancellationToken cancellationToken = default);
}
