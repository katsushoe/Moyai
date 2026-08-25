using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Persistence;

namespace Moyai.Infrastructure.Persistence;

/// <summary>SQLite Backup APIを使用して整合したBackupを作成します。</summary>
public sealed class SqliteBackupService : IDatabaseBackupService
{
    private readonly SqliteDatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SqliteBackupService(SqliteDatabaseOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        string sourcePath = Path.GetFullPath(builder.DataSource);
        string? sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (sourceDirectory is null) throw new InvalidOperationException("Database directory could not be resolved.");
        string backupDirectory = Path.GetFullPath(_options.BackupDirectory ?? Path.Combine(sourceDirectory, "backups"));
        Directory.CreateDirectory(backupDirectory);
        string timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        string backupPath = Path.Combine(backupDirectory, $"moyai-{timestamp}.db");

        await using var source = new SqliteConnection(_options.ConnectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        return backupPath;
    }
}
