using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Authentication;

/// <summary>既存DBの公開状態だけを読み、初期化・鍵作成・署名をしません。</summary>
public sealed class SqliteSigningReadiness(SqliteDatabaseOptions database, string issuer, string keyNamespace,
    string initialVersion, TimeProvider clock, Func<string, CancellationToken, Task<bool>>? protectorAvailable = null) : ISigningReadiness
{
    public async Task<SigningReadiness> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issuer)) return new("issuer_not_configured");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder(database.ConnectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 };
            await using var connection = new SqliteConnection(connectionString.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT not_before,not_after FROM assertion_keys WHERE issuer=$issuer AND state='Active' LIMIT 2;";
            command.Parameters.AddWithValue("$issuer", issuer);
            long start;
            long end;
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new("active_key_missing", false, false);
                start = reader.GetInt64(0);
                end = reader.GetInt64(1);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new("active_key_ambiguous", true, null);
            }
            long now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (end <= now) return new("active_key_expired", true, false);
            if (start > now) return new("active_key_not_yet_valid", true, false);
            command.CommandText = "SELECT active_version FROM protector_state WHERE key_namespace=$namespace;";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$namespace", keyNamespace);
            string version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? initialVersion;
            if (protectorAvailable is null) return new("configured_not_verified", true, true);
            bool available = await protectorAvailable(version, cancellationToken).ConfigureAwait(false);
            return new(available ? "configured" : "protector_unavailable", true, true, available);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or ProviderAuthenticationException or InvalidOperationException)
        { return new("metadata_unavailable"); }
    }
}
