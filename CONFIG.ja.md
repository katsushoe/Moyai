# Moyai Configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MoyaiのCLI、MCPサーバー、Provider接続に使う環境変数の正本です。

## Configuration Directory

設定ファイルは使用しません。MSIは`C:\Moyai\config`を作成します。MCPは環境変数に加え、優先順位の高い`--MOYAI_DB_PATH`と`--MOYAI_MCP_URL`引数を受け付けます。CLIは引き続き環境変数だけを読み込みます。

## File Generation

手動起動時は利用者がプロセス環境へ設定します。MSIは64ビットレジストリ`HKLM\Software\Akatsukisoft\Moyai`の`McpUrl`値へサービス待受URLを保存し、サービス引数を設定します。秘密情報は生成しません。

## Main Settings

| 項目 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `MOYAI_DB_PATH` | CLIデータ操作・MCPで必須 | 文字列 | なし | SQLiteファイルの書き込み可能なパス |
| `MOYAI_MCP_URL` | MCPで必須 | 絶対URL | なし | ループバックホストのみ |

### `MOYAI_DB_PATH`

SQLiteデータベースのファイルパスです。省略時はデータ操作またはMCP起動が失敗します。例: `$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'`

### `MOYAI_MCP_URL`

MCP待受URLです。省略時はMCP起動が失敗します。HTTP(S)の絶対URLで、`127.0.0.1`、`localhost`、`[::1]`などのループバックホストだけを使用できます。例: `$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'`

## Profile Settings

| 項目 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `GITHUBIE_MCP_URL` | 任意 | 絶対URL | 未登録 | GithubieのループバックMCP endpoint |
| `BUCKETTIE_MCP_URL` | 任意 | 絶対URL | 未登録 | BuckettieのループバックMCP endpoint |
| `MOYAI_BUILD_PROVIDER_NAME` | 任意グループ | 文字列 | 未登録 | URL・prefixと3項目同時指定 |
| `MOYAI_BUILD_PROVIDER_URL` | 任意グループ | 絶対URL | 未登録 | ループバックMCP endpoint |
| `MOYAI_BUILD_PROVIDER_PREFIX` | 任意グループ | 文字列 | 未登録 | Tool prefix |
| `MOYAI_DEPLOY_PROVIDER_NAME` | 任意グループ | 文字列 | 未登録 | URL・prefixと3項目同時指定 |
| `MOYAI_DEPLOY_PROVIDER_URL` | 任意グループ | 絶対URL | 未登録 | ループバックMCP endpoint |
| `MOYAI_DEPLOY_PROVIDER_PREFIX` | 任意グループ | 文字列 | 未登録 | Tool prefix |

KelpieSSH経由のServer DeployではDeploy Provider名を`server`にします。MoyaiはDeployment TargetへKelpie Target名／IDだけを保存し、SSH資格情報を保存しません。Local Targetは既定でProjectの`install_path`を使用します。

組み込みBuild Providerは`csharp`、`node`、`php`です。`build_config_json`には`configuration`と`artifacts`配列を指定できます。各Artifactには`name`、`artifact_type`、Project相対の`file_path`が必要です。FileはSHA-256、Directoryは相対PathとFile Hashからなる決定的ManifestでHash化します。同名の外部Provider設定がある場合は外部Providerを優先します。

各URLには対応Providerの`/mcp` endpointを指定します。Providerグループは3項目がすべて非空の場合だけ登録されます。例: `$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'`

## Samples

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'
```

秘密値は環境変数の例や文書へ直接記録しないでください。

## Installation

WiX Toolset製のx64 MSIは、自己完結型CLI・MCPを`C:\Moyai\bin`へ配置します。生成は`.\scripts\Build-Installer.ps1`で行います。`config`、`logs`、`data`ディレクトリを作成しますが、秘密情報、DB、ログは同梱しません。利用者が作成した内容のあるディレクトリは更新・アンインストールで保持します。

### Windows起動時の自動起動

MSIがWindowsサービス`Moyai`（表示名`Moyai MCP`）を自動起動として登録し、インストール時に起動します。ログオンや手動のサービス登録は不要です。実行アカウントは`NT AUTHORITY\LocalService`で、インストール実行ユーザーやLocalSystemではありません。DBは`C:\Moyai\data\moyai.db`を使用し、既存DBを置き換えません。data・logsへLocalServiceの継承可能な変更権限を設定します。継承を無効にした既存ファイルの権限は管理者による別途確認が必要です。

初回の待受URLは`http://127.0.0.1:43120`です。初回インストール時のMSIプロパティ`MOYAI_MCP_URL`で別のループバックURLを指定できます。修復・更新・再インストールでは保存済みのレジストリ値が優先されます。変更する場合は管理者権限で`McpUrl`を更新し、MSIを修復してサービス引数へ反映します。保存済みURLはアンインストールでも保持します。引数や設定へ秘密情報を入れないでください。

手動起動から移行する前に、同じポートを使用する既存MCPプロセスを停止してください。ポート競合や不正な設定はサービス起動失敗となり、インストール成功と見なしてはいけません。MSIは実行ファイルの更新・削除前にサービスを停止し、アンインストール時にサービス登録を削除します。既存DBと利用者作成の設定・ログは削除しません。メジャーアップグレード試験には新しい製品バージョンが必要です。開発用MSIは公開リリースではありません。

Provider環境変数はログインユーザーのプロセスだけでなく、サービスが参照できる環境へ設定してください。マシン環境変数の変更をサービス管理プロセスへ反映するにはWindows再起動が必要な場合があります。LocalServiceは利用者のファイルアクセス権・PATH・資格情報・ネットワークドライブ割り当てを引き継ぎません。必要なプロジェクトだけに権限を付与するか外部Providerを使用し、回避策としてLocalSystemへ昇格しないでください。

サービスの警告・エラーはWindows Applicationイベントログの既存`Application`ソースへ出力します。致命的な起動エラーには`Moyai MCP`の接頭辞を付けます。サービスではエラーダイアログを表示しません。手動起動では従来のコンソールログと個別の設定を使用します。

```powershell
Get-Service -Name Moyai
Stop-Service -Name Moyai
Start-Service -Name Moyai
```

サービス操作には適切なWindows権限が必要です。リリース前に隔離したWindows試験環境で、インストール・更新・アンインストール・再起動後の自動起動・既存DB保持を検証してください。
