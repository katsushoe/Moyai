# Moyai Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

ローカル接続管理は `configure codex|claude [--profile <path>] [--config <path>]`、`unconfigure codex|claude [--profile <path>]`、復元は `client-transaction codex|claude --phase rollback|commit [--profile <path>]` です。`--transaction`はMSI用の復元情報を残します。業務MCPではなくインストール管理コマンドです。[MCP設定手順](MCP_SETUP.ja.md)を参照してください。

`moyaictl.exe`の公開コマンド契約です。MCP Toolは同じ操作名をunderscore形式で提供します。MCPはAI向けの`project_list`別名として`list_projects`も公開し、対応するCLI操作は`project-list`です。

## サービス接続と管理

全業務コマンド（`version`を含む）はサービスへ接続します。`--config <path>`でJSON設定を指定します。`commands`は稼働サービスのコマンド一覧と入力schemaを返します。`help`はサービス未起動でも使用できます。

管理コマンドは`service start`、`service stop`、`service pause`、`service resume`、`service register`、`service unregister`、`service status`です。登録は同じbin内のMCP実行ファイルと設定を使用し、Auto／LocalServiceと必要なディレクトリ権限を設定します。登録解除前は停止が必要です。`config-init`は設定不在時だけ初期JSONを作成します。管理コマンドはWindows SCMを使用し、業務処理やDBへ直接アクセスしません。

## Command Groups

| Group | Commands | 用途 |
| :--- | :--- | :--- |
| Project | `project-list`、`project-get`、`project-create`、`project-ensure`、`project-configure`、`project-rename`、`project-update`、`project-set-archived`、`project-overview`、`project-changes-since` | Project状態・集約表示 |
| Work item | `work-item-list`、`work-item-get`、`work-item-create`、`work-item-update`、`work-item-set-deleted`、`work-item-transition`、`work-item-history`、`item-search` | 作業管理・履歴・FTS5検索 |
| Collaboration | `relation-add/remove/list`、`comment-add/list`、`task-link-add/remove/list`、`commit-link-add/remove/list` | WorkItem連携記録 |
| Repository | `repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull` | Provider経由Git操作 |
| Token | `token-issue`、`token-rotate`、`token-revoke`、`token-cleanup` | Service認証 |
| Release | `release-create/get/list/update/transition`、`release-add/remove/list-items`、`release-add/remove/list-artifacts`、`release-prepare/mark-ready/publish/retry/withdraw`、`release-latest/overview` | Release状態、内容、公開 |
| Build | `build`、`build-start`、`build-get`、`build-list`、`build-artifacts`、`build-clean` | 追跡可能なBuild実行とArtifact |
| Deployment | `deployment-target-get/update`、`deploy/start/get/list/status/retry/rollback` | Local／KelpieSSH Deployの追跡管理 |

## Common Options

Optionは`--kebab-case`です。変更操作には`--actor-type`と`--actor-name`、同時実行制御対象には`--expected-revision`も必要です。成功時は標準出力へJSONと終了コード`0`、失敗時は標準エラーへ`command`、`summary`、`ok`、`fatal`、`error`を含むJSONと終了コード`1`を返します。

## Commands

Project名の検索、重複登録判定、変更対象の解決にはOrdinalな大文字小文字非区別比較を使用します。`project-rename`または`project-update`で新しい名前を明示しない限り、登録時の表記を正本として保持します。

各コマンドの構文・処理・戻り値は[英語正本](COMMANDS.md)の同一順序の個別項目を正とします。Command、Option、制約、安全Note、Sample、Linkは英語正本と一致します。

### Project commands

- `project-list`: `project-list [--include-archived]`。既定ではarchive済みを除外します。
- `project-get`: `project-get --name <name>`。保存済みProjectを返します。
- `project-create`: 必須は`--name`だけです。パスやRepositoryなしでProjectを作成でき、実行設定は任意です。同名登録は拒否します。
- `project-ensure`: 必須は`--name`だけです。未登録なら作成し、登録済みなら設定・revision・アーカイブ状態を変更せず返します。同時呼び出しでもProjectと作成Eventは1件です。
- `project-configure`: `--name --expected-revision`が必須です。任意の`--source-path --install-path --repository-url --repository-provider --build-provider --deploy-mode`で後から設定できます。省略値は保持し、古いrevisionは拒否します。create/ensure/configureの`--actor-type --actor-name`は任意で、未指定時は`client`/`unspecified`（認証済み利用者を表すものではありません）です。
- `project-rename`: `--current-name --name --expected-revision`が必須です。名前だけを変更し、ID・全設定・アーカイブ状態・関連データを保持します。更新日時とrevisionを更新し、`project_renamed`監査イベントを記録します。空白名・重複名・古いrevision・対象なしはエラーとなり保存しません。同一名や大文字小文字だけの変更もrevisionを更新します。任意の`--actor-type --actor-name`は既定で`client`/`unspecified`です。MCPは`project_rename`です。例: `moyaictl.exe project-rename --current-name Sample --name NewName --expected-revision 1`。
- `project-update`: `--current-name --name --git-remote-name --expected-revision --actor-type --actor-name`が必須です。任意の`--repository-url`と`--repository-provider`で紐付けを変更でき、URLだけを指定した場合はProviderを再判定します。
- `project-set-archived`: `--name --expected-revision --archived --actor-type --actor-name`が必須です。
- `project-overview --project`はOpen WorkItem件数、blocker、最新stable Release、予定Release、直近Eventを返します。`--recent-limit`は既定`10`、範囲`1..100`です。
- `project-changes-since`は`--project --since`が必須で、指定したISO 8601日時より後のEventを時系列順に返します。`--offset --limit`の既定値は`0`と`50`です。

### Work item commands

- `work-item-list`、`work-item-get`は保存状態を返し、`--include-deleted`で削除済みを含めます。
- `work-item-create`は`--project --type --title --actor-type --actor-name`が必須です。
- `work-item-update`、`work-item-set-deleted`、`work-item-transition`はProject、key、revision、actorが必須です。
- `work-item-history --project --key`はWorkItemと関連記録の追記専用Audit Eventを返します。
- `item-search`は`--project --query`が必須で、任意の`--type --status --priority --owner --created-after --updated-after --offset --limit`を指定できます。Title、Description、Comment本文をFTS5検索し、削除済みWorkItemを除外します。limitの既定値は`50`、上限は`100`です。

### Collaboration commands

- `relation-add`は`--project --source-key --target-key --relation --actor-type --actor-name`が必須です。`depends_on`または`blocks`のcycleは保存したうえで`relation_cycle_detected`を返し、`relates_to`の逆順重複は拒否します。削除は`--relation-id`、一覧は`--key`を指定します。
- `comment-add`は`--project --key --body --actor-type --actor-name`で編集・削除不可のCommentを追記します。一覧は`comment-list --project --key`です。
- `task-link-add`は`--project --key --task-system --task-id --relation --actor-type --actor-name`で外部Taskを関連付けます。標準task systemは`hataori`です。削除は`--link-id`、一覧は`--key`を指定します。
- `commit-link-add`は`--project --key --commit-hash --relation --actor-type --actor-name`でCommitを関連付けます。relationは`implements`、`fixes`、`relates_to`です。削除は`--link-id`、一覧は`--key`を指定します。

### Repository commands

Provider本文の`ok:false`はMCPの`isError`がfalse／未指定でも失敗です。CLIは終了コード`1`と標準エラーの構造化JSONを返し、`error.message`に元の失敗本文を保持します。不正なRepository応答は`provider_invalid_response`、`repository_not_found`は`provider_not_found`へ変換します。構造化応答を優先しますが、構造化・テキストJSONのどちらかが明示的に失敗なら成功扱いしません。`ok`を持たない既存の一覧・レコード・バージョン応答は維持します。詳細は[応答契約](docs/adr/0006-provider-result-failures.md)を参照してください。

`repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull`は`--project`で選択したProjectのProvider結果を返します。commitには`--message`も必要です。

### Token commands

`token-issue`、`token-rotate`、`token-revoke`、`token-cleanup`は内部Service tokenを管理します。発行・rotation時のsecretは一度だけ返されます。

### Lifecycle commands

`release-create`は`--version --channel`、`release-update`は`--version --channel --expected-revision`、`release-transition`は`--version --next-status --expected-revision`を必須とします。`release-get`と`release-list`はRelease状態を取得します。

`release-add-item`は`--project --version --work-item-key --relation --actor-type --actor-name`が必須です。relationは`includes`、`fixes`、`implements`、`resolves`です。削除は`--relation-id`、一覧は`--project --version`を指定します。

`release-add-artifact`は`--project --version --name --artifact-type --platform --architecture --file-name --actor-type --actor-name`が必須です。任意で`--build-artifact-id --file-path --download-url --file-size --sha256 --signature-path --signature-url`を指定します。削除は`--artifact-id`、一覧は`--project --version`を指定します。ファイル本体は保存しません。

`release-prepare`と`release-mark-ready`は`--project --version --expected-revision --actor-type --actor-name`が必須で、`planned -> preparing -> ready`へ遷移します。

`release-publish`は同じOptionと明示承認が必要です。Provider呼び出し前に`publishing`を保存し、結果を`released`または`failed`として記録します。公開済みVersionへの再実行ではProviderを呼びません。`release-retry`は`failed -> ready`後、Providerの同一Versionを照会します。存在しなければ作成し、draftなら公開します。公開済みの場合は版／Tag、記録済みCommit、Artifactが一致するときだけ冪等な成功として整合し、不一致は相違項目を含む`provider_conflict`を返します。詳細は[ADR 0007](docs/adr/0007-provider-release-reconciliation.md)を参照してください。`release-withdraw`はProviderで公開停止後に状態を更新します。

`release-latest --project`は`released_at`基準の最新Stable Release、`release-overview --project --version`はRelease、WorkItem関連、Artifact metadataを返します。

`build`と`build-start`は`--project --actor-type --actor-name`が必須で、`--configuration`の既定値は`Release`です。Repository ProviderからSource CommitとDirty状態を取得し、Dirtyな標準BuildはBuild Provider実行前に拒否します。`build-get`、`build-list`、`build-artifacts`は永続状態を返し、`build-clean --project --actor-type --actor-name`はBuild／Artifact履歴を保持してProviderのclean操作を実行します。

`deployment-target-update`は`--project --name --mode --destination-path --expected-revision --actor-type --actor-name`が必須で、初回作成時のrevisionは`0`です。Server modeでは`--kelpie-target`も必須で、`--config-json`に資格情報を保存しません。

`deploy`と`deploy-start`は`--project --build-id --artifact-id --actor-type --actor-name`が必須で、任意の`--version`でReleaseを関連付けます。成功済みBuildの検証済みArtifactだけを受け付けます。get／status／list／retry／rollbackはDeployment状態と失敗履歴を保持し、Rollback失敗を`rollback_failed`として記録します。

## Safety Notes

commit、push、pull、build、release、deployの前にProvider対象を確認してください。`release-publish`と`deploy`は正確な対象への明示承認が必要です。token secretをログ、ソース、文書へ記録しません。

Moyai v1は各Projectの一部としてRepositoryを1つだけ管理するため、独立した`repository-register`と`repository-unregister`はありません。紐付けの利用停止はProjectをarchiveします。

# Repository branch

`branch_create`には`project`、`branch`、明示的な`source`が必要です。`source`はliteral branch名または完全な40桁commit SHAを指定します。省略時にMoyaiが`main`、`develop`、現在の`HEAD`を補完することはありません。
`tag_create`にも`project`、`tag`、明示的なliteral branch名または完全な40桁commit SHAの`source`が必要です。Providerが明示的なTag作成元に対応する場合、Moyaiは無変更で転送します。
