# Moyai設定

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

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MCPクライアント登録は対象ユーザープロファイルを指定し、他の設定を保持します。MSIプロパティは `MOYAI_CODEX`、`MOYAI_CLAUDE`（`1`で選択）、`MOYAI_CLIENT_PROFILE`（既存プロファイルの絶対パス）です。所有権・アンインストール・復元は[MCP設定手順](MCP_SETUP.ja.md)を参照してください。

## 設定ファイル

サービスとCLIはインストール先の`config/moyai.json`を読み込みます。既定位置は実行ファイルの`bin`ディレクトリの親から解決します。`--config <path>`で別ファイルを指定できます。環境変数は設定として読み込みません。CLIはサービスに接続し、DBを直接開きません。設定変更後はサービスを再起動します。

```json
{
  "databasePath": "../data/moyai.db",
  "serverUrl": "http://127.0.0.1:43120",
  "requestTimeoutSeconds": 60,
  "providers": []
}
```

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

MSIはサービス起動前にCLIの`config-init`を実行します。JSONがない場合だけ初期設定を生成し、以前のレジストリ`McpUrl`があれば取り込みます。既存JSONは検証して保持します。管理者がJSONを編集し、`service stop`、`service start`で反映します。既存JSONを旧MSIプロパティやレジストリで上書きしません。

旧環境変数方式から移行する場合、更新前にDBパス・待受URL・Provider設定をJSONへ明示的に移してください。開発MSIを承認なく稼働環境へ適用しません。アンインストールでは配布バイナリとサービス登録を削除し、利用者設定・DB・ログは保持します。

## Windows起動時の自動起動

サービス名は`Moyai`、自動起動、実行アカウントはLocalServiceです。設定はLocalServiceが読み取り、管理者が更新します。data・logsにはLocalServiceの変更権限を設定します。LocalServiceは利用者のファイル権限やネットワークドライブ割り当てを引き継ぎません。

管理CLIは`service register`（登録）、`service start`（起動）、`service status`（状態）、`service pause`（一時停止）、`service resume`（再開）、`service stop`（停止）、`service unregister`（登録解除）です。変更にはWindows権限が必要です。解除には停止状態が必要で、DB・設定は削除しません。一時停止中は新しいHTTP要求を503で拒否し、受付済み処理は完了できます。再開は受付を戻し、停止はホストを終了します。業務CLIは接続できない場合エラーとなり、SQLiteへ直接アクセスしません。

サービスログはWindows Applicationイベントログへ出力します。非対話のエラーではダイアログを表示しません。隔離試験では別DB・別ポートのJSONを用意し、`Moyai.Mcp.exe --config <path>`で起動できます。リリース前には管理者権限のある隔離WindowsでInstall・Upgrade・一時停止・再開・Uninstall・再起動・DB保持を検証します。
