# Moyai.ProviderAuthentication

Provider側でMoyaiの操作単位ES256 Assertionを検証する.NET 8ライブラリです。Protocol v1のRepository ContextとProtocol v2のLifecycle Resource Context、Trust Bundle再読込、永続SQLite Replay防止を提供します。

```csharp
var replay = new SqliteAssertionReplayCache(
    new SqliteAssertionReplayCacheOptions(config.ReplayDatabasePath));
await replay.InitializeAsync(cancellationToken);

var context = AssertionContext.ForRepository(
    "githubie",
    registeredProjectId,
    registeredRepositoryId,
    "github_push",
    ["repository.push"],
    operationId);
var capability = new AssertionCapability(
    "githubie",
    "githubie",
    "1",
    "ES256",
    true,
    new Dictionary<string, string[]> { ["github_push"] = ["repository.push"] });
var validator = new Es256AssertionValidator(
    new FileAssertionTrustStore(config.TrustBundlePath),
    replay,
    new AssertionOptions(config.Issuer),
    capability);

AssertionPrincipal principal = await validator.ValidateAsync(assertion, context, cancellationToken);

// 承認待機などの後、Provider処理を開始する直前に再確認します。
// Replayは再予約せず、期限・鍵状態・Capabilityを最新状態で検証します。
await validator.EnsureCurrentAsync(principal, cancellationToken);
```

Trust BundleとReplay DBにはProviderサービスアカウントだけが書き込める絶対パスを明示してください。Assertion、秘密鍵、外部サービスCredentialをログへ出力しないでください。
