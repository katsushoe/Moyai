using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests.Authentication;

internal sealed class AssertionFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "moyai-assertion-" + Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    public TestProtector Protector { get; } = new();
    public SqliteDatabaseOptions Database { get; }
    public SqliteSigningKeyRing Ring { get; }
    public SecretEnvelopeCryptor Cryptor { get; }
    public SqliteSecretEnvelopeStore Store { get; }
    public AssertionOptions Options { get; } = new("moyai:test");
    public AssertionContext Context { get; } = new("githubie", Guid.NewGuid(), "github.com/example/repo", "github_push", ["repository.push"], "test-operation");
    public Es256AssertionIssuer Issuer { get; }
    public AssertionFixture()
    {
        Directory.CreateDirectory(_directory);
        Database = new SqliteDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "test.db"), Pooling = false }.ToString());
        Store = new SqliteSecretEnvelopeStore(Database, "moyai", Options.Issuer);
        Cryptor = new SecretEnvelopeCryptor(Protector);
        Ring = new SqliteSigningKeyRing(Database, Store, Cryptor, Options.Issuer, Clock);
        Issuer = new Es256AssertionIssuer(Ring, Options, Clock);
    }
    public async Task InitializeAsync()
    {
        await new SqliteDatabaseInitializer(Database).InitializeAsync();
        AssertionTrustKey key = await Ring.PrepareAsync();
        await Ring.ActivateAsync(key.Kid, 360, true);
    }
    public Es256AssertionValidator Validator(AssertionContext? context = null, IAssertionTrustStore? trust = null)
    {
        AssertionContext expected = context ?? Context;
        var capability = new AssertionCapability(expected.Provider, expected.Provider, "1", "ES256", true,
            new Dictionary<string, string[]> { [expected.Tool] = expected.Scopes.ToArray() });
        return new Es256AssertionValidator(trust ?? Ring, new SqliteAssertionReplayCache(Database, Clock), Options, capability, Clock);
    }
    public ValueTask DisposeAsync()
    {
        Protector.Dispose();
        Directory.Delete(_directory, true);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class TestProtector : IKeyEncryptionKeyProvider, IDisposable
{
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal) { ["v1"] = RandomNumberGenerator.GetBytes(32) };
    private string _active = "v1";
    public bool Available { get; set; } = true;
    public Task<WrappedDataKey> WrapAsync(ReadOnlyMemory<byte> dek, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        byte[] value = new byte[60];
        RandomNumberGenerator.Fill(value.AsSpan(0, 12));
        using var aes = new AesGcm(_keys[_active], 16);
        aes.Encrypt(value.AsSpan(0, 12), dek.Span, value.AsSpan(12, 32), value.AsSpan(44, 16), context.Span);
        return Task.FromResult(new WrappedDataKey(value, _active));
    }
    public Task<byte[]> UnwrapAsync(WrappedDataKey key, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        byte[] dek = new byte[32];
        try
        {
            using var aes = new AesGcm(_keys[key.KeyVersion], 16);
            aes.Decrypt(key.WrappedDek.AsSpan(0, 12), key.WrappedDek.AsSpan(12, 32), key.WrappedDek.AsSpan(44, 16), dek, context.Span);
            return Task.FromResult(dek);
        }
        catch (Exception exception) when (exception is CryptographicException or KeyNotFoundException)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new ProviderAuthenticationException("auth_secret_decryption_failed");
        }
    }
    public Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        _active = Guid.NewGuid().ToString("N");
        _keys.Add(_active, RandomNumberGenerator.GetBytes(32));
        return Task.FromResult(_active);
    }
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);
    private void RequireAvailable()
    {
        if (!Available) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
    }
    public void Dispose()
    {
        foreach (byte[] key in _keys.Values) CryptographicOperations.ZeroMemory(key);
    }
}
