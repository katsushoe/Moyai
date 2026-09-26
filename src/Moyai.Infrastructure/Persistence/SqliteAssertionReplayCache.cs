using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Persistence;

/// <summary>再起動・複数Consumerに耐えるReplay Cacheです。</summary>
public sealed class SqliteAssertionReplayCache(SqliteDatabaseOptions options, TimeProvider clock) : IAssertionReplayCache
{
    public async Task<bool> TryUseAsync(string issuer, string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM assertion_replay WHERE expires_at <= $now;";
            command.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO assertion_replay(issuer, jti_hash, expires_at) VALUES($issuer,$jti,$expires) ON CONFLICT(issuer,jti_hash) DO NOTHING;";
            command.Parameters.AddWithValue("$issuer", issuer);
            command.Parameters.AddWithValue("$jti", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jti))));
            command.Parameters.AddWithValue("$expires", expiresAt.ToUnixTimeSeconds());
            bool inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return inserted;
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }
}
