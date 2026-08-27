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
        string backupDirectory = ResolveBackupDirectory();
        Directory.CreateDirectory(backupDirectory);
        string timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        string backupPath = Path.Combine(backupDirectory, $"moyai-{timestamp}.db");

        await using var source = new SqliteConnection(_options.ConnectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        await destination.CloseAsync().ConfigureAwait(false);
        if (!await IsValidAsync(backupPath, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(backupPath);
            throw new InvalidDataException("Created database backup failed integrity validation.");
        }

        return backupPath;
    }

    public async Task<IReadOnlyList<DatabaseBackupInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        string backupDirectory = ResolveBackupDirectory();
        if (!Directory.Exists(backupDirectory)) return [];

        var backups = new List<DatabaseBackupInfo>();
        foreach (string path in Directory.EnumerateFiles(backupDirectory, "moyai-*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            bool isValid = await IsValidAsync(path, cancellationToken).ConfigureAwait(false);
            backups.Add(new DatabaseBackupInfo(file.FullName, file.LastWriteTimeUtc, file.Length, isValid));
        }

        return backups.OrderByDescending(static backup => backup.CreatedAt).ToArray();
    }

    private string ResolveBackupDirectory()
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        string sourcePath = Path.GetFullPath(builder.DataSource);
        string? sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (sourceDirectory is null) throw new InvalidOperationException("Database directory could not be resolved.");
        return Path.GetFullPath(_options.BackupDirectory ?? Path.Combine(sourceDirectory, "backups"));
    }

    private static async Task<bool> IsValidAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(Convert.ToString(result, CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal);
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
