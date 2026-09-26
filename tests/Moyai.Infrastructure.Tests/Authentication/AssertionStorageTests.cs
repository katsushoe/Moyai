using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class AssertionStorageTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("temporary-private-test-data");

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("wrapped")]
    [InlineData("version")]
    [InlineData("aad")]
    [InlineData("owner")]
    [InlineData("project")]
    [InlineData("kind")]
    public async Task DecryptTamperedEnvelopeRejects(string field)
    {
        await using var f = new AssertionFixture();
        var context = new SecretContext(Guid.NewGuid(), "moyai", f.Options.Issuer, Guid.NewGuid(), "signing-key");
        SecretEnvelope original = await f.Cryptor.EncryptAsync(context, Secret);
        SecretEnvelope changed = field switch
        {
            "ciphertext" => original with { Ciphertext = Flip(original.Ciphertext) },
            "nonce" => original with { Nonce = Flip(original.Nonce) },
            "tag" => original with { Tag = Flip(original.Tag) },
            "wrapped" => original with { WrappedDek = Flip(original.WrappedDek) },
            "version" => original with { KeyVersion = "unknown" },
            "aad" => original with { AadVersion = 2 },
            "owner" => original with { Context = context with { OwnerId = "other" } },
            "project" => original with { Context = context with { ProjectId = Guid.NewGuid() } },
            "kind" => original with { Context = context with { SecretKind = "api-token" } },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Cryptor.DecryptAsync(changed, changed.Context));
    }

    [Fact]
    public async Task RewrapRotatedKekPreservesCiphertextAndDecrypts()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        var context = new SecretContext(Guid.NewGuid(), "moyai", f.Options.Issuer, null, "signing-key");
        SecretEnvelope envelope = await f.Cryptor.EncryptAsync(context, Secret);
        await f.Store.PutAsync(envelope);
        await f.Protector.RotateAsync();
        WrappedDataKey next = await f.Cryptor.RewrapAsync(envelope);
        Assert.NotEqual(envelope.KeyVersion, next.KeyVersion);
        Assert.True(await f.Store.RewrapAsync(context.SecretId, envelope.KeyVersion, next));
        Assert.False(await f.Store.RewrapAsync(context.SecretId, envelope.KeyVersion, next));
        SecretEnvelope stored = (await f.Store.GetAsync(context.SecretId))!;
        Assert.Equal(envelope.Ciphertext, stored.Ciphertext);
        Assert.Equal(Secret, await f.Cryptor.DecryptAsync(stored, context));
    }

    [Fact]
    public async Task StoreDatabaseAloneDoesNotContainPlaintextAndCannotDecryptWithDifferentProtector()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        var context = new SecretContext(Guid.NewGuid(), "moyai", f.Options.Issuer, null, "signing-key");
        await f.Store.PutAsync(await f.Cryptor.EncryptAsync(context, Secret));
        using var different = new TestProtector();
        var cryptor = new SecretEnvelopeCryptor(different);
        SecretEnvelope stored = (await f.Store.GetAsync(context.SecretId))!;
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => cryptor.DecryptAsync(stored, context));
        string path = new SqliteConnectionStringBuilder(f.Database.ConnectionString).DataSource;
        Assert.DoesNotContain(Encoding.UTF8.GetString(Secret), Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreProviderCredentialInMoyaiRejects()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        var context = new SecretContext(Guid.NewGuid(), "moyai", f.Options.Issuer, null, "api-token");
        SecretEnvelope envelope = await f.Cryptor.EncryptAsync(context, Secret);
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Store.PutAsync(envelope));
    }

    [Fact]
    public async Task EncryptRepeatedPlaintextUsesIndependentKeysAndNonces()
    {
        await using var f = new AssertionFixture();
        var context = new SecretContext(Guid.NewGuid(), "moyai", f.Options.Issuer, null, "signing-key");
        SecretEnvelope first = await f.Cryptor.EncryptAsync(context, Secret);
        SecretEnvelope second = await f.Cryptor.EncryptAsync(context, Secret);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.NotEqual(first.WrappedDek, second.WrappedDek);
    }

    [Fact]
    public async Task SignProtectorUnavailableFailsClosed()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        f.Protector.Available = false;
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Issuer.IssueAsync(f.Context));
        Assert.Equal("auth_key_provider_unavailable", error.Code);
    }

    [Fact]
    public async Task RotateKeyOverlapThenRetirementAndRevocationEnforcesStates()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion old = await f.Issuer.IssueAsync(f.Context);
        AssertionTrustKey next = await f.Ring.PrepareAsync();
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Ring.SignAsync(next.Kid, Secret));
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Ring.ActivateAsync(next.Kid, 360, false));
        Assert.Equal(old.KeyId, (await f.Ring.GetActiveKeyAsync()).Kid);
        await f.Ring.ActivateAsync(next.Kid, 360, true);
        Assert.NotNull(await f.Validator().ValidateAsync(old.Value, f.Context));
        SignedAssertion active = await f.Issuer.IssueAsync(f.Context);
        Assert.Equal(next.Kid, active.KeyId);
        await f.Ring.RevokeAsync(next.Kid);
        ProviderAuthenticationException revoked = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(active.Value, f.Context));
        Assert.Equal("auth_key_revoked", revoked.Code);
        f.Clock.Now = f.Clock.Now.AddSeconds(360);
        Assert.Equal(SigningKeyState.Retired, (await f.Ring.FindAsync(f.Options.Issuer, old.KeyId))!.Status);
    }

    [Fact]
    public async Task CngRotationAndNewAdapterRestoresKeysWithoutDatabaseKek()
    {
        if (!OperatingSystem.IsWindows()) return;
        string keyNamespace = "Moyai.Test." + Guid.NewGuid().ToString("N");
        var versions = new List<string>();
        try
        {
            var protector = new CngKeyEncryptionProvider(keyNamespace, "missing");
            Assert.False(await protector.IsAvailableAsync());
            versions.Add(await protector.RotateAsync());
            var cryptor = new SecretEnvelopeCryptor(protector);
            var context = new SecretContext(Guid.NewGuid(), "moyai", "moyai:test", null, "signing-key");
            SecretEnvelope first = await cryptor.EncryptAsync(context, Secret);
            versions.Add(await protector.RotateAsync());
            WrappedDataKey rewrapped = await cryptor.RewrapAsync(first);
            var restarted = new SecretEnvelopeCryptor(new CngKeyEncryptionProvider(keyNamespace, versions[1]));
            Assert.Equal(Secret, await restarted.DecryptAsync(first with { WrappedDek = rewrapped.WrappedDek, KeyVersion = rewrapped.KeyVersion }, context));
        }
        finally
        {
            foreach (string version in versions)
            {
                using CngKey key = CngKey.Open(keyNamespace + "." + version);
                key.Delete();
            }
        }
    }

    private static byte[] Flip(byte[] input)
    {
        byte[] value = (byte[])input.Clone();
        value[0] ^= 1;
        return value;
    }
}
