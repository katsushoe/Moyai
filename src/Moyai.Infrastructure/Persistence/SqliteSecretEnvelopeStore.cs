using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Persistence;

/// <summary>暗号化済みSecretの永続化です。外部Provider資格情報の集約は禁止します。</summary>
public sealed class SqliteSecretEnvelopeStore(SqliteDatabaseOptions options, string ownerType, string ownerId) : ISecretEnvelopeStore
{
    public async Task PutAsync(SecretEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        RequireOwner(envelope.Context);
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO secret_envelopes(secret_id,owner_type,owner_id,project_id,secret_kind,ciphertext,nonce,tag,wrapped_dek,key_version,aad_version)
                VALUES($id,$type,$owner,$project,$kind,$cipher,$nonce,$tag,$wrapped,$version,$aad);
                """;
            command.Parameters.AddWithValue("$id", envelope.Context.SecretId.ToString("D"));
            command.Parameters.AddWithValue("$type", ownerType);
            command.Parameters.AddWithValue("$owner", ownerId);
            command.Parameters.AddWithValue("$project", (object?)envelope.Context.ProjectId?.ToString("D") ?? DBNull.Value);
            command.Parameters.AddWithValue("$kind", envelope.Context.SecretKind);
            command.Parameters.AddWithValue("$cipher", envelope.Ciphertext);
            command.Parameters.AddWithValue("$nonce", envelope.Nonce);
            command.Parameters.AddWithValue("$tag", envelope.Tag);
            command.Parameters.AddWithValue("$wrapped", envelope.WrappedDek);
            command.Parameters.AddWithValue("$version", envelope.KeyVersion);
            command.Parameters.AddWithValue("$aad", envelope.AadVersion);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }

    public async Task<SecretEnvelope?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM secret_envelopes WHERE secret_id=$id AND owner_type=$type AND owner_id=$owner;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$type", ownerType);
            command.Parameters.AddWithValue("$owner", ownerId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            var context = new SecretContext(id, reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4));
            RequireOwner(context);
            return new SecretEnvelope(context, (byte[])reader[5], (byte[])reader[6], (byte[])reader[7], (byte[])reader[8], reader.GetString(9), reader.GetInt32(10));
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }

    public async Task<bool> RewrapAsync(Guid id, string expectedVersion, WrappedDataKey replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE secret_envelopes SET wrapped_dek=$wrapped,key_version=$version WHERE secret_id=$id AND key_version=$expected AND owner_type=$type AND owner_id=$owner;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$type", ownerType);
            command.Parameters.AddWithValue("$owner", ownerId);
            command.Parameters.AddWithValue("$wrapped", replacement.WrappedDek);
            command.Parameters.AddWithValue("$version", replacement.KeyVersion);
            command.Parameters.AddWithValue("$expected", expectedVersion);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("authentication_unavailable"); }
    }

    private void RequireOwner(SecretContext context)
    {
        if (context.OwnerType != ownerType || context.OwnerId != ownerId || (ownerType == "moyai" && context.SecretKind != "signing-key"))
            throw new ProviderAuthenticationException("auth_scope_denied");
    }
}
