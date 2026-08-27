# Moyai Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

`Moyai.Cli.exe`の公開コマンド契約です。MCP Toolは同じ操作名をunderscore形式で提供します。

## Command Groups

| Group | Commands | 用途 |
| :--- | :--- | :--- |
| Project | `project-list`、`project-get`、`project-create`、`project-update`、`project-set-archived` | Project状態 |
| Work item | `work-item-list`、`work-item-get`、`work-item-create`、`work-item-update`、`work-item-set-deleted`、`work-item-transition` | 作業管理 |
| Repository | `repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull` | Provider経由Git操作 |
| Token | `token-issue`、`token-rotate`、`token-revoke`、`token-cleanup` | Service認証 |
| Lifecycle | `build`、`release-create`、`release-publish`、`release-withdraw`、`deploy` | Lifecycle操作 |

## Common Options

Optionは`--kebab-case`です。変更操作には`--actor-type`と`--actor-name`、同時実行制御対象には`--expected-revision`も必要です。成功時は標準出力へJSONと終了コード`0`、失敗時は標準エラーへ`command`、`summary`、`ok`、`fatal`、`error`を含むJSONと終了コード`1`を返します。

## Commands

各コマンドの構文・処理・戻り値は[英語正本](COMMANDS.md)の同一順序の個別項目を正とします。Command、Option、制約、安全Note、Sample、Linkは英語正本と一致します。

### Project commands

- `project-list`: `project-list [--include-archived]`。既定ではarchive済みを除外します。
- `project-get`: `project-get --name <name>`。保存済みProjectを返します。
- `project-create`: `--name --source-path --repository-url --build-provider --deploy-mode --actor-type --actor-name`が必須です。
- `project-update`: `--current-name --name --git-remote-name --expected-revision --actor-type --actor-name`が必須です。
- `project-set-archived`: `--name --expected-revision --archived --actor-type --actor-name`が必須です。

### Work item commands

- `work-item-list`、`work-item-get`は保存状態を返し、`--include-deleted`で削除済みを含めます。
- `work-item-create`は`--project --type --title --actor-type --actor-name`が必須です。
- `work-item-update`、`work-item-set-deleted`、`work-item-transition`はProject、key、revision、actorが必須です。

### Repository commands

`repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull`は`--project`で選択したProjectのProvider結果を返します。commitには`--message`も必要です。

### Token commands

`token-issue`、`token-rotate`、`token-revoke`、`token-cleanup`は内部Service tokenを管理します。発行・rotation時のsecretは一度だけ返されます。

### Lifecycle commands

`build`、`release-create`、`release-publish`、`release-withdraw`、`deploy`はProject、actorと操作固有のversion、notes、artifact pathをProviderへ渡します。

## Safety Notes

commit、push、pull、build、release、deployの前にProvider対象を確認してください。`release-publish`と`deploy`は正確な対象への明示承認が必要です。token secretをログ、ソース、文書へ記録しません。
