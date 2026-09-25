using System.Security.Cryptography;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class NativeProtectorTests
{
    [Fact]
    public async Task ActiveKekVersionSurvivesRestartWithoutKeyMaterialInDatabase()
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        using var store = new MemoryStore();
        var first = new SqliteKeyEncryptionProvider(fixture.Database, "test", "", version => new NativeSecretKeyProtector(store, version));
        string version = await first.RotateAsync();
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedDataKey wrapped = await first.WrapAsync(dek, new byte[] { 1 });
        var restarted = new SqliteKeyEncryptionProvider(fixture.Database, "test", "", selected => new NativeSecretKeyProtector(store, selected));
        Assert.True(await restarted.IsAvailableAsync());
        Assert.Equal(dek, await restarted.UnwrapAsync(wrapped, new byte[] { 1 }));
        Assert.Equal(version, (await restarted.WrapAsync(dek, new byte[] { 1 })).KeyVersion);
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => restarted.UnwrapAsync(wrapped, new byte[] { 2 }));
    }

    [Fact]
    public async Task UnavailableTrustAndReplayStoreFailClosed()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator(trust: new FileAssertionTrustStore(path)).ValidateAsync(assertion.Value, f.Context));
        Assert.Equal("authentication_unavailable", error.Code);
        var options = new Moyai.Infrastructure.Persistence.SqliteDatabaseOptions("Data Source=:memory:");
        var cache = new Moyai.Infrastructure.Persistence.SqliteAssertionReplayCache(options, f.Clock);
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => cache.TryUseAsync("moyai:test", "jti", f.Clock.Now.AddMinutes(2)));
    }

    [Fact]
    public async Task FileTrustReloadsRevocationWithoutRestart()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        AssertionTrustKey key = await f.Ring.GetActiveKeyAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new[] { key }, AssertionJson.Options));
            var store = new FileAssertionTrustStore(path);
            Assert.Equal(SigningKeyState.Active, (await store.FindAsync(key.Issuer, key.Kid))!.Status);
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new[] { key with { Status = SigningKeyState.Revoked } }, AssertionJson.Options));
            ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator(trust: store).ValidateAsync(assertion.Value, f.Context));
            Assert.Equal("auth_key_revoked", error.Code);
        }
        finally { File.Delete(path); }
    }

    private sealed class MemoryStore : IProtectedKeyStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
        public Task StoreAsync(string version, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
        {
            _keys.Add(version, key.ToArray());
            return Task.CompletedTask;
        }
        public Task<byte[]> LoadAsync(string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys[version].ToArray());
        public void Dispose()
        {
            foreach (byte[] key in _keys.Values) CryptographicOperations.ZeroMemory(key);
        }
    }
}
