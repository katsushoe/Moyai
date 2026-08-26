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

    public SqliteServiceTokenRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
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

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
