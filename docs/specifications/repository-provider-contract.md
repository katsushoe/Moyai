# Repository Provider Contract

## Buckettie Diff remote forwarding extension (development)

Only Buckettie Diff additionally receives an exact nonblank Project.gitRemoteName as `remote` after a successful `remote_resolution` version1/mode `repository_url` capability lookup. Missing/blank remote skips negotiation; absent/malformed/failed/unsupported capability omits the argument for compatibility. Omission does not prove validation of an explicit remote. Reuse mapped repository identity and repository.read assertions; preserve raw diff text and Provider remote errors, without origin fallback after rejection. Diff remains a local HEAD working-tree comparison, not remote-branch diff. Githubie Diff remains excluded pending confirmation of its remote-validation semantics. No new CLI/MCP input, DB field or scope is introduced. Source implementation is not installed/live acceptance. See [ADR0016](../adr/0016-buckettie-diff-remote.md).

## Pull request creation extension (development)

Consumer `pull_request_create(project, sourceBranch, destinationBranch, title, description, draft)` has no default branch/draft. Githubie uses `github_pr_create` with snake_case route arguments and `provider.githubie.pull_request.write`. Buckettie uses `bitbucket_pr_create` with camelCase route arguments, `provider.buckettie.pr_create` and Moyai integration mode. Both receive repository/title/description/draft. Require successful explicit capability `data.pr_create=true` (Githubie) or `data.operations.pr_create=true` (Buckettie); these enablement keys still require Provider agreement/rollout.

Use mapped registration ID and Project-bound assertion, never generic commit/push rights or legacy. Provider owns allowed routes/protection/approval; no policy changes, push, merge or fallback. Common result fields and Provider-specific output mapping are defined in [ADR0015](../adr/0015-provider-pull-request-create.md). Preserve typed rejection code/outcome/detail; lost/malformed post-dispatch result is unknown, never automatically replayed. Real PR acceptance requires a separately approved disposable target.

## Local branch creation extension (development)

MCP `branch_create_local(project, branch, source)` creates only a local branch without checkout. Githubie mapping: `github_local_branch_create`; exact scope: `repository.branch.write` with Project-bound assertions; required capability: successful `data.local_branch_create=true`. `repository` is the mapped ID; branch/source are required and unchanged. Source is a literal local branch/tag or full40-hex SHA, not HEAD or a revision expression. No default/source/remote fallback.

Provider rejects dirty tree, unresolved conflicts, source absence and duplicate branch before mutation. Success adds only the local ref and preserves current branch/HEAD, remote refs, commits, index/worktree, upstream and repository/Project settings. Preserve raw success metadata and exact typed errors. Malformed/lost post-dispatch responses are unknown, never automatically replayed. Buckettie is unsupported until its dedicated contract is confirmed. Runtime capability/scopes and disposable real-Provider acceptance are separate gates. See [ADR0014](../adr/0014-provider-local-branch-create.md).

## Local branch switch extension (development1.3.12.0)

Moyai branch_switch(project, branch) requires an explicit boolean data.branch_switch capability (or Buckettie data.operations.branch_switch), exact repository.branch.write scope, and Project-bound assertions. Resolve registered ID using providerRepositoryId while preserving UUID/URL. Githubie tool is github_branch_switch; Buckettie tool is buckettie_branch_switch despite the configured bitbucket prefix, and requires Moyai integration mode.

Only repository/branch arguments are sent. Provider validates existing local branch, dirty tree and policy; there is no fetch, creation or default-branch change. Raw success preserves Githubie data.previous_branch/current_branch and Buckettie data.branch_switch. Branch-not-found, invalid-ref, dirty and policy rejection codes are preserved with Provider detail. Post-dispatch response loss is resultUnknown=true and requires status confirmation before another request. Runtime configuration and real Provider acceptance remain separate. See [ADR0013](../adr/0013-provider-branch-switch.md).

## Settings Consumer extension (development1.3.11.0)

Both Providers' agreed Settings Consumer routes are recorded in [ADR0012](../adr/0012-repository-settings-consumer.md). Source implementation does not establish installed enablement or real-Provider acceptance. Other Providers and legacy authentication are rejected before dispatch.

Consumer operations are `repository_settings_get(project)` and `repository_settings_update(project, changes)`. Githubie tools are `github_repository_settings_get/update`, with exact boolean capability keys `data.repository_settings_read/update`; Buckettie tools are `bitbucket_repository_settings_get/update`, with `data.operations.repository_settings_read/update`. Buckettie requires `integration_mode=moyai` for both operations. Read scope is `repository.read`; update is `repository.settings.write` plus Provider human approval. Preserve mapped registration ID and signed UUID/URL/tool context. Forward only repository and explicit changes; omission preserves, false is explicit, arrays replace. Null/unknown/wrong-type/empty changes and incomplete authors are rejected. Additional Provider fields such as Buckettie's pull_request_routes are not editable here. Provider remains the policy/persistence authority.

Return the eight common registered settings, plus Githubie's optional `local_commit_branches` when present, actual changed fields and affirmative approval on update success. The Githubie-only extension accepts an exact string-array replacement, including `[]`; omission preserves and null/wrong-type/blank elements are rejected. Do not derive it from push/PR permissions. Missing Provider output omits the optional property for backward compatibility; explicit empty output remains `[]`. Githubie `changed_fields` may name this field only when its registered value is returned. Buckettie updates reject the extension before dispatch and retain the eight-field contract. Unrelated extra GET fields remain ignored, not editable. Preserve settings/authentication errors with safe correlation IDs; do not expose raw Provider output. Update response loss or invalid post-dispatch response is unknown, not a known rejection. No automatic update replay; obtain a separate read before deciding another update. Provider admission rejection remains fail-closed. CONFIG/COMMANDS describe setup and symmetric CLI JSON input. See the 2026-10-20 extension in [ADR0012](../adr/0012-repository-settings-consumer.md).

状態: 確定（2026-09-27、ユーザー承認）
対象: Moyaiから委譲されるRepository Provider（Githubie、Buckettie、今後追加するProvider）
関連: [Provider Consumer Contract](provider-authentication-consumer-contract.md)、`src/Moyai.Infrastructure/Providers/RepositoryProviderContract.cs`、CR `CR-2026-09-27-provider-git-remote-resolution`

本書は、Providerの認証以外の共通契約を定めます。認証・認可はProvider Consumer Contractを正本とします。

## 選択commit patchの受け渡し

`repository_diff`はProviderが生成した結果をMCPのJSON文字列として返します。Moyaiはunified diffを生成・編集・改行変換せず、Provider応答を`RepositoryProviderResult.Output`へ保持します。`repository_commit_selected`も受け取ったpatch文字列をJSON引数`patch`へそのまま渡します。UTF-8本文、日本語、BOM、複数hunkの内容をMoyai側で正規化しません。

差分の妥当性検証、expected HEADとの照合、staged changeの拒否、patch適用とcommitの原子性はProviderの責務です。MoyaiはProvider capabilityが明示された場合だけ選択commitを呼び出し、`selected_commit_patch_invalid`、`expected_head_mismatch`、`staged_changes_present`を呼び出し元へ識別可能なまま返します。Moyaiのテストは、diff応答とcommit引数のpatch文字列がMCP transport境界で完全一致することを検証します。

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
- `gitRemoteName`が空白以外で設定され、Providerが`remote_resolution` version 1 / `repository_url` modeを表明した場合だけ、remote引数を渡します。対象はGithubie／Buckettieの`repository_status`と`repository_commit_selected`、Buckettieの`commit`と`repository_diff`です。Githubieの通常`commit`と`repository_diff`およびその他の操作にはremoteを送りません。未対応または能力を取得できないProviderにもremoteを送りません。Buckettie Diffの追加判断・互換条件は[ADR0016](../adr/0016-buckettie-diff-remote.md)を参照してください。
- Buckettieの`commit`では、従来どおり`integration_mode=moyai`を確認してから委譲します。Assertionのscope／Repository Claim、ProviderのURL照合・ブランチ保護は変更しません。登録remoteや認証情報を変更せず、明示されたremoteをProviderの既存検証へ渡します（2026-10-08追記）。
- Providerが返す共通エラーコード3種は、`RepositoryProviderContract`で正規化後も同じコードを利用者へ返します。
- Moyai側の基本実装とunit testsは2026-10-03に追加しました。2026-10-07に、非`origin` remoteを持つ隔離ProjectでMoyai経由のGithubie capabilities・statusの成功を確認しました（`artifacts/nonorigin-integration/e2e-20261007.json`）。
