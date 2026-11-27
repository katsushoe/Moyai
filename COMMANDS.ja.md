# Moyai Commands

## PR公開設定（開発候補2.0.4.2）

`repository-settings-get`は、Githubieが返す`pr_publication_source_branches`と`pull_request_routes`も表示します。旧Providerでの省略は省略のまま、明示的な空配列は`[]`のまま返します。同じホスト接続型CLI/MCPの設定更新を使います。

```powershell
moyaictl repository-settings-update --project Example --changes '{"pr_publication_source_branches":["docs/topic"],"pull_request_routes":[{"source_branch":"develop","destination_branch":"main"},{"source_branch":"docs/topic","destination_branch":"main"}]}'
```

先に現在値を読み、保持する全要素を含めてください。指定配列は全置換、省略は保持、`[]`は解除、null・型/値不正・重複は拒否します。大文字小文字と順序を保持します。公開元リストは通常commit許可と独立し、旧値を自動流用しません。Githubieは公開元リストと完全一致PR経路の両方を必要とし、保護元ブランチや空/未指定の許可を拒否します。Buckettieへ両項目は送信しません。既存の認証/能力確認とProviderの人による承認が必要で、例は実設定変更の許可を意味しません。[ADR0012](docs/adr/0012-repository-settings-consumer.md)を参照してください。

## PR source限定公開（開発候補2.0.4.0）

専用の[pull-request-publish-create/status](docs/pr-source-publication.ja.md)を使います。createには`--draft true|false`と元のoperation UUIDを明示し、結果不明ならstatusで読取照合します。既存pull-request-createはsource branchを公開しません。

## ホスティング側ブランチ保護（開発候補2.0.3.0）

`branch-protection-get`、`branch-protection-update`、`branch-protection-delete` に登録済み `--project` とexactな `--branch` を指定します。更新には `--edit <JSON>`（snake_case、省略/nullは保持）、新規作成を許可する場合は `--create-if-missing true` を明示します。変更はProviderのDesktop承認とMoyai監査を必須とし、結果不明時は自動再送しません。[ブランチ保護](docs/branch-protection.ja.md)を参照してください。

## 実行時診断（開発候補2.0.2.0）

CLI `moyaictl runtime-diagnostics [--project <登録名>]` またはMCP `runtime_diagnostics` を使います。[診断とトレイ操作](docs/runtime-diagnostics.ja.md)を参照してください。


## PR読取・マージ（開発版2.0.1.0）

MCP `pull_request_get(project, pullRequestId)` / CLI `moyaictl pull-request-get --project Example --pull-request-id 42` はProviderの実測PR状態を返します。projectはMoyai登録名、IDは正のInt64ですがGithubieでは送信前に2147483647以下へ制限します。今回の実装対象はGithubieで、Buckettieの状態経路は無効です。

MCP `pull_request_merge(project, pullRequestId, strategy, expectedSourceCommit, message?)` / CLI `moyaictl pull-request-merge --project Example --pull-request-id 42 --strategy merge --expected-source-commit <本人承認済み40桁SHA>` を追加します。実行前に対象PR・方式・SHAへの本人承認が必要です。方式は明示必須でrepository_default（Providerへnull）、merge、squash、rebaseに対応し、共通fast_forwardはGithubieでは拒否します。SHAの英字大小と非空白messageは保持し、省略/nullのmessageはProvider既定、空白だけは拒否します。

明示能力と認証を確認し、事前読取の実source SHAが一致した場合だけマージを要求します。draft・draft情報欠落・closed未マージは送信前に拒否し、既マージは実測結果の観測として再マージしません。結果はok/operation/project/provider/pullRequest/errorを返し、PR情報に実測sourceCommit/mergeCommitとnullable draftを含みます。errorはcode/outcome/retryable/suggestedAction/correlationId/providerDetailを保持します。pending/unknownは成功ではなくretryable=falseです。別の認証付きgetで照合し、署名期限切れを含めマージは自動再送しません。CLIの業務失敗はexit1です。ローカルpublishだけでは旧実機版や設定は更新されません。[ADR0018](docs/adr/0018-provider-pull-request-merge.md)を参照してください。

## MCP操作クライアント

Windowsビルド・配布物には、`moyaictl.exe`と同じCLIアセンブリ・依存ファイルを使う実行名`moyaimcp.exe`を含めます。`moyaimcp repository-status --project <登録名>`で稼働中サービスを呼び出します。設定は`<INSTALLFOLDER>\config\moyai.json`だけを使用します。コマンド前後の`--config`は正規化後に同じパスを指す場合だけ受理し、別ファイルは拒否します。設定の`serverUrl`がサービス接続先です。`commands`はサービスのTool schemaを取得します。`--help`と`--version`は設定を読み込まずローカルのヘルプ・CLI版を返し、`version`はサービスの版を取得します。

両実行名で業務コマンド、Project選択、承認、監査、出力、終了コードを維持します。`Moyai.Mcp.exe`はログオンユーザーのsessionで稼働します。`host start|stop|restart|status`は現在のユーザーに限定したnamed pipe経由でプロセスを管理し、`autostart enable|disable|status`はログオン自動起動を別に管理します。一方の設定はもう一方を変更しません。[ADR0017](docs/adr/0017-mcp-command-client-alias.md)と[ADR0019](docs/adr/0019-user-session-mcp-host.md)を参照してください。

## Diffのremote指定（開発中）

MCP `repository_diff(project)`／CLI `moyaictl repository-diff --project <name>`の入力は変えません。Buckettieは対応能力を確認できた場合に、Project設定の空白以外のgitRemoteNameをそのまま受け取ります。呼出側のremote/資格情報引数は追加しません。未対応/能力取得失敗時は既存の省略条件を維持し、Providerのremote不存在/曖昧/URL不一致はoriginへ退避せず保持します。日本語/BOM/改行/複数hunkは変更せず、比較先はローカルHEADのままです。Githubie向け転送は有効にしません。fetch/push/設定変更はなく、実受入・導入は別条件です。[ADR0016](docs/adr/0016-buckettie-diff-remote.md)を参照してください。

## Pull Request作成（開発中）

MCP `pull_request_create(project, sourceBranch, destinationBranch, title, description, draft)`、CLI例は`moyaictl pull-request-create --project Example --source-branch docs/proposal --destination-branch main --title "Docs update" --description "" --draft true`です。全6引数必須です。ブランチはliteral名、タイトルは空白以外を含む255文字以内で制御文字不可、説明は空文字可・32768文字以内でNUL不可、draftは明示boolです。develop/main/HEADを補完しません。

Githubieは`github_pr_create`（`source_branch/destination_branch`）、Buckettieは`bitbucket_pr_create`（`sourceBranch/destinationBranch`）へ変換します。明示PR capability、専用PR Assertion scope、対応付け済みRepository/Project、Providerが許可した経路が必要です。BuckettieはMoyai連携モードを要求します。汎用read/commit/push権限とlegacy tokenでは作成できません。経路設定変更・push・merge・代替経路の実行は行いません。

成功は`ok/operation/project/provider/pullRequest`を返します。共通データ例：`{"id":7,"title":"Docs update","sourceBranch":"docs/proposal","destinationBranch":"main","draft":null,"state":"open"}`。url/descriptionはnull可です。Githubieが返さないdraftは明示nullとし、Buckettieは返された値を保持します。失敗はcode/outcome/suggestedAction/providerDetailで、例は`project_not_found`、`pull_request_invalid`、`provider_capability_missing`、`auth_scope_denied`、`pull_request_route_not_allowed`です。CLI業務失敗はexit1。結果不明時はProviderのPRを別途読取確認してから再作成を判断し、自動再送しません。capability合意・稼働版導入・実外部受入は別条件です。[ADR0015](docs/adr/0015-provider-pull-request-create.md)を参照してください。

## ローカルブランチ作成（開発中）

MCP `branch_create_local(project, branch, source)`、CLI `moyaictl branch-create-local --project <name> --branch <new-local-branch> --source <local-branch|tag|full-SHA>`で、切替せず作成します。全引数必須で、`project`は登録済みProject名、`branch`は新規ローカル名、`source`はローカルブランチ名・タグ名・完全40桁16進commit SHAです。例：`moyaictl branch-create-local --project Example --branch docs/proposal --source v1.0.1.0`。暗黙HEAD補完、参照式、fetch/push、リモートブランチ作成、設定更新は行いません。

Githubieの明示的な`data.local_branch_create=true`と、正確な`github_local_branch_create: [repository.branch.write]`のAssertion認可が必要です。Buckettieとlegacy経路は未対応で、リモート用`branch_create`へ代替しません。allowlist・policy/承認・clean状態・conflictなし・source存在・作成先未存在はProviderが検証します。変わるのは新規ローカル参照だけで、HEAD/現在ブランチ・upstream・commit・remote refs・index/worktreeは変更しません。

結果は`RepositoryProviderResult`（`ok`、`operation`、`output`、`errorCode`、`errorMessage`、`resultUnknown`）です。成功例は`ok=true`、`operation=branch_create_local`で、JSON文字列`output`にGithubieの`data.name/source/current_branch/head_sha`を保持します。失敗例は委譲前の`provider_capability_missing`、Provider由来の`working_tree_dirty`・`unresolved_conflicts`・`branch_source_not_found`・`local_branch_already_exists`・`invalid_ref`です。CLI失敗はexit1です。送信後の結果不明ではProviderが提供するローカル参照の読取で別途確認し、再送しません。ソース実装と稼働版への導入・実Provider受入は別条件です。[ADR0014](docs/adr/0014-provider-local-branch-create.md)を参照してください。

## ローカルbranch切替（開発版1.3.12.0）

MCP `branch_switch(project, branch)`、CLI `moyaictl branch-switch --project <name> --branch <existing-local-branch>`で登録Providerへ委譲します。Project識別情報・対応付け済みRepository ID・URLは保持し、`gitDefaultBranch`も更新しません。状態変更Toolで、明示capabilityと`github_branch_switch`/`buckettie_branch_switch`への`repository.branch.write`が必要です。BuckettieはMoyai連携モードを要求し、legacyは拒否します。

fetch・remote checkout・branch作成・source/remote引数の追加は行いません。local branchの存在・参照形式・policy・dirty拒否はProviderが検証し、同じbranchの無変更成功もProvider規則に従います。既存RepositoryProviderResult.outputにProvider JSONを保持し、Githubieはdata.previous_branch/current_branch、Buckettieはdata.branch_switch内（switchedも含む）に切替前後が入ります。working_tree_dirty/branch_not_found/invalid_ref/Buckettieのbranch_switch_invalid_refとpolicyコードを保持します。resultUnknown=trueなら別のrepository statusで確認してから再要求し、自動再送しません。稼働版・scope導入と使い捨て実Provider受入は別条件です。[ADR0013](docs/adr/0013-provider-branch-switch.md)を参照してください。

## Repository設定（開発版1.3.11.0）

MCPは`repository_settings_get(project)`／`repository_settings_update(project, changes)`、CLIは`moyaictl repository-settings-get --project <name>`／`moyaictl repository-settings-update --project <name> --changes '<JSON object>'`です。必要に応じて`--config`を指定します。作者更新例は`{"commit_author_name":"Test Author","commit_author_email":"test@example.invalid"}`。CLIはサービスへ接続しDBを直接操作しません。

Githubie限定の開発中拡張として`local_commit_branches`の配列置換に対応します。例: `moyaictl repository-settings-update --project Example --changes '{"local_commit_branches":["codex/docs-main-v1010","develop"]}'`。現在値を読み取り、保持する既存名を含めて指定します。省略は保持、`[]`は明示解除、null・型不正・空白要素は拒否します。大文字小文字や順序を変更せず、push許可やPR経路から推測しません。GETはGithubieが返した場合だけ追加項目を表示し、省略と空配列を区別します。Buckettieへの当該更新は拒否し、共通8項目は維持します。既存のProject-bound更新scopeとProviderの人による承認が必要で、例は更新許可や実機設定変更を意味しません。[ADR0012](docs/adr/0012-repository-settings-consumer.md)の拡張節を参照してください。

合意済みGithubie／Buckettie経路を実装しています。稼働設定への導入と実結合の完了は別条件です。正確な対応能力とAssertion scopeを必要とし、Buckettieは読取でもMoyai連携モードを確認します。読取は共通の登録8項目と上記Githubie限定の追加項目で、作者未設定はnullです。更新の省略は保持、falseは明示値、配列は置換です。空changes・未知項目・null・型不正・不正作者ペアを拒否します。Buckettie固有のpull_request_routesは対象8項目に含めません。policy検証と人の承認はProvider側です。型付き結果はok/operation/project/provider/settings/changedFields/approved/errorで、成功時のproviderはgithubieまたはbuckettieを示し、生応答は返しません。認証・能力・設定エラーと安全な相関IDを保持し、更新成功にはapproved=trueが必須です。更新結果不明はerror.outcome=unknownとし、別の読取で確認してから再更新します。自動更新再試行、Git/稼働設定変更、直接/legacy代替は行いません。[ADR0012](docs/adr/0012-repository-settings-consumer.md)を参照してください.

Provider認証管理: `assertion-key-prepare`、`assertion-key-get --kid <id>`、`assertion-key-activate --kid <id> --trust-distribution-confirmed true [--overlap-seconds 86400]`、`assertion-key-revoke --kid <id>`、`assertion-protector-rotate`、`assertion-secret-rewrap`。すべてサービス経由で公開情報だけを返します。[運用Contract](docs/specifications/provider-authentication-operations.md)に状態遷移・エラー・移行・Trust配布条件を記載しています。

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

ローカル接続管理は `configure codex|claude [--profile <path>] [--config <path>]`、`unconfigure codex|claude [--profile <path>]`、復元は `client-transaction codex|claude --phase rollback|commit [--profile <path>]` です。`--transaction`はMSI用の復元情報を残します。業務MCPではなくインストール管理コマンドです。[MCP設定手順](MCP_SETUP.ja.md)を参照してください。

`moyaictl.exe`の公開コマンド契約です。MCP Toolは同じ操作名をunderscore形式で提供します。MCPはAI向けの`project_list`別名として`list_projects`も公開し、対応するCLI操作は`project-list`です。

## サービス接続と管理

全業務コマンド（`version`を含む）はサービスへ接続します。JSON設定は`<INSTALLFOLDER>\config\moyai.json`に固定し、`--config`で別ファイルを選択できません。`commands`は稼働サービスのコマンド一覧と入力schemaを返します。`help`はサービス未起動でも使用できます。

管理コマンドは`host start|stop|restart|status`、`autostart enable|disable|status`です。`profile-init`は未作成の正規設定とユーザー領域のDB/logディレクトリを初期化し、新規profileで自動起動を有効にします。`profile-uninstall`は製品所有の自動起動entryだけを削除し、ユーザーデータを保持します。旧`service`コマンド群はサポートしません。管理コマンドは業務処理やDBへ直接アクセスしません。

`host start`はreadyを最大60秒待ちます。接続後の制御応答には独立した10秒の期限があります。タイムアウトや応答前の切断だけでは停止済みと判定できません。管理操作を繰り返す前に`host status`を確認してください。`host restart`は停止応答を返したプロセスの終了を最大15秒待ち、終了を確認できない場合は次のプロセスを起動せず中断します。[ADR0019](docs/adr/0019-user-session-mcp-host.md)を参照してください。

## Command Groups

| Group | Commands | 用途 |
| :--- | :--- | :--- |
| Project | `project-list`、`project-get`、`project-create`、`project-ensure`、`project-configure`、`project-rename`、`project-provider-repository-set`、`project-update`、`project-set-archived`、`project-overview`、`project-changes-since` | Project状態・集約表示 |
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

#### `project-provider-repository-set`

目的：Moyai Project名を変えず、Provider登録IDを設定・更新・明示解除します。MCP Toolは `project_provider_repository_set` です。

構文：`moyaictl project-provider-repository-set --project <name> --expected-revision <revision> (--provider-repository-id <id> | --clear-mapping) [--actor-type <type>] [--actor-name <name>]`。

`project` は既存Project、`expectedRevision` は現在のrevisionです。`providerRepositoryId` はCONFIG記載の形式に従う登録ID、`clearMapping` の既定値はfalseです。ID指定か解除のどちらか一方だけを指定します。任意actorの既定は `client`／`unspecified` で、監査上の記録であり認証ではありません。

例：`moyaictl project-provider-repository-set --project Neon_SDK --expected-revision 1 --provider-repository-id neonsdk`。実際のrevisionは事前に `project-get` で確認し、既存Projectで1と仮定しません。解除例：`moyaictl project-provider-repository-set --project Neon_SDK --expected-revision 2 --clear-mapping`。

サービスが対応付けと `project_provider_repository_set` 監査Eventを同じトランザクションで保存し、revisionを増やします。UUID・名前・他設定・archive状態・関連データは保持します。結果は保存済みProjectを返し、`providerRepositoryId` と新revisionを含みます。明示解除はnullを保存し、従来のProject名へのfallbackを戻します。結果項目の例：`{"name":"Neon_SDK","providerRepositoryId":"neonsdk","revision":2}`（実際の応答には他のProject項目も含みます）。

対象なし、古いrevision、不正ID、解除なしのID省略、IDと解除の同時指定では保存せず失敗します（CLI終了コード1）。CLIはサービスに接続し、SQLiteを直接変更しません。Provider登録の作成、資格情報変更、UUID／Repository URL検証の回避は行いません。この設定操作の確認に実commit／pushは不要です。

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
# Repository stash連携

MCP Tool `repository_stash_push` / `repository_stash_list` / `repository_stash_apply` / `repository_stash_drop`は設定済みRepository Providerへ委譲します。`repository_stash_push`の`paths`はRepository相対pathの配列で、省略時は全変更を退避します。適用・削除ではlist/pushが返す完全なstash object IDを指定します。Assertion scopeは一覧が`repository.read`、変更操作が`repository.stash.write`です。

CLI例: `moyaictl repository-stash-push --project Buckettie --message "split changes" --paths "src/a.cs,docs/readme.md"`、`moyaictl repository-stash-list --project Buckettie`、`moyaictl repository-stash-apply --project Buckettie --stash-id <40桁ID>`、`moyaictl repository-stash-drop --project Buckettie --stash-id <40桁ID>`。CLIの`paths`はカンマ区切りです。ファイル名にカンマが含まれる場合はMCPを使います。

`repository_commit_selected`は`expected_head`とunified diffの`patch`をProviderへ渡し、Provider Capabilityで対応が明示される場合だけ実行します。未選択の作業ツリー変更は残り、staged変更がある場合はProvider側で拒否します。
