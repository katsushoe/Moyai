# Moyai v1 利用ガイド

## 1. インストール

1. [v1.0.1リリース](https://github.com/katsushoe/Moyai/releases/tag/v1.0.1)から次の2ファイルをダウンロードします。
   - `Moyai-1.0.1-x64.msi`
   - `Moyai-1.0.1-x64.msi.sha256`
2. PowerShellでチェックサムを確認します。

```powershell
(Get-FileHash .\Moyai-1.0.1-x64.msi -Algorithm SHA256).Hash
Get-Content .\Moyai-1.0.1-x64.msi.sha256
```

両方の値が一致することを確認してください。公開値はリリースページの`.sha256`ファイルを正本とします。

3. MSIを実行して、管理者権限の確認を許可します。

インストール先は`C:\Moyai`です。`bin`、`config`、`data`、`logs`が作成されます。MSIは設定、秘密情報、利用者データ、ログを同梱しません。アップグレードまたはアンインストール時も、空でない利用者作成ファイルは保持されます。

## 2. CLI

各PowerShellセッションでデータベースを設定します。初回のデータ操作時にSQLiteデータベースが初期化されます。

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$moyai = 'C:\Moyai\bin\Moyai.Cli.exe'
& $moyai version
```

### プロジェクト

```powershell
& $moyai project-create `
  --name Sample `
  --source-path 'C:\Workspace\Sample' `
  --repository-url 'https://github.com/example/Sample.git' `
  --build-provider local `
  --deploy-mode manual `
  --actor-type user `
  --actor-name operator

& $moyai project-list
& $moyai project-get --name Sample
```

更新系操作は、取得結果の`revision`を`--expected-revision`へ指定します。これは同時更新による上書きを防ぐための値です。

### 作業項目

```powershell
& $moyai work-item-create `
  --project Sample `
  --type Task `
  --title '動作確認' `
  --actor-type user `
  --actor-name operator

& $moyai work-item-list --project Sample
```

利用可能なCLIコマンドは次のとおりです。

- プロジェクト: `project-list`、`project-get`、`project-create`、`project-update`、`project-set-archived`
- 作業項目: `work-item-list`、`work-item-get`、`work-item-create`、`work-item-update`、`work-item-set-deleted`、`work-item-transition`
- リポジトリ: `repository-status`、`repository-diff`、`repository-commit`、`repository-push`、`repository-pull`
- 認証: `token-issue`、`token-rotate`、`token-revoke`、`token-cleanup`
- ライフサイクル: `build`、`release-create`、`release-publish`、`release-withdraw`、`deploy`

オプション名は`--kebab-case`です。変更操作には`--actor-type`と`--actor-name`が必要です。更新、アーカイブ、削除、状態遷移には`--expected-revision`も必要です。

## 3. MCPサーバー

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
& 'C:\Moyai\bin\Moyai.Mcp.exe'
```

- transport: stateless Streamable HTTP
- endpoint: `${MOYAI_MCP_URL}/mcp`
- 待受ホスト: ループバックのみ
- 提供Tool: Project・WorkItem操作、`auth_introspect`、`get_version`、Provider routing、Lifecycle操作

MCPサーバーはWindowsサービスとして自動登録されません。利用するセッションまたはプロセス管理環境から起動してください。予期しない致命的エラーではログに加えて多言語対応のエラーダイアログを表示します。

## 4. Provider routingと認証

GitHubまたはBitbucketのリポジトリ操作を委譲する場合、対応するStreamable HTTP MCP endpointを設定します。

```powershell
$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'
$env:BUCKETTIE_MCP_URL = 'http://127.0.0.1:43122/mcp'
```

プロジェクトに登録したrepository providerに従って、Moyaiが接続先を選択します。サービス間認証には`token-issue`で発行したtokenを使い、漏えいが疑われる場合は`token-rotate`または`token-revoke`を実行してください。tokenそのものをログや文書へ記録しないでください。

## 5. Build・Release・Deploy

GithubieとBuckettieはリポジトリ操作に加え、対応するLifecycle Providerとしても登録されます。別のBuildまたはDeploy Providerを使用する場合は、名前、endpoint、Tool prefixの3値をすべて設定します。

```powershell
$env:MOYAI_BUILD_PROVIDER_NAME = 'builder'
$env:MOYAI_BUILD_PROVIDER_URL = 'http://127.0.0.1:43123/mcp'
$env:MOYAI_BUILD_PROVIDER_PREFIX = 'build'
$env:MOYAI_DEPLOY_PROVIDER_NAME = 'deployer'
$env:MOYAI_DEPLOY_PROVIDER_URL = 'http://127.0.0.1:43124/mcp'
$env:MOYAI_DEPLOY_PROVIDER_PREFIX = 'deploy'
```

```powershell
& $moyai build --project Sample --actor-type user --actor-name operator
& $moyai release-create --project Sample --version 1.0.0 --notes 'Initial release' --actor-type user --actor-name operator
& $moyai release-publish --project Sample --version 1.0.0 --actor-type user --actor-name operator
& $moyai deploy --project Sample --version 1.0.0 --artifact-path 'C:\Artifacts\Sample-1.0.0.zip' --actor-type user --actor-name operator
```

実際のビルド、リリース、デプロイ方法と権限は接続先Providerに依存します。Moyaiは明示された操作をProviderへ委譲し、Lifecycle eventをSQLiteへ記録します。

## 6. エラーとトラブルシューティング

- `MOYAI_DB_PATH is required.`: `MOYAI_DB_PATH`を設定してください。
- `MOYAI_MCP_URL is required.`: MCP起動時に待受URLを設定してください。
- `MOYAI_MCP_URL must use a loopback host.`: `127.0.0.1`または`localhost`を使用してください。
- `unable to open database file`: 親ディレクトリの存在と書き込み権限を確認してください。
- Providerが見つからない: プロジェクトのprovider名と対応するendpoint環境変数を確認してください。

CLIは終了コード0で成功、1で失敗を示します。エラーJSONの`command`、`summary`、`fatal`、`error.code`、`error.message`を確認してください。
