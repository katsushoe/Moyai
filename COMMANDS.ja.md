# Moyai Commands

## MCP操作クライアント

Windowsビルド・配布物には、`moyaictl.exe`と同じCLIアセンブリ・依存ファイルを使う実行名`moyaimcp.exe`を含めます。`moyaimcp --config "<path>" repository-status --project <登録名>`で稼働中サービスを呼び出します。`--config`はコマンド後にも指定でき、省略時は実行ファイルのディレクトリを基準に`../config/moyai.json`を使います。設定の`serverUrl`がサービス接続先です。`commands`はサービスのTool schemaを取得します。`--help`と`--version`は設定を読み込まずローカルのヘルプ・CLI版を返し、`version`はサービスの版を取得します。

両実行名でコマンド、Project選択、承認、監査、出力、終了コードを維持します。成功は0、失敗は1で構造化エラーを標準エラーへ出力します。Provider操作では既存の本人承認を使います。サービスをホストするのは`Moyai.Mcp.exe`で、クライアントからの起動は明示的な`service start`で行います。別名exeは配布されたCLI依存ファイルと同じ場所に配置してください。[ADR0017](docs/adr/0017-mcp-command-client-alias.md)を参照してください。

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

全業務コマンド（`version`を含む）はサービスへ接続します。`--config <path>`でJSON設定を指定します。`commands`は稼働サービスのコマンド一覧と入力schemaを返します。`help`はサービス未起動でも使用できます。

管理コマンドは`service start`、`service stop`、`service pause`、`service resume`、`service register`、`service unregister`、`service status`です。登録は同じbin内のMCP実行ファイルと設定を使用し、Auto／LocalServiceと必要なディレクトリ権限を設定します。登録解除前は停止が必要です。`config-init`は設定不在時だけ初期JSONを作成します。管理コマンドはWindows SCMを使用し、業務処理やDBへ直接アクセスしません。

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
