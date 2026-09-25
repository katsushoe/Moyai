using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Authentication;

/// <summary>秘密鍵はEnvelopeへ保存し、鍵状態はSQLiteで原子的に更新します。</summary>
public sealed class SqliteSigningKeyRing(SqliteDatabaseOptions database, ISecretEnvelopeStore secrets,
    SecretEnvelopeCryptor cryptor, string issuer, TimeProvider clock) : IAssertionSigner, IAssertionTrustStore
{
    public Task<AssertionTrustKey?> GetTrustAsync(string kid, CancellationToken cancellationToken = default) => FindAsync(issuer, kid, cancellationToken);
    public async Task<AssertionTrustKey> PrepareAsync(CancellationToken cancellationToken = default)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters publicKey = key.ExportParameters(false);
        var context = new SecretContext(Guid.NewGuid(), "moyai", issuer, null, "signing-key");
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            SecretEnvelope envelope = await cryptor.EncryptAsync(context, privateKey, cancellationToken).ConfigureAwait(false);
            await secrets.PutAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
        string kid = Guid.NewGuid().ToString("N");
        DateTimeOffset now = clock.GetUtcNow();
        var entry = new AssertionTrustKey(issuer, kid, "ES256", AssertionJson.Encode(publicKey.Q.X!), AssertionJson.Encode(publicKey.Q.Y!), now, now.AddYears(1), SigningKeyState.Next);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO assertion_keys(issuer,kid,secret_id,public_x,public_y,not_before,not_after,state) VALUES($issuer,$kid,$secret,$x,$y,$before,$after,'Next');";
        command.Parameters.AddWithValue("$issuer", issuer);
        command.Parameters.AddWithValue("$kid", kid);
        command.Parameters.AddWithValue("$secret", context.SecretId.ToString("D"));
        command.Parameters.AddWithValue("$x", entry.X);
        command.Parameters.AddWithValue("$y", entry.Y);
        command.Parameters.AddWithValue("$before", entry.NotBeforeUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$after", entry.NotAfterUtc.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task ActivateAsync(string kid, int overlapSeconds, bool trustDistributionConfirmed, CancellationToken cancellationToken = default)
    {
        if (!trustDistributionConfirmed || overlapSeconds < 360) throw new ProviderAuthenticationException("authentication_unavailable");
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM assertion_keys WHERE issuer=$issuer AND kid=$kid AND state='Next' AND not_before<=$now AND not_after>$after;";
        command.Parameters.AddWithValue("$issuer", issuer);
        command.Parameters.AddWithValue("$kid", kid);
        command.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$after", clock.GetUtcNow().AddSeconds(overlapSeconds).ToUnixTimeSeconds());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new ProviderAuthenticationException("auth_key_unknown");
        command.CommandText = "UPDATE assertion_keys SET state='Retiring',retire_after=$after WHERE issuer=$issuer AND state='Active'; UPDATE assertion_keys SET state='Active' WHERE issuer=$issuer AND kid=$kid AND state='Next';";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string kid, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE assertion_keys SET state='Revoked' WHERE issuer=$issuer AND kid=$kid;";
        command.Parameters.AddWithValue("$issuer", issuer);
        command.Parameters.AddWithValue("$kid", kid);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new ProviderAuthenticationException("auth_key_unknown");
    }

    public async Task<int> RewrapAsync(CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        await using (SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT secret_id FROM assertion_keys WHERE issuer=$issuer;";
            command.Parameters.AddWithValue("$issuer", issuer);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(Guid.Parse(reader.GetString(0)));
        }
        int count = 0;
        foreach (Guid id in ids)
        {
            SecretEnvelope envelope = await secrets.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new ProviderAuthenticationException("auth_key_unknown");
            WrappedDataKey replacement = await cryptor.RewrapAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (await secrets.RewrapAsync(id, envelope.KeyVersion, replacement, cancellationToken).ConfigureAwait(false)) count++;
        }
        return count;
    }

    public async Task<AssertionTrustKey> GetActiveKeyAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT kid FROM assertion_keys WHERE issuer=$issuer AND state='Active';";
        command.Parameters.AddWithValue("$issuer", issuer);
        string? kid = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return kid is null ? throw new ProviderAuthenticationException("authentication_unavailable")
            : await FindAsync(issuer, kid, cancellationToken).ConfigureAwait(false) ?? throw new ProviderAuthenticationException("auth_key_unknown");
    }

    public async Task<AssertionTrustKey?> FindAsync(string expectedIssuer, string keyId, CancellationToken cancellationToken = default)
    {
        if (expectedIssuer != issuer) return null;
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE assertion_keys SET state='Retired' WHERE issuer=$issuer AND state='Retiring' AND retire_after<=$now;";
        command.Parameters.AddWithValue("$issuer", issuer);
        command.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = "SELECT public_x,public_y,not_before,not_after,state FROM assertion_keys WHERE issuer=$issuer AND kid=$kid;";
        command.Parameters.AddWithValue("$kid", keyId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new AssertionTrustKey(issuer, keyId, "ES256", reader.GetString(0), reader.GetString(1), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)), Enum.Parse<SigningKeyState>(reader.GetString(4)));
    }

    public async Task<byte[]> SignAsync(string keyId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT secret_id FROM assertion_keys WHERE issuer=$issuer AND kid=$kid AND state='Active' AND not_before<=$now AND not_after>$now;";
        command.Parameters.AddWithValue("$issuer", issuer);
        command.Parameters.AddWithValue("$kid", keyId);
        command.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeSeconds());
        string? secretId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (secretId is null) throw new ProviderAuthenticationException("auth_key_revoked");
        var context = new SecretContext(Guid.Parse(secretId), "moyai", issuer, null, "signing-key");
        SecretEnvelope envelope = await secrets.GetAsync(context.SecretId, cancellationToken).ConfigureAwait(false) ?? throw new ProviderAuthenticationException("auth_key_unknown");
        byte[] privateKey = await cryptor.DecryptAsync(envelope, context, cancellationToken).ConfigureAwait(false);
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(privateKey, out _);
            byte[] signature = key.SignData(payload.Span, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return signature;
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        try { return await SqliteConnectionFactory.OpenAsync(database, cancellationToken).ConfigureAwait(false); }
        catch (SqliteException) { throw new ProviderAuthenticationException("auth_key_provider_unavailable"); }
    }
}
