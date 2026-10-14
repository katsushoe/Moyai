# Repository Provider Contract

## Settings Consumer extension (development1.3.11.0)

Githubie settings agreement is recorded in [ADR0012](../adr/0012-repository-settings-consumer.md). This implementation is not installed and is not a confirmed Buckettie contract. Only Githubie settings routing is enabled; other Providers and legacy authentication are rejected before dispatch.

Consumer operations are `repository_settings_get(project)` and `repository_settings_update(project, changes)`; Provider tools are `github_repository_settings_get/update`, exact capability keys `repository_settings_read/update`, read scope `repository.read` and update scope `repository.settings.write` plus Provider human approval. Preserve mapped registration ID and signed UUID/URL/tool context. Forward only explicit changes; omission preserves, false is explicit, arrays replace. Null/unknown/wrong-type/empty changes and incomplete authors are rejected. Provider remains the policy/persistence authority.

Return only eight registered settings, actual changed fields and affirmative approval on update success. Preserve settings/authentication errors with safe correlation IDs; do not expose raw Provider output. Update response loss or invalid post-dispatch response is unknown, not a known rejection. No automatic update replay; obtain a separate read before deciding another update. Provider admission rejection remains fail-closed. CONFIG/COMMANDS describe setup and symmetric CLI JSON input.

状態: 確定（2026-09-27、ユーザー承認）
対象: Moyaiから委譲されるRepository Provider（Githubie、Buckettie、今後追加するProvider）
関連: [Provider Consumer Contract](provider-authentication-consumer-contract.md)、`src/Moyai.Infrastructure/Providers/RepositoryProviderContract.cs`、CR `CR-2026-09-27-provider-git-remote-resolution`

本書は、Providerの認証以外の共通契約を定めます。認証・認可はProvider Consumer Contractを正本とします。

## Gitリモートの解決

### 1. 解決規則

Providerは、ローカルリポジトリのGit設定（`remote.<name>.url`）から、Projectの`repositoryUrl`と一致するリモートを自動で選びます。

- URLの比較は、正規化した値の完全一致とします。正規化はMoyaiのAssertion Repository Claimと同じ規則です。
  - `git@host:owner/repo` 形式は `ssh://git@host/owner/repo` とみなします。
  - 接続方式（`https`／`ssh`）、`.git`接尾辞、末尾の`/`は比較対象から除きます。ホスト名は小文字化し、既定以外のポートは残します。パスの大文字小文字は維持します。
  - 資格情報（`user:password@`）、query、fragmentを含むURLは、比較せずに拒否します。
  - 例: `https://bitbucket.org/stk2k/cupperpro.git` と `git@bitbucket.org:stk2k/cupperpro.git` は、どちらも `bitbucket.org/stk2k/cupperpro` として一致します。
- Providerが自身のRepository登録でリモート名を保持している場合は、`gitRemoteName`の指定がないときに限り、そのリモートを最初の候補にしてよいものとします。この場合も下記3と同じ検証を行い、URLが一致しなければ`provider_remote_mismatch`、存在しなければ`provider_remote_not_found`で拒否します。自動解決や`origin`へは退避しません（2026-09-27追記、Buckettie 1.3.36.0の実装に合わせて明確化）。
- 一致するリモートが複数ある場合は、次の順に選びます。
  1. `gitRemoteName`が指定されていれば、その名前（下記3の検証を行います）。
  2. 下記2の命名規則に合う名前が1つだけあれば、その名前。
  3. それでも一意に決まらなければ、`provider_remote_ambiguous`で拒否します。
- 一致するリモートがない場合は、`provider_remote_not_found`で拒否します。既定値`origin`への暗黙の退避は行いません。
- 解決は、操作ごとにProviderが実行直前に行います。解決結果（リモート名）は、Providerの応答に含めてよいものとします。

### 2. リモート名の命名規則（推奨）

`<ホスト>-origin-<接続方式>` とします。ホストはサービス名の短縮形です。

- 例: `bitbucket-origin-https`、`bitbucket-origin-ssh`、`github-origin-https`
- 既存リポジトリの改名は任意です。上記1の自動解決により、既存の`origin`もURLが一致すれば利用できます。
- Providerが対応しない接続方式（例: BuckettieのSSH）のリモートは、解決候補から除外してよいものとします。除外によって候補がなくなった場合や、指定名のリモートが未対応の接続方式だった場合は、`provider_remote_not_found`です。Provider固有の詳細（例: `ssh_remote_not_supported`）は、`error.provider.code`で示してよいものとします。

### 3. `gitRemoteName`の扱い

- 省略可能とします。省略時は、上記1の自動解決を行います。
- 指定された場合は、その名前のリモートを使います。そのリモートの正規化URLが`repositoryUrl`と一致しなければ、`provider_remote_mismatch`で拒否します。指定名のリモートが存在しない場合は、`provider_remote_not_found`です。

### 4. 対応の表明

各Providerは、`*_provider_capabilities`の`data`に次を含めて対応を表明します。

```json
{ "remote_resolution": { "version": 1, "mode": "repository_url" } }
```

- `remote_resolution`がないProviderは、本規則に未対応とみなします。Moyaiは未対応Providerにも従来どおり委譲しますが、利用者には未対応であることを示します。
- 本規則の改訂時は、`version`を上げます。

### 共通エラーコード

| コード | 意味 |
|---|---|
| `provider_remote_not_found` | `repositoryUrl`と一致するリモート、または指定名のリモートがない |
| `provider_remote_ambiguous` | 一致するリモートが複数あり、命名規則でも一意に決まらない |
| `provider_remote_mismatch` | 指定された`gitRemoteName`のURLが`repositoryUrl`と一致しない |

## Provider Repository登録ID

Moyai Projectの `providerRepositoryId` は、Providerの登録IDをProject名とは別に保持します。未設定なら従来どおりProject名を `repository` 引数として渡し、明示設定した場合は登録IDを使います。Project名、UUID、正規化Repository URL、audience、scopeは変更しません。Project名変更時にも明示した登録IDは維持します。

登録・更新・解除は `project_provider_repository_set` を使い、現在revisionと監査を適用します。`clearMapping=true` で解除し、Project名へのfallbackを戻します。設定形式・schema v7・運用条件は[ADR 0011](../adr/0011-provider-repository-id.md)を正本とします。Provider自身の登録・対応付け検証は維持され、Moyai側の設定保存だけで認可を成立させません。

この対応はMoyai 1.3.10.0のソース実装です。稼働環境への導入とNeon_SDKの読取結合確認は別の完了条件です。

## Build開始時のstatus読取

MoyaiのBuild状態読取は、Source Commitとして従来の`commit`／`head_sha`／`head`に加えて`local_head`を認識します。作業ツリー判定は`dirty`／`is_dirty`を優先し、これらがない場合は`working_tree_clean`の真偽を反転してDirtyを判定します。Provider応答の`data`内の値も対象です。これはMoyaiの既存応答への対応であり、Providerへ新しい応答形式を要求するものではありません。

Dirtyの場合、またはSource Commitが欠落・空の場合は、Build Provider呼出しとBuild記録の作成より前に拒否します。

## Moyai側の実装方針

- Moyaiは`gitRemoteName`を保持し、下記の条件を満たす操作ではProviderの`remote`引数へ転送します。未指定の場合はProvider側の解決規則に委ねます。
- `Project.GitRemoteName`は省略可能（`null`許容）とし、新規Projectの既定値`origin`は使用しません。
- 既存Projectの`git_remote_name='origin'`は、利用者が明示した値と区別できないため、DB schema v6への移行で`null`（自動解決）へ変更します。`origin`以外の値は明示指定として保持します。
- `gitRemoteName`が設定され、Providerが`remote_resolution` version 1 / `repository_url` modeを表明した場合だけ、remote引数を渡します。対象はGithubie／Buckettieの`repository_status`と、既存Toolがremote引数を受け取るBuckettieの`commit`です。Githubieの`commit`およびその他の操作にはremoteを送りません。未対応または能力を取得できないProviderにもremoteを送りません。
- Buckettieの`commit`では、従来どおり`integration_mode=moyai`を確認してから委譲します。Assertionのscope／Repository Claim、ProviderのURL照合・ブランチ保護は変更しません。登録remoteや認証情報を変更せず、明示されたremoteをProviderの既存検証へ渡します（2026-10-08追記）。
- Providerが返す共通エラーコード3種は、`RepositoryProviderContract`で正規化後も同じコードを利用者へ返します。
- Moyai側の基本実装とunit testsは2026-10-03に追加しました。2026-10-07に、非`origin` remoteを持つ隔離ProjectでMoyai経由のGithubie capabilities・statusの成功を確認しました（`artifacts/nonorigin-integration/e2e-20261007.json`）。
