# Moyai設定

## トレイの設定と起動時更新

Windowsの通知領域で「Moyai MCP」を右クリックし、「設定」を開きます。起動時の自動更新は既定でオンで、次回のトレイ起動から反映されます。Providerリクエストのタイムアウトは1～3600秒で、MCPホストの再起動後に反映されます。両方とも正規の `moyai.json` に保存します。

MSIで導入したトレイは、MCPホストが停止している場合に公式GitHub Releasesの新しい安定版を確認します。x64 MSIのサイズ、SHA-256、Akatsukisoftの有効なAuthenticode署名を確認してから更新します。Windowsの管理者承認が表示されることがあります。更新試行後はトレイが再起動し、結果は `startup-update.log` に記録されます。

## GithubieのPR公開ポリシー設定

Moyai2.0.4.2は、既存のProject認証・Settings scope/能力を使い、`pr_publication_source_branches`と`pull_request_routes`の明示更新を転送します。新しい権限を自動追加せず、登録値・承認・永続化はGithubieが管理します。両項目はGithubie専用で大文字小文字を区別する配列全置換、経路は`source_branch`/`destination_branch`の組です。省略は保持、空配列は解除、null・不正値・重複は拒否し、旧Providerが省略した取得値は補完しません。公開元リストと完全一致経路の両方、保護元ブランチ拒否・既定拒否はGithubieが検証します。通常commit用リストやpush許可、seed JSONから新リストを推測しません。実更新前に現在値を読み、保持項目を含む全配列をレビューしてください。既存Provider DBの更新は実設定APIから行い、seed編集では代用できません。Consumer修正の導入だけで通常設定/認証対応付けは変更しません。[コマンド](COMMANDS.ja.md)と[ADR0012](docs/adr/0012-repository-settings-consumer.md)を参照してください。

## ホスティング保護の権限

開発候補2.0.3.0はProviderのbranch_protection_read/write明示能力を要求します。所有者方式では署名鍵不要、署名方式の更新・削除には専用provider.githubie.branch_protection.writeが必要です。既存grant/設定は自動変更せず、操作監査はschema8を使用します。Providerのprotected_branchesとGitHub classic保護/rulesetは別です。[ブランチ保護](docs/branch-protection.ja.md)を参照してください。

## 読込済み設定の診断

設定変更は明示的なホスト再起動後に反映します。[実行時診断](docs/runtime-diagnostics.ja.md)で読込パスと内容の差を確認できます。自動再読込は行いません。


## PR読取・マージの認証（開発版2.0.1.0）

新しいトップレベル設定やDB移行はありません。Githubie署名方式は既存Providerの文字列配列辞書assertionToolScopesへ厳密な `"github_pr_get": ["repository.read"]` と `"github_pr_merge": ["provider.githubie.pull_request.merge"]` が必要です。既定の未設定は拒否し、PR作成/write/push権限でマージを代用しません。登録Project UUID・正規repository・対象toolへ束縛し、実機の権限や設定は自動変更しません。

既存のGithubie windows_owner方式ではIssuer・署名鍵・旧tokenを追加しません。owner protocol1、検証済みdiscovery、全HTTPのsession/generationヘッダー、owner operationsとtools/list両方の対象get/merge、厳密なdata.pr_get/data.pr_merge真偽値を確認します（単独読取はpr_getのみ）。不整合時は別認証やstatic接続へ退避しません。能力広告だけでは実PR受入を意味しません。Buckettieのget/merge経路は無効です。ProviderのGitHub資格・allowlist・経路/保護規則・承認は維持します。[ADR0018](docs/adr/0018-provider-pull-request-merge.md)を参照してください。

## Buckettie Diffのremote指定（開発中）

既存Projectの`gitRemoteName`が空白以外で、Providerが`remote_resolution`のversion1/mode `repository_url`を表明した場合だけ、同じ値をBuckettie Diffへ渡します。未設定/空白では能力確認を省略し、未対応/不正/取得失敗時は互換性のためremoteを省略します。省略した場合は指定名の検証済みとは扱いません。Githubie DiffはProviderの検証条件確認まで対象外です。新設定・既定値・DB移行・scope追加はなく、`repository.read`を使います。稼働設定やremoteは変更しません。[ADR0016](docs/adr/0016-buckettie-diff-remote.md)を参照してください。

## Pull Request作成の認可（開発中）

契約・対応表明の準備と運用者承認後、各Providerの`assertionToolScopes`へ正確な`"github_pr_create": ["provider.githubie.pull_request.write"]`、または`"bitbucket_pr_create": ["provider.buckettie.pr_create"]`を追加します。辞書値は文字列配列で、既定は項目なし・拒否です。既存read/commit/push権限では有効になりません。Githubieは`data.pr_create=true`、Buckettieは`data.operations.pr_create=true`とintegration_mode=moyaiを要求します。この操作の明示的な対応表明は担当側との合意・導入が必要で、Tool存在だけでは作成要求を送りません。最上位設定追加、稼働scopeやProvider許可経路の自動変更はありません。[ADR0015](docs/adr/0015-provider-pull-request-create.md)を参照してください。

## ローカルブランチ作成の認可（開発中）

Githubieが`data.local_branch_create=true`を返すことを確認した後、管理者は該当Providerの`assertionToolScopes`辞書へ`github_local_branch_create`を追加し、値に正確な文字列配列`["repository.branch.write"]`を指定できます。設定例：`"github_local_branch_create": ["repository.branch.write"]`。既定は項目なしで拒否します。汎用writeやリモートブランチ用の認可では有効になりません。最上位の新規キーはなく、実装時に稼働scope・鍵・アカウント・DBは変更しません。Buckettieの専用操作との対応付けは未確定のため未対応です。[ADR0014](docs/adr/0014-provider-local-branch-create.md)を参照してください。

## branch切替の認可（開発版1.3.12.0）

Providerがbranch_switch対応を明示し、管理者が有効化を承認した後、各assertionToolScopesへgithub_branch_switch: [repository.branch.write]またはbuckettie_branch_switch: [repository.branch.write]を設定します。Buckettieの設定prefixはbitbucketのままで、この操作だけbuckettie_branch_switchへ解決します。BuckettieはMoyai連携モードが必要です。実装時に稼働scope・署名鍵・DB・サービスアカウントは変更しません。[ADR0013](docs/adr/0013-provider-branch-switch.md)を参照してください。

設定Consumerは合意済みのGithubie／Buckettie経路に対応します。真偽値の対応能力はGithubieが`data.repository_settings_read/update`、Buckettieが`data.operations.repository_settings_read/update`です。Buckettieは読取・更新ともMoyai連携モードを必要とします。対応版の準備と所有者の許可後、該当Providerの`assertionToolScopes`へ次の項目を追加します。既定は項目なしで拒否し、Upgradeで既存設定を保持します。commit/push scopeやlegacy tokenで代替せず、実装だけで稼働権限を追加しません。Providerが保存と人の承認を管理します。[ADR0012](docs/adr/0012-repository-settings-consumer.md)を参照してください。

```json
{
  "bitbucket_repository_settings_get": ["repository.read"],
  "bitbucket_repository_settings_update": ["repository.settings.write"]
}
```

Githubieは同じscopeでTool名を`github_repository_settings_get`／`github_repository_settings_update`にします。この断片は最上位ではなく`assertionToolScopes`内の設定例です。管理者が確認して追加するもので、自動付与はしません.

Githubie限定の`local_commit_branches`拡張も同じTool・scope・対応能力を使い、既定設定や権限を追加しません。指定した配列だけを置換し、Providerの承認を必要とします。Buckettieは共通8項目のままです。Settings Toolのscope対応付けがない場合はHTTP前、Providerが対応能力を返さない/falseの場合は能力照会後に`provider_capability_missing`となります。接続できるだけでは設定不足は解消しません。対象runtimeの設定と実行版を確認し、承認したscope変更は対象runtimeだけの再起動で反映します。

Repository認証の既定はES256 Assertionです。未構成時は操作を拒否します。[Provider認証設定・鍵初期化・移行手順](docs/specifications/provider-authentication-operations.md)を参照してください。静的Repository Tokenは最大7日の明示的な移行期間に限ります。

## Providerユーザーホスト検出（開発中）

Providerの`connectionMode`は既定`static`で、既存のloopback `endpoint`を保持します。任意指定の`buckettie_user_host_v1`では`name=buckettie`、`toolPrefix=bitbucket`、承認された絶対パス`runtimeDirectory`、正規の`expectedMcpPath`（既定`/mcp`）を指定し、`endpoint`を省略します。動的検出失敗時に固定URLへ退避しません。所有者/session、private ACL、プロセス世代、版付きrecordを検証し、全HTTPへ一時的な所有者資格を付けます。署名対象はES256も必要です。読取の再接続は最大1回、変更操作は自動再送しません。[設定・エラー・運用](docs/provider-user-host.ja.md)と[ADR 0020](docs/adr/0020-provider-user-host-consumer.md)を参照してください。通常設定の変更やBuckettie再有効化は自動で行いません。

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## 同一ユーザーのProvider認証

Buckettieの`connectionMode: "buckettie_user_host_v1"`接続では、`authenticationMode: "windows_owner"`を明示して所有ユーザー認証を選択します。既定値は`"assertion"`です。Buckettie2.0.1.0以降、契約3、所有ユーザープロトコル1の明示的な能力通知を必須とし、`runtimeDirectory`には承認済みの非公開ランタイムを指定します。静的URL接続やGithubieの所有ユーザーモードは、対応する接続契約の実装まで拒否します。

所有ユーザーモードのRepository・Release呼出しは、全HTTP要求に`X-Buckettie-Session`を付け、Authorization・Issuer署名サービス・旧Service Tokenを使用しません。能力不足や不正なdiscoveryで別の認証方式へ切り替えません。Provider側のRepository/branch制限と本人承認、ルーティング用のProject対応付けを保持し、結果不明の変更操作は自動再送しません。現在はソース実装の段階で、インストール済み設定は未変更です。隔離連携受入とバックアップ付きの切替が必要です。[ADR0023](docs/adr/0023-same-user-provider-trust.md)を参照してください。

`<INSTALLFOLDER>`はMSIで選択したインストール先です。無人導入では`INSTALLFOLDER`で指定し、修復・更新でも保持します。実行時は64bit HKLMの製品登録から解決し、未登録の配布物では実行ファイルの配置を基準にします。ドライブや配置先は固定しません。これはインストール情報であり、別の設定ファイルではありません。

MCPクライアント登録は対象ユーザープロファイルを指定し、他の設定を保持します。MSIプロパティは `MOYAI_CODEX`、`MOYAI_CLAUDE`（`1`で選択）、`MOYAI_CLIENT_PROFILE`（既存プロファイルの絶対パス）です。所有権・アンインストール・復元は[MCP設定手順](MCP_SETUP.ja.md)を参照してください。

## 設定ファイル

MCPホスト・CLI・トレイは`<INSTALLFOLDER>\config\moyai.json`だけを読み込みます。`--config`は正規化後に同じファイルを指す場合だけ受理し、別パスは拒否します。不在・不正な設定は明示的に失敗し、ユーザープロファイルや環境変数へフォールバックしません。起動・再起動ではプロセスを変更する前に設定を検証します。CLIは設定上のホストへ接続し、DBを直接開きません。設定変更後はホストを再起動します。[ADR0022](docs/adr/0022-single-operational-configuration.md)を参照してください。

```json
{
  "databasePath": "<user-profile>\\data\\moyai.db",
  "serverUrl": "http://127.0.0.1:<persisted-user-port>",
  "apiToken": "<config-init-generated-64-hex-secret>",
  "requestTimeoutSeconds": 60,
  "providers": []
}
```

`apiToken`は必須の64桁16進値です。`config-init`は未作成の正規設定に暗号学的乱数32バイトを生成し、既存ファイルは上書きしません。不在・不正な値では起動を拒否します。`get_version`を含む全HTTP要求に`Authorization: Bearer <apiToken>`が必要です。CLIは自動送信し、`configure codex|claude`は現在のユーザーのprofileだけにendpointと認証ヘッダーを設定します。設定・クライアントファイル・復元情報を非公開にしてください。更新時はホストを停止し、新しい乱数へ変更し、両クライアントを再登録後、ホストとクライアントを再起動します。この受信認証はProvider/Service Tokenとは別です。

`databasePath`の相対パスはJSONファイルのディレクトリ基準です。`serverUrl`は資格情報を含まないHTTP(S)のループバックURLです。CLIは末尾へ`/mcp`を付けます。`requestTimeoutSeconds`はCLI操作のタイムアウト秒数（1〜3600）です。ファイル不在、不正JSON、未知の設定項目、不正URLは明示的なエラーとなり、環境変数へ切り替えません。

## Provider

各項目は`name`、`endpoint`、`toolPrefix`と任意の`repository`（既定false）で構成します。URLはHTTP(S)ループバック、名前は重複不可です。既存のGithubieルーティング識別子は`githubbie`、prefixは`github`、repositoryはtrueです。Assertionでは正規Provider ID／Audience `githubie`へ解決します。Buckettieは`buckettie`／`bitbucket`／trueです。組み込みBuild Providerは`csharp`、`node`、`php`で、同名の外部Provider設定を優先します。KelpieSSH配備は`server`名または正規名`kelpiessh`を使用し、Protocol v2 Assertionで認証します。内部`server`経路もAudience `kelpiessh`で署名し、Moyai Service Tokenを取得・送信しません。TokenはサービスDBで管理し、JSONへ保存しません。

```json
[
  {"name":"githubbie","endpoint":"http://127.0.0.1:43121/mcp","toolPrefix":"github","repository":true},
  {"name":"server","endpoint":"http://127.0.0.1:45432/mcp","toolPrefix":"kelpie","repository":false}
]
```

Server配備Targetの`kelpieTarget`にはKelpieSSHの不変Target IDを設定し、配備先Pathを指定します。配備ArtifactはSHA-256付きの単一Fileが必要です。Protocol v2の固定Scopeは[Provider認証の実装・運用Contract](docs/specifications/provider-authentication-operations.md)を参照してください。

Projectの`build_config_json`では`configuration`と`artifacts`配列（`name`、`artifact_type`、Project相対の`file_path`）を指定します。配備先は既存のProject設定を使用します。

## ProjectのProvider Repository対応付け

Projectの `providerRepositoryId` はサービスJSONではなく `projects.provider_repository_id` に保存します。Moyai Project名とは別に、既存のProvider登録IDを指定します。現在のProject revisionを指定して `project_provider_repository_set` または `moyaictl project-provider-repository-set` で登録・更新し、`project_get`／Project一覧で照会します。[ADR 0011](docs/adr/0011-provider-repository-id.md)を参照してください。

未設定の場合は互換性のためProviderの `repository` 引数へ従来どおりProject名を渡します。IDの大文字小文字を保持し、1～128文字のASCII英数字・アンダースコア・ハイフン・ドットだけを使い、先頭は英数字とします。`clearMapping=true` で明示解除できます。IDの省略／nullだけでは解除しません。通常のProject更新・configure・rename・archiveは対応付けを保持します。

schema v7でnullable列を追加し、Project UUID・名前・revision・関連WorkItemを変更しません。既存の移行バックアップとトランザクション規則を使います。Repository操作とRepositoryを使うRelease操作は登録IDを使い、AssertionのUUID・正規化Repository URL・audience・scopeを維持します。対応付けの保存だけでProvider登録や別Repositoryへの認可を行わず、誤ったProvider対応付けは委譲時に拒否します。

## インストール・移行

MSIはログオン中ユーザーの権限で`profile-init`を実行します。明示的な初期化時だけ、未作成の正規設定にトークンとloopback portを生成します。DB・ログ・署名秘密鍵はユーザー領域に保持します。設定ディレクトリは設定所有ユーザー・SYSTEM・Administratorsだけに許可し、修復・更新でも所有ユーザーSIDを保持します。運用対象は設定所有ユーザー1名です。

既存ユーザーホストからの切替では、両設定を非公開でバックアップし、稼働設定のendpoint・トークン・絶対DBパス・署名identityを保って正規パスへ明示的に移します。確認成功後に旧profile設定を退避します。旧Service設定をそのまま有効化したり、旧DB・鍵を自動移行したりしません。通常のホスト起動は設定ファイルを新規作成しません。

開発MSIを承認なく稼働環境へ適用しません。アンインストールでは正規設定・ユーザー別DB・ログと旧サービスデータを保持します。

## Windows起動時の自動起動

新規導入ではトレイアプリをWindowsログオン時に自動起動します。ホスト状態とログオン自動起動は独立しています。制御pipeは現在のWindowsユーザーだけに許可します。DB・鍵scope・ログはユーザー領域に保持し、運用設定と保存済みendpointは設定所有ユーザーに属します。他ユーザー用の別設定は作成しません。

管理コマンドは`host start`、`host stop`、`host restart`、`host status`と`autostart enable|disable|status`です。自動起動を無効にしても稼働hostは停止せず、host停止でも自動起動は無効になりません。業務CLIはユーザー別endpointへ接続できない場合エラーとなり、別ユーザーprofileやSQLiteへfallbackしません。

ホスト出力はユーザー別LocalAppDataのlogsへ保存します。Uninstallでは製品所有の自動起動entryとバイナリだけを削除し、ユーザー別データと旧サービスデータは保持します。リリース前にユーザー分離、複数ユーザー同時起動、Install・Upgrade・rollback・Uninstall・自動起動・データ保持をWindowsで検証します。

## トレイのMCPサーバーメニュー

「MCPサーバー」は既存メニューの末尾の区切り線の上に表示します。サブメニューは、クリック不可の状態行、区切り線、「状態を確認」「起動」「停止」の順です。親項目と状態行に16×16pxのピル型LEDを表示します。初回確認前は「状態: 未確認」で灰色です。

| 状態 | 判定 | LEDの基本色 |
| --- | --- | --- |
| 稼働中 | GET `/health` がHTTP 2xxでJSONの`status`が`ok`。`version`があれば版も表示 | 緑 RGB(40,190,70) |
| 応答なし | Health無応答で、インストール先の`bin/host/Moyai.Mcp.exe`とフルパスが一致するプロセスが存在 | 黄 RGB(240,170,30) |
| エラー | HTTPエラー・不正なJSON・`status`が`ok`以外、または操作失敗・期限切れ | 赤 RGB(215,50,45) |
| 停止中 | 対象プロセスもHealth応答もない | 灰 RGB(150,150,150) |

Health URLは正規設定`serverUrl`のoriginに`/health`を付けます。設定が読めない・JSON不正・HTTP(S) loopback以外の場合は`http://127.0.0.1:43120/health`を使います。タイムアウトは2秒で、リダイレクトは追いません。`GET /health`だけは認証なしで状態と版を返し、設定・秘密情報は返しません。MCPの認証は変更しません（[ADR 0027](docs/adr/0027-tray-mcp-server-health.md)）。

メニューを開くたびに再確認し、「状態を確認」は結果をバルーン通知します。稼働中の「起動」と停止中の「停止」は無効です。起動は既存のユーザー別ホスト実行ファイルを直接起動し、最大20秒、確認間隔0.5秒でHealthを待ちます。停止は既定「いいえ」の確認ダイアログで、再起動までMoyaiを使うCodex・Claude Codeのすべてのセッションが切断されることを通知します。同じ実行ファイルパスのホストだけを制御pipeで正常停止し、最大10秒後に残る対象プロセスを強制終了します。強制終了の確認期限は追加2秒です。

起動・停止中は状態行を「状態: 起動中...」「状態: 停止中...」にし、二重実行を無効にします。操作結果は成功が情報、失敗が警告のバルーンです。実機の停止→起動確認はユーザーご本人の同意を得てから行います。

