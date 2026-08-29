# Moyai Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

`Moyai.Cli.exe`の公開コマンド契約です。MCP Toolは同じ操作名をunderscore形式で提供します。

## Command Groups

| Group | Commands | 用途 |
| :--- | :--- | :--- |
| Project | `project-list`、`project-get`、`project-create`、`project-update`、`project-set-archived`、`project-overview`、`project-changes-since` | Project状態・集約表示 |
| Work item | `work-item-list`、`work-item-get`、`work-item-create`、`work-item-update`、`work-item-set-deleted`、`work-item-transition`、`work-item-history`、`item-search` | 作業管理・履歴・FTS5検索 |
| Collaboration | `relation-add/remove/list`、`comment-add/list`、`task-link-add/remove/list`、`commit-link-add/remove/list` | WorkItem連携記録 |
| Repository | `repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull` | Provider経由Git操作 |
| Token | `token-issue`、`token-rotate`、`token-revoke`、`token-cleanup` | Service認証 |
| Release | `release-create/get/list/update/transition`、`release-add/remove/list-items`、`release-add/remove/list-artifacts`、`release-prepare/mark-ready/publish/retry/withdraw`、`release-latest/overview` | Release状態、内容、公開 |
| Lifecycle | `build`、`deploy` | Provider経由Lifecycle操作 |

## Common Options

Optionは`--kebab-case`です。変更操作には`--actor-type`と`--actor-name`、同時実行制御対象には`--expected-revision`も必要です。成功時は標準出力へJSONと終了コード`0`、失敗時は標準エラーへ`command`、`summary`、`ok`、`fatal`、`error`を含むJSONと終了コード`1`を返します。

## Commands

Project名の検索、重複登録判定、変更対象の解決にはOrdinalな大文字小文字非区別比較を使用します。`project-update`で新しい名前を明示しない限り、登録時の表記を正本として保持します。

各コマンドの構文・処理・戻り値は[英語正本](COMMANDS.md)の同一順序の個別項目を正とします。Command、Option、制約、安全Note、Sample、Linkは英語正本と一致します。

### Project commands

- `project-list`: `project-list [--include-archived]`。既定ではarchive済みを除外します。
- `project-get`: `project-get --name <name>`。保存済みProjectを返します。
- `project-create`: `--name --source-path --repository-url --build-provider --deploy-mode --actor-type --actor-name`が必須で、Projectと1つのRepository紐付けを同時に作成します。
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

`repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull`は`--project`で選択したProjectのProvider結果を返します。commitには`--message`も必要です。

### Token commands

`token-issue`、`token-rotate`、`token-revoke`、`token-cleanup`は内部Service tokenを管理します。発行・rotation時のsecretは一度だけ返されます。

### Lifecycle commands

`release-create`は`--version --channel`、`release-update`は`--version --channel --expected-revision`、`release-transition`は`--version --next-status --expected-revision`を必須とします。`release-get`と`release-list`はRelease状態を取得します。

`release-add-item`は`--project --version --work-item-key --relation --actor-type --actor-name`が必須です。relationは`includes`、`fixes`、`implements`、`resolves`です。削除は`--relation-id`、一覧は`--project --version`を指定します。

`release-add-artifact`は`--project --version --name --artifact-type --platform --architecture --file-name --actor-type --actor-name`が必須です。任意で`--build-artifact-id --file-path --download-url --file-size --sha256 --signature-path --signature-url`を指定します。削除は`--artifact-id`、一覧は`--project --version`を指定します。ファイル本体は保存しません。

`release-prepare`と`release-mark-ready`は`--project --version --expected-revision --actor-type --actor-name`が必須で、`planned -> preparing -> ready`へ遷移します。

`release-publish`は同じOptionと明示承認が必要です。Provider呼び出し前に`publishing`を保存し、結果を`released`または`failed`として記録します。公開済みVersionへの再実行ではProviderを呼びません。`release-retry`は`failed -> ready`後に再公開し、`release-withdraw`はProviderで公開停止後に状態を更新します。

`release-latest --project`は`released_at`基準の最新Stable Release、`release-overview --project --version`はRelease、WorkItem関連、Artifact metadataを返します。

`build`、`release-publish`、`release-withdraw`、`deploy`はProject、actorと操作固有のversion、artifact pathをProviderへ渡します。

## Safety Notes

commit、push、pull、build、release、deployの前にProvider対象を確認してください。`release-publish`と`deploy`は正確な対象への明示承認が必要です。token secretをログ、ソース、文書へ記録しません。

Moyai v1は各Projectの一部としてRepositoryを1つだけ管理するため、独立した`repository-register`と`repository-unregister`はありません。紐付けの利用停止はProjectをarchiveします。
