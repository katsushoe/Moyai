# Provider Authentication Consumer Contract

## Status

2026-09-07にGithubie、Buckettie、KelpieSSHから受領した実装前確認へのMoyai回答です。Provider側の実装は、このContractと共通ライブラリを使用します。

共通ライブラリ1.0.2とMoyai側KelpieSSH Protocol v2・段階Deploy Adapterのローカル実装は完了しています。生成物は`artifacts/provider-authentication/Moyai.ProviderAuthentication.1.0.2.nupkg`、SHA-256は`BBBCD73CFD1CA1D005921AEC5208CFEF4E3891587DE2AC1838C3FBCB3C8802A0`です。1.0.1はReplay予約中に期限を越えたAssertionをPrincipal返却前に拒否し、1.0.2は承認待機等の後にReplayを再予約せず期限・最新鍵状態・Capabilityを再検証する`IAssertionExecutionValidator.EnsureCurrentAsync`を追加します。ProjectReferenceを持たない独立ConsumerへのPackageReference復元・実行にも合格しています。共有CR領域のGithubie、Buckettie、KelpieSSH各Inboxへ同一HashのNuGetとchecksumを再配布し、再検証を依頼しました。実KelpieSSHとの結合はProvider側実装との隔離環境を準備して行います。

## 共通ライブラリ

MoyaiはProvider配布用の`Moyai.ProviderAuthentication` NuGetパッケージを分離しました。現行版は`1.0.2`、対象は.NET 8です。ProviderからMoyai Repositoryへの固定相対`ProjectReference`、Validatorのソース複製、Provider独自JWT実装は使用しません。

パッケージには公開Contract、ES256 Validator、JSON/JWK処理、File Trust Bundle、SQLite Replay Cacheとその独立初期化を含みます。Moyai固有のProject Repository、`SqliteDatabaseOptions`、Moyai本体Schemaへ依存しません。Replay DBの場所とTrust Bundleの場所はProviderが明示設定し、既定の共有パスや秘密値をパッケージへ持たせません。NuGet成果物とSHA-256を共有CR領域へ配置してから、各Providerへ参照先を通知します。

## Protocol

Repository Providerは既存の`protocol_version=1`を使用します。必須Contextは単一`aud/prv`、Moyai Project UUID、正規化Repository ID、単一Toolと最小Scopeです。

Lifecycle Providerは`protocol_version=2`を使用します。v2はv1の`repository` Claimを要求せず、代わりに次を必須とします。

- `resource_kind`: 現在は`kelpie_target`。
- `resource`: KelpieSSH管理の不変Target ID。Profile表示名やClient自己申告値ではありません。

Validatorは設定されたProtocol版にないClaim、v1とv2のContext混在、未知の`resource_kind`を拒否します。v1とv2は同じIssuer、ES256、Trust Bundle、期限、鍵状態、Replay、Operation ID検証を共有します。

## 正規Audience

- Githubie: `githubie`。既存の`githubbie`はMoyai内部routing互換名だけに残し、Assertion発行前に`githubie`へ解決します。`aud=githubbie`は拒否します。
- Buckettie: `buckettie`。
- KelpieSSH: `kelpiessh`。既存の`server`はMoyai内部routing互換名だけに残し、Assertion発行前に`kelpiessh`へ解決します。`aud=server`は拒否します。

## Project対応付け

Moyai Project UUIDは既存の`list_projects`／Project取得結果に含まれるIDを管理者が参照します。Providerは管理操作でUUIDを登録済みRepositoryまたはTargetへ明示対応付けし、実Tool引数からProvider自身の登録情報を解決してClaimと比較します。Claim内UUID、Repository、Target IDを登録値として自動採用しません。未登録、重複、Context不一致は拒否します。

Provider側の対応付け登録・更新・削除は通常のRepository／Deploy Toolと分離した管理境界です。MoyaiのProvider AssertionではProject文脈のないProvider管理Toolを認可しません。Provider HTTP入口に管理Toolを残す場合は、Provider自身が別の管理者認証を定義します。

## 既存Tokenと直接接続

Githubie、Buckettie、KelpieSSHの現行MCP入口にはMoyai Service Token受入検証がありません。したがってProvider側のdual-acceptと旧MCP Service Token削除は非該当です。Moyaiが現在送信しているKelpieSSH向けBearer Tokenは受信側で認証されていないため、Protocol v2移行時に送信を停止します。

GithubieのGitHub Token、BuckettieのBitbucket Token、KelpieSSHのSSH資格情報は外部サービス用Credentialであり、削除・移管しません。Moyai管理下の変更ToolはAssertion必須とします。Codex／ClaudeがProviderへ直接接続する場合も、同じ変更Toolを無Assertionで呼び出せません。直接管理が必要な操作はProvider固有の管理者境界を使用します。Bootstrapはinitialize、discovery、version、capabilityだけに限定し、Project一覧を返しません。

2026-09-24のユーザー判断により、Codex／ClaudeからBuckettieへの直接接続では、読み取りToolをAssertionなしで許可します。対象は、Scope Mappingが`repository.read`だけを要求するToolと、Provider登録一覧（`list_projects`）です。条件はloopback入口からの呼び出しに限ることで、状態変更を伴わない操作だけとします。`fetch`・`pull`のようにローカル状態を変える操作や、変更ToolはこれまでどおりAssertion必須です。Assertionを伴う要求は従来どおり完全に検証し、失敗時に無Assertionの読み取り許可へ退避しません。Moyai経由の読み取りは引き続きAssertionを付与します。GithubieとKelpieSSHの直接接続は変更しません。

### Buckettie単体運用

2026-09-25のユーザー承認により、Buckettieは起動オプションで動作モードを切り替えます。本節はBuckettieだけに適用し、GithubieとKelpieSSHの扱いは変更しません。

1. `--moyai`なしの起動を既定の単体モードとします。単体モードはMoyai管理下ではない独立した運用形態で、移行モードや無効化モードには当たりません。仕様正本の`enabled: false`禁止は、Moyai連携モードに適用します。
2. 単体モードでは、Buckettie固有のポリシーに従い、loopback入口からのAuthorizationなしのRepository Toolを許可します。
3. `Buckettie.Server.exe <config> --moyai`で起動した場合だけMoyai連携モードとし、本Contractの制約に従います。`provider_authentication`の欠落・不正時は単体モードへ退避せず、起動を拒否します。
4. Moyaiは、Buckettieへ状態変更操作・`fetch`・`pull`を委譲する前に、`bitbucket_provider_capabilities.data.authentication.integration_mode`が`moyai`であることを確認します。`moyai`でない場合、または確認できない場合は委譲せず、`provider_integration_mode_mismatch`を返します。確認はMoyai 1.3.2.0で実装し、Bootstrapの同Toolを無Assertionで呼び出します。読み取り操作は確認しません。

## Scope

Repository Toolは既存のProvider非依存Scope Mappingを使用します。Project文脈のない登録・更新・削除ToolはMapping対象外です。

KelpieSSHは次の単一Scopeを各Toolへ対応させます。

- `target_status`: `target.status`
- `deploy_prepare`: `deploy.prepare`
- `deploy_upload`: `deploy.upload`
- `deploy_activate`: `deploy.activate`
- `deploy_verify`: `deploy.verify`
- `deploy_rollback`: `deploy.rollback`
- `deploy_cleanup`: `deploy.cleanup`
- 追加するDeployment状態照会Tool: `deploy.status`

Capabilityは正規Audience、Protocol版、ES256、Replay必須、Tool-to-Scope Mapping、`resource_kind`を公開します。

## KelpieSSH段階実行と再実行

Moyaiは現行の単一`server_deploy`／`server_deploy_rollback` Adapterを、KelpieSSHの段階Deploy Contractへ置き換えました。KelpieSSHは`deploymentId`、Project UUID、Target ID、入力Hash、状態を再起動後も解決できる永続Storeへ保存し、状態照会Toolを追加します。

認証層がProvider Tool未実行を保証した`auth_assertion_expired`だけ、Moyaiが認可Contextを再取得した後に新Assertionで最大1回再試行できます。通信断、Timeout、Protocol失敗など結果不明の場合、状態照会以外を自動再実行しません。同一`deploymentId`と同一入力は冪等に扱い、入力差は競合として拒否します。

## 隔離結合テスト

本番とは別のloopback port、設定、Trust Bundle、Replay DB、Provider管理DB、使い捨てIssuer鍵を使用します。Repository Providerは2 ProjectとAudience相互隔離、KelpieSSHは使い捨てTargetと制限されたSSH環境を使用します。正常系、各Context不一致、時刻、Replay、再起動後Replay、鍵失効、Gateway未実行、秘密非露出、両MCP Client互換を検証します。

インストール、サービス更新、公開Releaseは別途承認を受けるまで実行しません。
