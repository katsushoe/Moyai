# Provider認証の実装・運用Contract

[仕様正本](Provider_Authentication_Specification.md)と[ADR 0008](../adr/0008-provider-assertions.md)に対応する開発版の操作仕様です。インストール済みサービスを変更する手順の実行には別途承認が必要です。

## 公開設定

既存の`moyai.json`へ次の`providerAuthentication`を追加します。既存設定との互換性のため設定FileはcamelCaseを維持し、JWT/DBとTrust Bundleはsnake_caseです。Provider名は登録とAudienceを完全一致させます。下記値は例です。

```json
{
  "providerAuthentication": {
    "mode": "assertion",
    "issuer": "moyai:instance-01",
    "lifetimeSeconds": 120,
    "clockSkewSeconds": 30,
    "protectorMode": "cng",
    "keyNamespace": "AkatsukiSoft.Moyai.instance-01",
    "activeKeyVersion": ""
  },
  "providers": [
    {
      "name": "githubbie",
      "endpoint": "http://127.0.0.1:43121/mcp",
      "toolPrefix": "github",
      "repository": true,
      "assertionToolScopes": {
        "get_version": ["repository.read"],
        "github_repository_status": ["repository.read"],
        "github_push": ["repository.push"]
      }
    }
  ]
}
```

`mode`の既定は`assertion`です。Issuer未設定、鍵未初期化、Tool Capability未登録の場合は`authentication_unavailable`または`provider_capability_missing`を返し、静的Tokenへ退避しません。既存Githubie登録の`githubbie`表記は内部ルーティング互換名として保持し、Assertion発行前に正規Provider ID／Audienceの`githubie`へ解決します。`aud=githubbie`は発行しません。Capability MappingはProviderの対応表を管理者が確認して登録する方式であり、Provider自身も独立した対応表を強制する必要があります。

Repository操作のScopeはMoyaiが固定Mappingで決定します。ClientのMCP/CLI引数にはScope、Assertion、Project UUIDを追加しません。Repository URLから輸送方式と`.git`接尾辞を除き、資格情報・query・fragmentを拒否します。Pathの大文字小文字は維持します。Project UUIDはMoyai DBのIDです。Provider側は自身のRepository登録とUUIDを対応付けて検証してください。

KelpieSSH Lifecycle操作はProtocol v2を使用し、正規Audienceを`kelpiessh`、Resourceを`kelpie_target:<不変Target ID>`とします。内部routing名`server`も同じAudienceへ解決します。Scopeは`target.status`、`deploy.prepare`、`deploy.upload`、`deploy.activate`、`deploy.verify`、`deploy.rollback`、`deploy.cleanup`、`deploy.status`を各Toolへ1対1で割り当てます。段階実行と結果不明時の照合規則は[ADR 0009](../adr/0009-kelpiessh-protocol-v2.md)を参照してください。

## Key Protector

- `cng`: Windowsユーザーの非Export RSA鍵でDEKとAAD HashをOAEP-SHA256により保護します。
- `keychain`: macOSユーザーKeychainの専用Service/AccountへKEKを保存します。
- `secret-service`: LinuxのSecret Serviceを使用します。`secretToolPath`に信頼する`secret-tool`の絶対パスを設定します。秘密値はProcess引数へ渡さず、入出力内容をログへ残しません。タイムアウトは30秒です。
- `broker`: PKCS#11、TPM、外部KMS等を提供する管理済みBrokerへHTTPS+mTLSで委譲します。`brokerEndpoint`と`brokerCertificateThumbprint`を設定します。クライアント証明書はCurrentUser/Myに秘密鍵付きで登録し、証明書検証を無効化しません。Broker実装・認可・ネイティブ基盤は別途必要です。

OS StoreのAccess Control、サービス実行ユーザー、鍵Namespaceを管理者が制限してください。既定のKeychain ACLに追加制約が必要な運用では配備前に設定してください。Passphrase fallbackは未提供で、未知の方式を選ぶと起動構成検証が失敗します。平文Fileや固定Keyへの代替はありません。

Broker APIは`POST /wrap`（`dek`,`context`→`wrapped_dek`,`key_version`）、`POST /unwrap`（`wrapped_dek`,`key_version`,`context`→`dek`）、`POST /rotate`（空Object→`key_version`）、`POST /health`（空Object→`available`）です。byte列はBase64です。ContextをAEADに結び付け、秘密値を保存・記録しないBrokerを使用してください。BrokerがActive版を永続管理します。

## 明示初期化・鍵更新

1. `assertion_protector_rotate`でKEKを作成します。OS方式では公開のActive版をDBの`protector_state`へ保存します。KEKは保存しません。`activeKeyVersion`は既存鍵を導入する場合の初期参照だけです。
2. `assertion_key_prepare`で`next`署名鍵と公開Trust情報を作成します。秘密鍵はレコード単位のEnvelopeへ保存します。
3. 全Providerへ公開JWK、Issuer、有効期間と受理可能な鍵状態を配布します。Providerに登録した鍵の署名検証が可能であることを管理者が確認します。署名側の`next`は発行不可ですが、切替前に検証側を受理可能な`active`状態へ準備します。未配布のProviderがある場合は次へ進みません。
4. `assertion_key_activate --kid <id> --trust-distribution-confirmed true --overlap-seconds 86400`を実行します。旧鍵は`retiring`となり、Overlap後に`retired`へ移ります。Overlapは360秒以上です。
5. `assertion_key_get --kid <id>`で公開の最新状態を取得できます。Trust BundleはJSON配列とし、戻り値を`AssertionJson.Options`のsnake_case表現へ変換します。`FileAssertionTrustStore`は毎要求にFileを再読込します。配布Fileは管理者保護と原子的置換が必要です。
6. KEKの更新は`assertion_protector_rotate`後に`assertion_secret_rewrap`を実行します。暗号文は維持し、検証済みDEKを再Wrapします。更新は版比較で競合を拒否します。
7. 漏えい時は`assertion_key_revoke --kid <id>`でMoyai側を即時失効させ、同時に各ProviderのTrust Bundleへ失効状態を配布します。配布前のRemote失効完了を主張しないでください。

上記はMCP Tool名です。CLIは`moyaictl`の後に`_`を`-`へ変えた名前を使用し、すべて稼働サービスを経由します。秘密を返す鍵Exportコマンドはありません。

## 移行と検証境界

旧Tokenが必要な期間は`mode=legacy`と`legacyStartedAt`,`legacyUntil`を明示します。両日時の差は最大7日で、期間外は拒否します。期限の自動延長はありません。Providerのdual-accept導入後、Moyaiをassertionへ切り替え、動作確認後にProviderの旧受付を停止し、既存`token_revoke`で旧Tokenを削除してください。Providerの移行実装は各担当CRで追跡します。

MCP bootstrapにAuthorizationを付与せず、実Tool呼び出しだけに新しいJWTを付与します。同一TransportでのTool再送は拒否します。`auth_assertion_expired`という明示的な業務応答だけを最大1回再試行し、その前にProjectのID・Revision・Provider・Repositoryを再確認します。結果不明、Replay、鍵失効、Policy拒否は自動再実行しません。

認証監査は`assertion_audit`へ操作ID、Provider、Project UUID、Repository ID、Scope、鍵ID、結果Code、UTCだけを保存します。JWTは非公開の一時値で、要求DTOのJSON、ToString、MCP引数へ含めません。ProviderがJWTを応答へそのまま反射した場合も除去します。

現在の実行ホストはWindows向けです。Application/Infrastructureはnet8.0でOS非依存Contractを共有しますが、macOS/LinuxのネイティブAdapterとホスト配備、実Broker、実Githubie/Buckettieの結合は別環境の検証が必要です。隔離HTTPテストは実Providerの適合性検証の代わりにはなりません。
