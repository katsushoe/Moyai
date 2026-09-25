using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.ProviderAuthentication;

namespace Moyai.ProviderAuthentication.Tests;

public sealed class ProviderAuthenticationTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "moyai-provider-auth-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new();
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _issuer = "moyai:test";
    private readonly string _keyId = "test-key";

    [Fact]
    public async Task ProtocolV1AcceptsOnceAndPersistsReplayAcrossCacheRestart()
    {
        AssertionContext context = AssertionContext.ForRepository(
            "githubie",
            Guid.NewGuid(),
            "github.com/example/repository",
            "github_push",
            ["repository.push"],
            "operation-1");
        string assertion = CreateAssertion(context);
        Es256AssertionValidator validator = await CreateValidatorAsync(context);

        AssertionPrincipal principal = await validator.ValidateAsync(assertion, context);
        Assert.Equal(context, principal.Context);

        Es256AssertionValidator restarted = await CreateValidatorAsync(context);
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => restarted.ValidateAsync(assertion, context));
        Assert.Equal("auth_replay_detected", error.Code);
    }

    [Fact]
    public async Task ProtocolV2AcceptsManagedResourceContext()
    {
        AssertionContext context = AssertionContext.ForResource(
            "kelpiessh",
            Guid.NewGuid(),
            "kelpie_target",
            "target-01",
            "deploy_prepare",
            ["deploy.prepare"],
            "operation-2");

        AssertionPrincipal principal = await (await CreateValidatorAsync(context)).ValidateAsync(CreateAssertion(context), context);

        Assert.Equal("kelpie_target", principal.Context.ResourceKind);
        Assert.Null(principal.Context.Repository);
    }

    [Theory]
    [InlineData("repository", "unexpected")]
    [InlineData("unknown", "unexpected")]
    public async Task ProtocolV2RejectsMixedOrUnknownClaims(string name, string value)
    {
        AssertionContext context = AssertionContext.ForResource(
            "kelpiessh",
            Guid.NewGuid(),
            "kelpie_target",
            "target-01",
            "deploy_status",
            ["deploy.status"],
            "operation-3");
        string assertion = CreateAssertion(context, payload => payload[name] = value);
        Es256AssertionValidator validator = await CreateValidatorAsync(context);

        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.ValidateAsync(assertion, context));
        Assert.Equal("auth_assertion_invalid", error.Code);
    }

    [Fact]
    public async Task UnknownResourceKindIsRejectedByCapability()
    {
        AssertionContext context = AssertionContext.ForResource(
            "kelpiessh",
            Guid.NewGuid(),
            "unknown_target",
            "target-01",
            "deploy_status",
            ["deploy.status"],
            "operation-4");

        Es256AssertionValidator validator = await CreateValidatorAsync(context);
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.ValidateAsync(CreateAssertion(context), context));
        Assert.Equal("provider_capability_missing", error.Code);
    }

    [Fact]
    public async Task FileTrustStoreReloadsRevocation()
    {
        AssertionContext context = AssertionContext.ForRepository(
            "buckettie",
            Guid.NewGuid(),
            "workspace/repository",
            "bitbucket_push",
            ["repository.push"],
            "operation-5");
        string assertion = CreateAssertion(context);
        string trustPath = await WriteTrustAsync(SigningKeyState.Active);
        SqliteAssertionReplayCache replay = await CreateReplayAsync();
        var capability = CreateCapability(context);
        var validator = new Es256AssertionValidator(
            new FileAssertionTrustStore(trustPath),
            replay,
            new AssertionOptions(_issuer),
            capability,
            _clock);
        await WriteTrustAsync(SigningKeyState.Revoked);

        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.ValidateAsync(assertion, context));
        Assert.Equal("auth_key_revoked", error.Code);
    }

    [Fact]
    public async Task AssertionExpiringDuringReplayReservationIsRejectedBeforePrincipalReturns()
    {
        AssertionContext context = AssertionContext.ForRepository(
            "githubie",
            Guid.NewGuid(),
            "github.com/example/repository",
            "github_push",
            ["repository.push"],
            "operation-expiry-race");
        string assertion = CreateAssertion(context);
        string trustPath = await WriteTrustAsync(SigningKeyState.Active);
        var replay = new AdvancingReplayCache(_clock, TimeSpan.FromSeconds(121));
        var validator = new Es256AssertionValidator(
            new FileAssertionTrustStore(trustPath),
            replay,
            new AssertionOptions(_issuer, ClockSkewSeconds: 0),
            CreateCapability(context),
            _clock);

        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.ValidateAsync(assertion, context));

        Assert.Equal("auth_assertion_expired", error.Code);
        Assert.Equal(1, replay.CallCount);
    }

    [Fact]
    public async Task ExecutionValidationRejectsExpiredPrincipalWithoutReservingReplayAgain()
    {
        AssertionContext context = AssertionContext.ForRepository(
            "githubie",
            Guid.NewGuid(),
            "github.com/example/repository",
            "github_push",
            ["repository.push"],
            "operation-delayed-execution");
        string trustPath = await WriteTrustAsync(SigningKeyState.Active);
        var replay = new RecordingReplayCache();
        var validator = new Es256AssertionValidator(
            new FileAssertionTrustStore(trustPath),
            replay,
            new AssertionOptions(_issuer, ClockSkewSeconds: 0),
            CreateCapability(context),
            _clock);
        AssertionPrincipal principal = await validator.ValidateAsync(CreateAssertion(context), context);
        await validator.EnsureCurrentAsync(principal);
        _clock.Advance(TimeSpan.FromSeconds(121));

        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.EnsureCurrentAsync(principal));

        Assert.Equal("auth_assertion_expired", error.Code);
        Assert.Equal(1, replay.CallCount);
    }

    [Fact]
    public async Task ExecutionValidationReloadsTrustRevocationWithoutReservingReplayAgain()
    {
        AssertionContext context = AssertionContext.ForRepository(
            "githubie",
            Guid.NewGuid(),
            "github.com/example/repository",
            "github_push",
            ["repository.push"],
            "operation-delayed-revocation");
        string trustPath = await WriteTrustAsync(SigningKeyState.Active);
        var replay = new RecordingReplayCache();
        var validator = new Es256AssertionValidator(
            new FileAssertionTrustStore(trustPath),
            replay,
            new AssertionOptions(_issuer, ClockSkewSeconds: 0),
            CreateCapability(context),
            _clock);
        AssertionPrincipal principal = await validator.ValidateAsync(CreateAssertion(context), context);
        await WriteTrustAsync(SigningKeyState.Revoked);

        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => validator.EnsureCurrentAsync(principal));

        Assert.Equal("auth_key_revoked", error.Code);
        Assert.Equal(1, replay.CallCount);
    }

    private async Task<Es256AssertionValidator> CreateValidatorAsync(AssertionContext context)
    {
        string trustPath = await WriteTrustAsync(SigningKeyState.Active);
        SqliteAssertionReplayCache replay = await CreateReplayAsync();
        return new Es256AssertionValidator(
            new FileAssertionTrustStore(trustPath),
            replay,
            new AssertionOptions(_issuer),
            CreateCapability(context),
            _clock);
    }

    private static AssertionCapability CreateCapability(AssertionContext context) => new(
        context.Provider,
        context.Provider,
        context.ProtocolVersion,
        "ES256",
        true,
        new Dictionary<string, string[]> { [context.Tool] = context.Scopes.ToArray() },
        context.ProtocolVersion == "2" ? ["kelpie_target"] : null);

    private async Task<SqliteAssertionReplayCache> CreateReplayAsync()
    {
        Directory.CreateDirectory(_directory);
        var replay = new SqliteAssertionReplayCache(
            new SqliteAssertionReplayCacheOptions(Path.Combine(_directory, "replay.db")),
            _clock);
        await replay.InitializeAsync();
        return replay;
    }

    private async Task<string> WriteTrustAsync(SigningKeyState state)
    {
        Directory.CreateDirectory(_directory);
        ECParameters publicKey = _signer.ExportParameters(false);
        var key = new AssertionTrustKey(
            _issuer,
            _keyId,
            "ES256",
            AssertionJson.Encode(publicKey.Q.X!),
            AssertionJson.Encode(publicKey.Q.Y!),
            _clock.Now.AddMinutes(-1),
            _clock.Now.AddHours(1),
            state);
        string path = Path.Combine(_directory, "trust.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] { key }, AssertionJson.Options));
        return path;
    }

    private string CreateAssertion(AssertionContext context, Action<JsonObject>? change = null)
    {
        long issuedAt = _clock.Now.ToUnixTimeSeconds();
        var payload = new JsonObject
        {
            ["iss"] = _issuer,
            ["sub"] = _issuer,
            ["aud"] = context.Provider,
            ["iat"] = issuedAt,
            ["nbf"] = issuedAt,
            ["exp"] = issuedAt + 120,
            ["jti"] = AssertionJson.Encode(RandomNumberGenerator.GetBytes(16)),
            ["prv"] = context.Provider,
            ["project"] = context.Project.ToString("D"),
            ["scope"] = new JsonArray(context.Scopes.Select(scope => JsonValue.Create(scope)).ToArray()),
            ["protocol_version"] = context.ProtocolVersion,
            ["operation_id"] = context.OperationId,
        };
        if (context.ProtocolVersion == "1")
        {
            payload["repository"] = context.Repository;
        }
        else
        {
            payload["resource_kind"] = context.ResourceKind;
            payload["resource"] = context.Resource;
        }

        change?.Invoke(payload);
        var header = new { typ = "JWT", alg = "ES256", kid = _keyId };
        string encodedHeader = AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(header));
        string encodedPayload = AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        string input = encodedHeader + "." + encodedPayload;
        byte[] signature = _signer.SignData(
            Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return input + "." + AssertionJson.Encode(signature);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _signer.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan duration) => Now += duration;
    }

    private sealed class AdvancingReplayCache(TestClock clock, TimeSpan duration) : IAssertionReplayCache
    {
        public int CallCount { get; private set; }

        public Task<bool> TryUseAsync(
            string issuer,
            string jti,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            clock.Advance(duration);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingReplayCache : IAssertionReplayCache
    {
        public int CallCount { get; private set; }

        public Task<bool> TryUseAsync(
            string issuer,
            string jti,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(true);
        }
    }
}
