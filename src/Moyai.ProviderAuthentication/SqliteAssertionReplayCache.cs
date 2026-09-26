using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Moyai.ProviderAuthentication;

/// <summary>Replay DBの明示設定です。</summary>
public sealed record SqliteAssertionReplayCacheOptions(string DatabasePath, int BusyTimeoutSeconds = 5)
{
    /// <summary>設定値を検証します。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath)
            || !Path.IsPathFullyQualified(DatabasePath)
            || BusyTimeoutSeconds is < 1 or > 60)
        {
            throw new ProviderAuthenticationException("authentication_unavailable");
        }
    }
}

/// <summary>Moyai本体DBから独立したSQLite Replay Cacheです。</summary>
public sealed class SqliteAssertionReplayCache(
    SqliteAssertionReplayCacheOptions options,
    TimeProvider? clock = null) : IAssertionReplayCache
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Replay DBと専用Schemaを冪等に初期化します。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        try
        {
            string? directory = Path.GetDirectoryName(options.DatabasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ProviderAuthenticationException("authentication_unavailable");
            }

            Directory.CreateDirectory(directory);
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS assertion_replay (
                    issuer TEXT NOT NULL,
                    jti_hash BLOB NOT NULL,
                    expires_at INTEGER NOT NULL,
                    PRIMARY KEY (issuer, jti_hash)
                ) WITHOUT ROWID;
                CREATE INDEX IF NOT EXISTS assertion_replay_expiry ON assertion_replay(expires_at);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw new ProviderAuthenticationException("authentication_unavailable");
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryUseAsync(
        string issuer,
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);
        options.Validate();
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM assertion_replay WHERE expires_at <= $now;";
            command.Parameters.AddWithValue("$now", _clock.GetUtcNow().ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO assertion_replay(issuer, jti_hash, expires_at)
                VALUES($issuer, $jti_hash, $expires_at)
                ON CONFLICT(issuer, jti_hash) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$issuer", issuer);
            command.Parameters.Add("$jti_hash", SqliteType.Blob).Value = SHA256.HashData(Encoding.UTF8.GetBytes(jti));
            command.Parameters.AddWithValue("$expires_at", expiresAt.ToUnixTimeSeconds());
            bool inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return inserted;
        }
        catch (SqliteException)
        {
            throw new ProviderAuthenticationException("authentication_unavailable");
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = options.BusyTimeoutSeconds,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
