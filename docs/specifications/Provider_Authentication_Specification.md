# SPEC.md Version

1.0

2026.09.06

# 変更履歴

- 2026.09.06: 初版。Provider 汎用の署名付き Assertion、暗号化 DB、Key Protector、鍵更新、移行およびテスト要件を定義。

# 目的

本書は、Moyai が Githubie、Buckettie および将来追加される Repository Provider を呼び出す際の、Provider 汎用認証とローカル秘密情報保護の仕様正本である。

既存の `Moyai_v1_Specification.md` に定義された Repository Provider Contract を拡張し、次を実現する。

- AI Agent や MCP Client を秘密情報の中継経路にしない。
- Moyai と Provider の間で長期 Token を授受または更新しない。
- 操作ごとに短時間だけ有効な署名付き Assertion を生成する。
- Provider、Project、操作権限を Assertion 単位で制限する。
- 秘密情報をローカル DB に保存する場合は Envelope Encryption を使用する。
- Windows、Linux、macOS で同じ上位 Contract を使用する。
- KelpieSSH の `.dat` Store で実証された Envelope 形式の考え方を再利用し、Windows DPAPI への直接依存は持ち込まない。

本書と既存仕様が矛盾する場合、Provider 間認証およびローカル秘密情報保護については本書を優先する。Repository Provider が保有する GitHub、Bitbucket 等の外部サービス認証情報は、引き続き各 Provider の責務とする。

## 対象範囲

- Moyai から Repository Provider への呼び出し認証
- Assertion の発行、検証、失効、再送防止
- Provider Capability と操作 Scope の対応
- Moyai の署名秘密鍵および Provider 側秘密情報のローカル保護
- 暗号化 DB の論理構造
- OS 固有 Key Protector の抽象化
- 鍵更新、障害時動作、監査、移行、テスト

## 対象外

- GitHub、Bitbucket 等が提供する OAuth Token、Access Token、SSH Key の発行手順
- Provider と外部サービス間の OAuth Refresh 手順
- AI Agent による秘密情報の入力、表示、更新、受け渡し
- 全 Provider で共有する万能 Token
- OS 間で同一の親鍵を自動同期する機能
- Remote KMS、HSM、PKCS#11 製品固有の導入手順

## 用語

| 用語 | 定義 |
| :--- | :--- |
| Provider | Moyai から Repository 操作を委譲される Githubie、Buckettie 等のサービス |
| Assertion | Moyai が操作ごとに発行し、Provider が検証する短寿命の署名付き JWT |
| Issuer | Assertion を発行する Moyai Instance |
| Audience | Assertion の受領を許可された単一 Provider |
| Scope | 許可された Repository 操作を表す安定識別子 |
| Signing Key | Assertion に署名する Moyai の非対称秘密鍵 |
| Verification Key | Provider が Assertion の署名検証に使用する公開鍵 |
| DEK | 個別の秘密レコードを暗号化する Data Encryption Key |
| KEK | DEK を暗号化する Key Encryption Key。親鍵とも呼ぶ |
| Key Protector | OS Key Store、HSM 等を使って KEK または Signing Key を保護する実装 |
| Envelope | 暗号文、Nonce、Tag、Wrapped DEK、鍵版、メタデータを含む永続化単位 |

# アーキテクチャ

## 構成

```text
MCP Client / AI Agent
        |
        | 操作要求。秘密情報を含めない
        v
+-------------------+
|       Moyai       |
| Project Resolver  |
| Scope Resolver    |
| Assertion Issuer  |
+---------+---------+
          | Authorization: Bearer <short-lived assertion>
          | Repository Provider Contract
          v
+-------------------+       +----------------------------+
| Repository        |------>| GitHub / Bitbucket / ...   |
| Provider Adapter  |       | Provider-owned credential  |
+---------+---------+       +----------------------------+
          |
          v
+-------------------+
| Secret Store      |
| Encrypted DB      |
| Key Protector     |
+-------------------+
```

## 責務分担

| Component | 責務 | 保持してはならない情報 |
| :--- | :--- | :--- |
| MCP Client / AI Agent | Project と操作の指定、結果の受領 | Assertion、Signing Key、Provider Credential の永続コピー |
| Moyai | Project と Provider の解決、Scope 決定、Assertion 発行、結果記録 | Provider の外部サービス用 Token、Password、SSH Private Key |
| Provider 共通認証層 | Assertion の検証、再送防止、Scope 強制、監査 | 他 Provider 用 Credential、Moyai Signing Key |
| Provider Adapter | 共通 Scope と Provider 固有操作の対応付け | 他 Provider の設定と秘密情報 |
| Provider Secret Store | Provider 自身の外部サービス認証情報を暗号化して保持 | Moyai Signing Key |
| Key Protector | KEK または Signing Key Material の保護と復号 | 業務データ、Repository 操作内容 |

## 依存方向

- Moyai Core は `IAssertionSigner` と `IKeyEncryptionKeyProvider` の Interface のみに依存する。
- Provider 共通認証層は `IAssertionValidator`、Replay Cache、Trust Bundle に依存する。
- Windows DPAPI、macOS Keychain、Linux Secret Service、PKCS#11 は下位 Adapter とする。
- Core、DB Schema、JWT Claim、Provider Contract は OS 固有 API に依存してはならない。
- Provider Adapter は共通認証層の検証結果だけを受け取り、JWT の独自解釈をしてはならない。

## 信頼境界

1. MCP Client から Moyai への要求は、Provider 用資格情報とは別の境界とする。
2. Moyai と Provider の HTTP 接続が localhost であっても、無認証を許可しない。
3. Provider は署名検証済み Assertion だけを認可判断へ使用する。
4. Provider から外部サービスへの認証は Provider 自身の資格情報で行う。
5. DB ファイル単体の漏えいでは秘密平文を復元できない設計とする。

## 暗号アルゴリズム

| 用途 | 必須方式 | 備考 |
| :--- | :--- | :--- |
| Assertion 署名 | `ES256` | ECDSA P-256 + SHA-256。3 OS 共通の必須 Baseline |
| レコード暗号化 | `AES-256-GCM` | レコードごとに一意な 256-bit DEK と 96-bit Nonce を使用 |
| DEK 保護 | Key Protector が定める AEAD または OS 保護 API | `wrapped_dek` と `key_version` を返す |
| 鍵 ID | `kid` | 鍵版を一意に識別し、秘密情報を含めない |

`alg=none`、共通秘密による全 Provider 共用 HMAC、ECB、固定 Nonce は禁止する。EdDSA は全対象 Runtime での相互運用性を検証した後に追加可能とする。

# データ構造

## Assertion Header

| Field | Required | Value |
| :--- | :---: | :--- |
| `typ` | Yes | `JWT` |
| `alg` | Yes | `ES256` |
| `kid` | Yes | 使用した Signing Key の安定 ID |

## Assertion Claims

| Claim | Type | Required | 制約 |
| :--- | :--- | :---: | :--- |
| `iss` | String | Yes | Moyai Instance の安定 ID |
| `sub` | String | Yes | `moyai:<instance-id>` |
| `aud` | String | Yes | 単一 Provider ID。配列は禁止 |
| `iat` | Integer | Yes | UTC Unix time |
| `nbf` | Integer | Yes | 通常は `iat - clock_skew_seconds` |
| `exp` | Integer | Yes | `iat + assertion_lifetime_seconds` |
| `jti` | String | Yes | 128 bit 以上の暗号学的乱数による一意 ID |
| `prv` | String | Yes | Provider ID。`aud` と一致すること |
| `project` | String | Yes | Moyai の内部 Project UUID |
| `repository` | String | Yes | 正規化した Repository 識別子。資格情報を含まないこと |
| `scope` | String Array | Yes | 1 件以上。未知 Scope を含めないこと |
| `protocol_version` | String | Yes | 本仕様の Protocol Version。初期値 `1` |
| `operation_id` | String | Yes | Moyai の監査 Event と対応する一意 ID |

Project 表示名、ローカル Path、Remote URL 内のユーザー情報、外部サービス Token は Claim に含めない。

### Payload 例

```json
{
  "iss": "moyai:instance-01",
  "sub": "moyai:instance-01",
  "aud": "buckettie",
  "iat": 1788634800,
  "nbf": 1788634770,
  "exp": 1788634920,
  "jti": "2c099f36-5375-4d38-9fe3-e42b972cf6a8",
  "prv": "buckettie",
  "project": "4e5a771f-b241-4c47-8554-b9651a9db81a",
  "repository": "bitbucket.org/example/repository",
  "scope": ["repository.push"],
  "protocol_version": "1",
  "operation_id": "repo-op-018f4bdb"
}
```

例の識別子は説明用であり、実環境の値ではない。

## Scope

Scope は Provider 非依存の安定識別子とし、最小権限で発行する。

| Scope | 許可する共通操作 |
| :--- | :--- |
| `repository.read` | status、diff、branch、remote 情報の参照 |
| `repository.commit` | ローカル Commit の作成 |
| `repository.push` | Remote への Push |
| `repository.pull` | Remote からの Pull |
| `repository.branch.write` | Branch の作成、更新、削除 |
| `repository.tag.write` | Tag の作成、更新、削除 |
| `release.publish` | Release の作成または公開 |
| `artifact.upload` | Release Artifact の Upload |

複数操作を一度に行う場合でも、実行予定の操作だけを Scope に含める。Provider 固有操作は `provider.<provider-id>.<operation>` 形式で追加できるが、共通 Contract の Scope を置き換えてはならない。

## Provider Capability Manifest

Provider は認証要件を Capability とともに返す。

```json
{
  "provider_id": "buckettie",
  "protocol_versions": ["1"],
  "assertion_algorithms": ["ES256"],
  "required_audience": "buckettie",
  "scopes": {
    "repository_status": ["repository.read"],
    "repository_commit": ["repository.commit"],
    "repository_push": ["repository.push"]
  },
  "replay_protection": true
}
```

Capability 取得自体は起動時 Trust Bootstrap に限定した無認証 Endpoint、または既知の Trust Bundle による認証済み Endpoint とする。無認証 Endpoint は秘密情報、Project 情報、Runtime Path を返してはならない。

## Trust Bundle

Provider は次の公開情報を保持する。

| Field | Description |
| :--- | :--- |
| `issuer` | 許可する Moyai Instance ID |
| `kid` | Verification Key ID |
| `algorithm` | `ES256` |
| `public_key_jwk` | 公開鍵 JWK |
| `not_before_utc` | 検証開始日時 |
| `not_after_utc` | 検証終了日時 |
| `status` | `next`、`active`、`retiring`、`revoked` |

Trust Bundle は秘密ではないが、改ざん検知または管理者権限で保護された設定として保存する。Network から自動取得する場合は、TLS に加えて Pinning または署名済み Bundle を必須とする。

## 暗号化秘密レコード

秘密情報をローカル DB に保存する場合、1 Secret を1レコードとして次の Envelope を保持する。

| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `secret_id` | UUID | Yes | レコード ID |
| `owner_type` | TEXT | Yes | `moyai` または `provider` |
| `owner_id` | TEXT | Yes | Instance ID または Provider ID |
| `project_id` | UUID/TEXT | No | Project 固有 Secret の場合だけ設定 |
| `secret_kind` | TEXT | Yes | `signing-key`、`oauth-refresh-token`、`api-token` 等 |
| `ciphertext` | BLOB | Yes | AES-256-GCM 暗号文 |
| `nonce` | BLOB | Yes | 96 bit。DEK ごとに一意 |
| `tag` | BLOB | Yes | 128 bit Authentication Tag |
| `wrapped_dek` | BLOB | Yes | KEK で保護された DEK |
| `key_version` | TEXT | Yes | KEK の版または ID |
| `aad_version` | INTEGER | Yes | AAD Schema Version。初期値 `1` |
| `created_at_utc` | DATETIME | Yes | 作成日時 |
| `updated_at_utc` | DATETIME | Yes | 更新日時 |
| `rotation_due_at_utc` | DATETIME | No | 更新予定日時 |

## Additional Authenticated Data

AAD は次を順序固定の Canonical JSON として構成する。

```json
{
  "schema_version": 1,
  "secret_id": "<uuid>",
  "owner_type": "provider",
  "owner_id": "buckettie",
  "project_id": "<uuid-or-null>",
  "secret_kind": "oauth-refresh-token"
}
```

レコードを別 Provider、別 Project、別 Secret Kind へコピーして復号する攻撃を防ぐため、AAD 不一致は必ず復号失敗とする。

## Key Protector Interface

```text
IKeyEncryptionKeyProvider
  get_active_key_metadata() -> KeyMetadata
  wrap_key(dek, context) -> WrappedKey
  unwrap_key(wrapped_key, context) -> dek
  rotate_key() -> KeyMetadata
  get_health() -> KeyProviderHealth

IAssertionSigner
  get_active_signing_key() -> SigningKeyMetadata
  sign(payload, key_id) -> SignedAssertion
  rotate_signing_key() -> SigningKeyMetadata
  get_public_jwk(key_id) -> Jwk
```

秘密鍵の生 Byte を上位 Layer へ返さない実装を推奨する。OS API の制約で返却が必要な場合は、最短 Scope で Memory に保持し、使用後に消去を試みる。

## Key Protector Adapter

| Platform / Mode | Adapter | 要件 |
| :--- | :--- | :--- |
| Windows | DPAPI CurrentUser または CNG | Service Account を固定し、別ユーザーからの復号を拒否 |
| macOS | Keychain | 専用 Service 名と Account 名を使用し、Access Control を制限 |
| Linux Desktop | Secret Service | 専用 Collection Item を使用し、Headless 可否を起動時検査 |
| Linux Server | PKCS#11、TPM、外部 KMS | Device または Service の利用不能時は Fail Closed |
| Portable Fallback | Passphrase 由来 KEK | 明示設定時のみ。Argon2id の Parameter を設定し、無人起動用途では既定無効 |

Windows DPAPI は Adapter の一つであり、Core Contract、DB Schema、Envelope Format に露出させない。

## KelpieSSH Envelope との関係

KelpieSSH の `SshProfileTrustStore` から次の設計を採用する。

- AES-GCM による Payload 暗号化
- ランダムな 32-byte Data Key
- Envelope の Version 管理
- AAD による用途固定
- 改ざん時の Fail Closed
- Atomic Write と同時実行制御

次はそのまま採用しない。

- `ProtectedData.Protect` / `Unprotect` の直接呼び出し
- `DataProtectionScope.CurrentUser` を上位仕様へ固定すること
- Windows 以外で `PlatformNotSupportedException` とする構造
- Wrapped Key と暗号文を同じ Store に置けば親鍵分離が成立するとみなすこと

暗号化 DB と KEK の保護領域は論理的かつ可能な限り物理的に分離する。DB Backup に OS Key Store の秘密 Material を含めてはならない。

## 設定

```json
{
  "provider_authentication": {
    "enabled": true,
    "protocol_version": "1",
    "issuer": "moyai:instance-01",
    "signing_algorithm": "ES256",
    "assertion_lifetime_seconds": 120,
    "clock_skew_seconds": 30,
    "replay_protection": true,
    "key_rotation_overlap_seconds": 86400,
    "key_protector": {
      "mode": "os-default",
      "key_namespace": "AkatsukiSoft.Moyai.ProviderAuth"
    }
  }
}
```

| Setting | Required | Default | Constraint |
| :--- | :---: | :--- | :--- |
| `enabled` | Yes | `true` | Production では `false` 禁止 |
| `protocol_version` | Yes | `1` | Provider の対応版に含まれること |
| `issuer` | Yes | なし | Install ごとに一意。秘密値を含めない |
| `signing_algorithm` | Yes | `ES256` | Provider の対応方式と一致 |
| `assertion_lifetime_seconds` | Yes | `120` | 30 以上 300 以下 |
| `clock_skew_seconds` | Yes | `30` | 0 以上 60 以下 |
| `replay_protection` | Yes | `true` | 変更操作では `false` 禁止 |
| `key_rotation_overlap_seconds` | Yes | `86400` | Assertion 寿命より長いこと |
| `key_protector.mode` | Yes | `os-default` | 対象 OS で利用可能な Adapter を解決できること |
| `key_namespace` | Yes | 製品既定値 | 製品と Instance の衝突を避けること |

環境変数や Command Line 引数へ長期秘密値を直接設定してはならない。設定 File には Key ID、Adapter 種別、公開情報だけを置く。

# 処理仕様

## 起動時初期化

1. Moyai は Provider Authentication 設定を検証する。
2. Key Protector の利用可否と現在の Signing Key を確認する。
3. Signing Key が未作成の場合は、明示された初期化処理で生成する。通常起動中の暗黙生成は禁止する。
4. Provider ごとに Endpoint、Provider ID、Protocol Version、Algorithm、Capability を確認する。
5. Trust Bundle に Active Key が登録されていることを確認する。
6. 一つでも必須条件を満たさない Provider は `authentication_unavailable` とし、認証が必要な操作を拒否する。

## Repository 操作要求

1. MCP Client は Moyai に Project 名と操作 Parameter を送る。秘密情報は送らない。
2. Moyai は Project UUID、正規化 Repository ID、Provider ID を DB から解決する。
3. Moyai は Provider Capability を確認し、共通操作を最小 Scope へ変換する。
4. Moyai は `operation_id` と `jti` を新規生成する。
5. Moyai は現在時刻、Audience、Project、Repository、Scope を含む Assertion を `ES256` で署名する。
6. Moyai は Provider 呼び出しの HTTP `Authorization: Bearer <assertion>` に設定する。Request Body、MCP Argument、Log へは含めない。
7. Provider 共通認証層は署名、Issuer、Audience、時刻、Protocol、Project、Repository、Scope、`jti` を検証する。
8. Provider は `jti` を Replay Cache に原子的に登録する。既登録なら拒否する。
9. Provider Adapter は検証済み Principal と Context を受け取り、対象操作だけを実行する。
10. Provider は機密情報を除いた標準結果と `operation_id` を返す。
11. Moyai は結果を Event History に記録する。Assertion 本文と Header は記録しない。
12. Assertion の参照を破棄する。Cache や File へ保存しない。

## Provider 検証順序

情報漏えいを抑えるため、Provider は原則として次の順に検証する。

1. Header Size、Payload Size、Token Format
2. 許可 Algorithm と `kid`
3. 署名
4. `iss`、`sub`、`aud`、`prv`、`protocol_version`
5. `nbf`、`iat`、`exp`
6. `jti` Replay
7. `project` と Repository Context
8. Scope と Provider Capability
9. Provider 固有 Policy と Branch Protection

外部向け Error は詳細を正規化し、署名検証前に Project や鍵の存在を推測できる情報を返してはならない。

## Assertion の更新

Refresh Token は使用しない。Assertion が失効または期限切れになった場合、Moyai は元の認可判断を再実行し、新しい `jti` と時刻で新規 Assertion を1回だけ発行できる。

- Provider は Assertion を更新しない。
- Provider は Moyai に Token Refresh を要求しない。
- AI Agent は Assertion の取得、更新、転送を行わない。
- 変更操作が Provider に到達した可能性がある場合、状態を確認せず自動再実行してはならない。

## Replay Cache

- Key は `iss + jti` とする。
- TTL は `exp + clock_skew_seconds` までとする。
- 登録は Check-and-Set を単一の原子操作で行う。
- Provider の複数 Instance 構成では共有 Cache を使用する。
- Cache 利用不能時、変更操作は Fail Closed とする。
- Read-only 操作の Fail Open は本仕様では許可しない。

## 鍵更新

Signing Key の状態は次の順で遷移する。

```text
next -> active -> retiring -> retired
                    |
                    +-> revoked
next/active/retiring -> revoked
```

1. Moyai は新しい Key Pair を `next` として生成する。
2. 公開 JWK を全対象 Provider の Trust Bundle に登録する。
3. 全 Provider で検証可能なことを確認する。
4. 新しい鍵を `active` にし、旧鍵を `retiring` にする。
5. Overlap 期間中、Moyai は新鍵だけで署名し、Provider は新旧両方を検証する。
6. `max(assertion_lifetime + clock_skew, key_rotation_overlap)` 経過後、旧鍵を `retired` にする。
7. 漏えい時は対象 `kid` を直ちに `revoked` とし、未期限切れ Assertion も拒否する。

KEK 更新では新規書き込みを新 KEK へ切り替え、既存 `wrapped_dek` を段階的に Rewrap する。Payload の再暗号化は DEK 漏えい時だけ必要とする。

## Secret の保存

1. 256-bit DEK と 96-bit Nonce を CSPRNG で生成する。
2. Canonical AAD を生成する。
3. Secret Plaintext を AES-256-GCM で暗号化する。
4. Key Protector の Active KEK で DEK を Wrap する。
5. Envelope を一つの DB Transaction で保存する。
6. Plaintext と DEK の Memory Buffer を可能な範囲で消去する。
7. Log、Exception、Telemetry、Backup Manifest に秘密値を出力しない。

## Secret の取得

1. Secret ID と Owner Context でレコードを取得する。
2. AAD を再構築し、保存 Metadata と一致することを確認する。
3. `key_version` に対応する Key Protector で DEK を Unwrap する。
4. AES-256-GCM の Tag を検証しながら復号する。
5. 復号済み Secret を要求した Provider 内の操作 Scope だけで使用する。
6. AAD、Tag、Key Version の不一致はすべて Fail Closed とする。

## Provider 登録

Provider の登録では次を明示する。

- 一意な Provider ID
- Endpoint
- 対応 Protocol Version
- 対応署名 Algorithm
- 必須 Audience
- 共通 Tool と Scope の対応
- Trust Bundle 配布方式
- 外部サービス Credential の保管方式
- Key Protector Adapter と Health Check

Project 登録は Provider 登録後に行い、Project の Repository Host と Provider ID の不一致を拒否する。

## 入力

Provider 認証が必要な Moyai 操作の入力は、既存 Repository Provider Contract の入力に限定する。Client が次を渡した場合は拒否する。

- `authorization`、`token`、`password`、`private_key` 等の秘密入力
- Provider ID と矛盾する Repository URL
- Project DB と一致しない Repository Context
- 未知 Scope の直接指定

Scope は Client 入力から受け取らず、Moyai が Tool と実行計画から決定する。

## 出力

成功時は既存の標準応答に次を含める。

```json
{
  "ok": true,
  "operation_id": "repo-op-018f4bdb",
  "provider": "buckettie",
  "authentication": {
    "protocol_version": "1",
    "key_id": "moyai-signing-2026-09",
    "authorized_scopes": ["repository.push"]
  },
  "result": {}
}
```

Assertion、署名値、Credential、暗号文、Wrapped DEK は出力しない。`key_id` は監査用公開識別子として返却可能とする。

## 状態遷移

Provider Authentication の状態は次とする。

| State | Meaning | 許可動作 |
| :--- | :--- | :--- |
| `unconfigured` | Provider または Trust Bundle 未設定 | Version、Health、設定検査のみ |
| `initializing` | 鍵または Trust を準備中 | 認証操作は禁止 |
| `ready` | 発行、検証、Replay 防止が利用可能 | Capability が許可する操作 |
| `degraded` | 一部 Provider または旧鍵に問題 | 影響を受けない Provider のみ |
| `locked` | Key Protector が利用不能または解除待ち | 秘密を必要としない診断のみ |
| `revoked` | Active Signing Key が失効 | 全認証操作を禁止 |

状態変更は時刻、対象 Provider、Key ID、理由を Audit Log に残す。秘密情報は残さない。

## エラー

| Code | Condition | Retry |
| :--- | :--- | :--- |
| `authentication_unavailable` | 認証基盤が `ready` でない | Health 回復後 |
| `auth_assertion_missing` | Authorization Header がない | 新規 Assertion で再要求 |
| `auth_assertion_invalid` | Format、署名、必須 Claim が不正 | 不可。設定確認 |
| `auth_assertion_expired` | `exp` 超過 | Moyai が認可を再評価後に1回だけ新規発行 |
| `auth_assertion_not_yet_valid` | `nbf` より前 | 時計同期後 |
| `auth_audience_mismatch` | `aud` または `prv` が Provider と不一致 | 不可 |
| `auth_scope_denied` | 必須 Scope がない | 不可。Scope Mapping 確認 |
| `auth_project_mismatch` | Project または Repository が Context と不一致 | 不可 |
| `auth_key_unknown` | `kid` が Trust Bundle にない | Trust 更新後 |
| `auth_key_revoked` | `kid` が失効済み | 新鍵で再要求 |
| `auth_replay_detected` | `iss + jti` が使用済み | 操作結果を確認。自動再実行禁止 |
| `auth_protocol_unsupported` | Protocol Version 非対応 | 構成更新後 |
| `auth_key_provider_unavailable` | OS Key Store、HSM 等を利用不可 | 基盤回復後 |
| `auth_secret_decryption_failed` | AAD、Tag、Wrapped Key の検証失敗 | 自動 Retry 禁止 |
| `provider_capability_missing` | Tool または Scope 非対応 | Provider 更新後 |

Error Message、Log、Telemetry に JWT 全文、Claim 全文、Token、秘密鍵、復号済み Secret を含めない。

## 監査

Audit Event には次だけを記録する。

- `operation_id`
- Provider ID
- Project UUID
- Repository の非機密識別子
- Scope
- Key ID
- 発行、検証、拒否、更新の Event Type
- 結果 Code
- UTC Timestamp

`jti` は必要な場合に Hash 化して保存する。Assertion 全文、Signature、外部サービス Credential は保存しない。

## Backup と Restore

- 暗号化 DB Backup は許可する。
- KEK、Signing Key、OS Key Store Export を同じ Backup Archive に含めてはならない。
- Restore 手順は DB と Key Material を別経路で復元する。
- Key Material がない DB は復号不能であることを正常な Fail Closed とする。
- Restore 後は Instance ID、Trust Bundle、Clock、Replay Cache を検証してから `ready` にする。

## Migration

既存の静的 Service Token 方式から次の順で移行する。

1. Provider 共通認証層と Trust Bundle を導入する。
2. Moyai Signing Key を初期化し、公開 JWK を Provider へ登録する。
3. Provider を `dual-accept` にし、旧 Token と Assertion の両方を監査付きで受け付ける。
4. Moyai を Assertion 発行へ切り替える。
5. 全操作が Assertion で成功していることを確認する。
6. 旧 Token の受付を停止する。
7. Provider 側で旧 Token を失効し、安全に削除する。
8. `dual-accept` 設定を削除する。

`dual-accept` の既定期間は7日以内とし、Production で期限なし設定を禁止する。旧 Token を AI Agent、MCP Argument、Migration Log へ出力してはならない。

## テスト

### Protocol

- 正しい `ES256` Assertion だけが受理される。
- `alg=none`、Algorithm Confusion、未知 `kid` が拒否される。
- `iss`、`aud`、`prv`、Project、Repository、Scope の各不一致が拒否される。
- `iat`、`nbf`、`exp` の境界値と Clock Skew が仕様どおり処理される。
- 同一 `jti` の同時要求で1件だけが受理される。
- Githubie 用 Assertion が Buckettie で拒否され、その逆も拒否される。
- Read Scope で Push、Commit Scope で Release が拒否される。

### Storage

- DB File 単体から Secret Plaintext と DEK を復元できない。
- Ciphertext、Nonce、Tag、Wrapped DEK、AAD の各改ざんが検出される。
- レコードを別 Provider、別 Project、別 Secret Kind へコピーすると復号に失敗する。
- Nonce が同じ DEK で再利用されない。
- KEK 更新後、旧レコードが Rewrap 前後で正しく復号される。
- Secret、DEK、Assertion が Log、Exception、Crash Dump 用 Metadata に出ない。

### Key Rotation

- `next` 登録前に署名へ使用されない。
- Overlap 中は新旧鍵を検証でき、署名は新鍵だけで行われる。
- `retired` または `revoked` 鍵の Assertion が拒否される。
- Provider の一部で Trust 更新に失敗した場合、Active 切替が中止される。

### Platform

- Windows で DPAPI/CNG Adapter の生成、再起動後復号、別ユーザー拒否を確認する。
- macOS で Keychain Adapter の生成、再起動後復号、Access Control 拒否を確認する。
- Linux Desktop で Secret Service Adapter の生成、再起動後復号、Collection Lock を確認する。
- Linux Server で選択した PKCS#11、TPM または KMS Adapter の利用不能時 Fail Closed を確認する。
- 3 OS で同一の DB Schema、JWT Claim、Provider Contract Test Suite が通る。

### Operation

- Key Protector、Replay Cache、Provider Endpoint の各停止時に変更操作が実行されない。
- Assertion 期限切れ時の再発行が最大1回である。
- Push 結果不明時に自動再実行せず、Status 確認を要求する。
- Backup と Key Material を分離した Restore Test が成功する。
- Static Token 移行後に旧 Token が拒否される。

## 受け入れ条件

1. AI Agent を経由せずに Moyai が操作ごとの Assertion を発行できる。
2. Provider 間で Refresh Token の送受信を行わず、期限切れ時に新規 Assertion を発行できる。
3. Audience、Project、Repository、Scope、期限、Replay のすべてを Provider が強制できる。
4. Provider 追加時に Core を変更せず、Capability、Scope Mapping、Trust Bundle、Adapter の追加で対応できる。
5. 長期秘密情報を暗号化 DB へ保存しても、KEK が DB の外で保護される。
6. Windows、Linux、macOS で `IKeyEncryptionKeyProvider` と `IAssertionSigner` の同一 Contract を利用できる。
7. KelpieSSH 由来の Envelope 設計を再利用しつつ、DPAPI 直接依存を Core から排除できる。
8. 改ざん、鍵不明、Key Protector 障害、Replay 時に Fail Closed となる。
9. Assertion、Token、Password、Private Key が MCP Argument、標準応答、Log に現れない。
10. Protocol、Storage、Rotation、Platform、Migration の必須 Test がすべて合格する。

# 設計判断

## 採用した判断

### 操作ごとの短寿命 Assertion

Refresh 可能な長期 Token を Moyai と Provider の間で共有せず、各操作の直前に Assertion を発行する。漏えい時の有効時間と権限範囲を限定でき、AI を Token 更新経路から外せるためである。

### Provider ごとの Audience

Assertion の `aud` は単一 Provider に固定する。ある Provider 向け Assertion が別 Provider で利用されることを防ぐためである。

### Provider 汎用 Scope

認可単位を GitHub、Bitbucket 固有 API ではなく Repository Provider Contract の操作へ対応させる。Provider 追加時の Core 変更を抑えるためである。

### ES256 Baseline

Windows、Linux、macOS の一般的な暗号 API と相互運用しやすく、非対称署名によって Provider に署名秘密を配布せずに済むためである。

### Envelope Encryption

Secret ごとの DEK と、DB 外で保護する KEK を分離する。DB 漏えい耐性、部分更新、KEK Rotation を両立するためである。

### Key Protector 抽象化

KelpieSSH の DPAPI 利用を Windows Adapter として再利用し、Core には Interface だけを置く。Linux と macOS への移植性を確保するためである。

### Provider Credential の所有権維持

GitHub、Bitbucket 等の外部サービス Credential は各 Provider が保有し、Moyai DB へ集約しない。侵害範囲と責務を Provider 単位に分離するためである。

## 見送った案

| 案 | 見送る理由 |
| :--- | :--- |
| Moyai と Provider の共通 Static Token | 長期秘密の配布、更新、漏えい範囲が広い |
| Provider 間の Refresh Token 通信 | Provider 結合が強くなり、更新用長期秘密が増える |
| 全 Provider 共通 JWT | Audience 分離がなく、横展開を防げない |
| 共通 HMAC Key | 検証側 Provider が署名能力も持ち、侵害時に他 Provider へ偽装できる |
| KEK を暗号化 DB と同じ File に保存 | DB 単体漏えいへの防御にならない |
| Windows DPAPI への直接依存 | Linux と macOS へ同じ Core を移植できない |
| 長期秘密を環境変数へ保存 | Process 環境、診断情報、子 Process への露出を制御しにくい |
| Assertion の永続 Cache | 短寿命 Credential の攻撃面と回収対象を増やす |
| 認証障害時の Fail Open | Repository 変更権限を無認証で許可する結果になる |

## 実装上の禁止事項

- Assertion または外部サービス Token を MCP Tool の入力 Schema に追加しない。
- Provider Adapter ごとに独自の JWT 検証処理を複製しない。
- `aud`、Scope、Project、Repository、Replay のいずれかを省略して署名だけで許可しない。
- Key Protector 利用不能時に平文 File、固定 Key、Hard-coded Key へ自動退避しない。
- Nonce、DEK、`jti` を予測可能な値から生成しない。
- 暗号エラー時に破損レコードを既定値や空文字として扱わない。
- Debug Log であっても Assertion、Secret、Plaintext Key Material を出力しない。
- Provider 追加のために Moyai Core へ Provider 固有 Token 更新処理を追加しない。
