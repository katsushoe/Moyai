using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Domain.Authentication;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Service TokenをSQLiteへ永続化します。</summary>
public sealed class SqliteServiceTokenRepository : IServiceTokenRepository
{
    private readonly SqliteDatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SqliteServiceTokenRepository(SqliteDatabaseOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task AddAsync(ServiceToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        const string sql = """
            INSERT INTO service_tokens(id, token, audience, scopes_json, issued_at, expires_at, last_used_at)
            VALUES ($id, $token, $audience, $scopes, $issued, $expires, NULL);
            """;
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", token.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$token", token.Token);
        command.Parameters.AddWithValue("$audience", token.Audience);
        command.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(token.Scopes));
        command.Parameters.AddWithValue("$issued", Format(token.IssuedAt));
        command.Parameters.AddWithValue("$expires", token.ExpiresAt is null ? DBNull.Value : Format(token.ExpiresAt.Value));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        const string sql = "SELECT id, token, audience, scopes_json, issued_at, expires_at, last_used_at FROM service_tokens WHERE token = $token;";
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$token", token);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        string scopesJson = reader.GetString(3);
        string[] scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? [];
        return ServiceToken.Restore(
            Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            reader.GetString(1), reader.GetString(2), scopes,
            Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : Parse(reader.GetString(6)));
    }

    public Task<ServiceToken?> FindByAudienceAsync(string audience, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return FindAsync("audience = $value ORDER BY issued_at DESC LIMIT 1", audience, cancellationToken);
    }

    public async Task UpdateLastUsedAtAsync(Guid id, DateTimeOffset lastUsedAt, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE service_tokens SET last_used_at = $last_used WHERE id = $id;";
        await ExecuteAsync(sql, id, "$last_used", Format(lastUsedAt), cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM service_tokens WHERE expires_at IS NOT NULL AND expires_at <= $now;";
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", Format(now));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("DELETE FROM service_tokens WHERE id = $id;", id, null, null, cancellationToken);

    public Task IssueWithEventAsync(ServiceToken token, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        ReplaceWithEventAsync(token, "service_token_issued", actorType, actorName, deleteExisting: false, cancellationToken);

    public Task RotateWithEventAsync(ServiceToken token, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        ReplaceWithEventAsync(token, "service_token_rotated", actorType, actorName, deleteExisting: true, cancellationToken);

    public async Task<bool> RevokeWithEventAsync(string audience, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (Guid Id, string Audience)? token = await FindIdentityAsync(connection, transaction, "audience = $value", audience, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
            await DeleteAudienceAsync(connection, transaction, audience, cancellationToken).ConfigureAwait(false);
            await InsertAuditEventAsync(connection, transaction, token.Value.Id, token.Value.Audience, "service_token_revoked", actorType, actorName, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> DeleteExpiredWithEventsAsync(DateTimeOffset now, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var expired = new List<(Guid Id, string Audience)>();
            await using (SqliteCommand select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT id,audience FROM service_tokens WHERE expires_at IS NOT NULL AND expires_at <= $now;";
                select.Parameters.AddWithValue("$now", Format(now));
                await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) expired.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
            }
            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM service_tokens WHERE expires_at IS NOT NULL AND expires_at <= $now;";
                delete.Parameters.AddWithValue("$now", Format(now));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach ((Guid id, string audience) in expired)
            {
                await InsertAuditEventAsync(connection, transaction, id, audience, "service_token_expired", actorType, actorName, cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return expired.Count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task ExecuteAsync(string sql, Guid id, string? valueName, object? value, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
        if (valueName is not null) command.Parameters.AddWithValue(valueName, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServiceToken?> FindAsync(string predicate, string value, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT id, token, audience, scopes_json, issued_at, expires_at, last_used_at FROM service_tokens WHERE {predicate};";
        command.Parameters.AddWithValue("$value", value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        string[] scopes = JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [];
        return ServiceToken.Restore(Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetString(2), scopes, Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : Parse(reader.GetString(5)), reader.IsDBNull(6) ? null : Parse(reader.GetString(6)));
    }

    private async Task ReplaceWithEventAsync(ServiceToken token, string eventType, string actorType, string actorName, bool deleteExisting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (deleteExisting) await DeleteAudienceAsync(connection, transaction, token.Audience, cancellationToken).ConfigureAwait(false);
            await InsertTokenAsync(connection, transaction, token, cancellationToken).ConfigureAwait(false);
            await InsertAuditEventAsync(connection, transaction, token.Id, token.Audience, eventType, actorType, actorName, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task InsertTokenAsync(SqliteConnection connection, SqliteTransaction transaction, ServiceToken token, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO service_tokens(id,token,audience,scopes_json,issued_at,expires_at,last_used_at) VALUES($id,$token,$audience,$scopes,$issued,$expires,NULL);";
        command.Parameters.AddWithValue("$id", token.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$token", token.Token);
        command.Parameters.AddWithValue("$audience", token.Audience);
        command.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(token.Scopes));
        command.Parameters.AddWithValue("$issued", Format(token.IssuedAt));
        command.Parameters.AddWithValue("$expires", token.ExpiresAt is null ? DBNull.Value : Format(token.ExpiresAt.Value));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteAudienceAsync(SqliteConnection connection, SqliteTransaction transaction, string audience, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM service_tokens WHERE audience=$audience;";
        command.Parameters.AddWithValue("$audience", audience);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(Guid Id, string Audience)?> FindIdentityAsync(SqliteConnection connection, SqliteTransaction transaction, string predicate, string value, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT id,audience FROM service_tokens WHERE {predicate} LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? (Guid.Parse(reader.GetString(0)), reader.GetString(1)) : null;
    }

    private async Task InsertAuditEventAsync(SqliteConnection connection, SqliteTransaction transaction, Guid tokenId, string audience, string eventType, string actorType, string actorName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,NULL,'service_token',$entity,$event,$actor_type,$actor_name,NULL,$after,NULL,$created);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$entity", tokenId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$event", eventType);
        command.Parameters.AddWithValue("$actor_type", actorType);
        command.Parameters.AddWithValue("$actor_name", actorName);
        command.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new { token_id = tokenId, audience }));
        command.Parameters.AddWithValue("$created", Format(_timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
