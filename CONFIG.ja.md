# Moyai Configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MoyaiのCLI、MCPサーバー、Provider接続に使う環境変数の正本です。

## Configuration Directory

設定ファイルは使用しません。MSIは`C:\Moyai\config`を作成しますが、v1.0.3は環境変数だけを読み込みます。

## File Generation

設定は利用者がプロセス環境へ設定します。Moyai、ビルド、MSIは設定や秘密情報を生成しません。

## Main Settings

| 項目 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `MOYAI_DB_PATH` | CLIデータ操作・MCPで必須 | 文字列 | なし | SQLiteファイルの書き込み可能なパス |
| `MOYAI_MCP_URL` | MCPで必須 | 絶対URL | なし | ループバックホストのみ |

### `MOYAI_DB_PATH`

SQLiteデータベースのファイルパスです。省略時はデータ操作またはMCP起動が失敗します。例: `$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'`

### `MOYAI_MCP_URL`

MCP待受URLです。省略時はMCP起動が失敗します。`127.0.0.1`または`localhost`だけを使用できます。例: `$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'`

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

各URLには対応Providerの`/mcp` endpointを指定します。Providerグループは3項目がすべて非空の場合だけ登録されます。例: `$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'`

## Samples

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'
```

秘密値は環境変数の例や文書へ直接記録しないでください。
