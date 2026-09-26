using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Authentication;

/// <summary>OS Protectorの公開Active版だけを永続化し、Rotationを再起動後も保持します。</summary>
public sealed class SqliteKeyEncryptionProvider(SqliteDatabaseOptions database, string keyNamespace, string initialVersion,
    Func<string, IKeyEncryptionKeyProvider> create) : IKeyEncryptionKeyProvider
{
    public async Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default) =>
        await create(await ActiveAsync(cancellationToken).ConfigureAwait(false)).WrapAsync(dek, context, cancellationToken).ConfigureAwait(false);
    public Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return create(key.KeyVersion).UnwrapAsync(key, context, cancellationToken);
    }
    public async Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        string next = await create(await ActiveAsync(cancellationToken).ConfigureAwait(false)).RotateAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO protector_state(key_namespace,active_version) VALUES($namespace,$version) ON CONFLICT(key_namespace) DO UPDATE SET active_version=excluded.active_version;";
        command.Parameters.AddWithValue("$namespace", keyNamespace);
        command.Parameters.AddWithValue("$version", next);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return next;
    }
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await create(await ActiveAsync(cancellationToken).ConfigureAwait(false)).IsAvailableAsync(cancellationToken).ConfigureAwait(false);
    private async Task<string> ActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(database, cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT active_version FROM protector_state WHERE key_namespace=$namespace;";
            command.Parameters.AddWithValue("$namespace", keyNamespace);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? initialVersion;
        }
        catch (SqliteException) { throw new ProviderAuthenticationException("auth_key_provider_unavailable"); }
    }
}
