# Moyai

[English](README.md) | [日本語](README.ja.md)

Moyaiは、プロジェクトと作業項目をSQLiteで管理し、リポジトリ、ビルド、リリース、デプロイ操作を設定済みProviderへ委譲するWindows向けツールです。JSON CLIとstateless Streamable HTTP MCPサーバーを提供します。

## Getting Started

MSIをインストールし、次のコマンドを実行します。

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
& 'C:\Moyai\bin\Moyai.Cli.exe' version
& 'C:\Moyai\bin\Moyai.Cli.exe' project-list
```

MCPサーバーを起動する場合は次を実行します。

```powershell
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
& 'C:\Moyai\bin\Moyai.Mcp.exe'
```

MCPクライアントには`http://127.0.0.1:43120/mcp`をStreamable HTTPサーバーとして登録します。完全なクライアント設定は[MCPセットアップ](MCP_SETUP.ja.md)を参照してください。

## Installation

### Installer

[Moyai v1.0.4](https://github.com/katsushoe/Moyai/releases/tag/v1.0.4)から`Moyai-1.0.4-x64.msi`と`.sha256`ファイルをダウンロードします。両方のSHA-256値が一致することを確認し、管理者権限でMSIを実行してください。Moyaiは`C:\Moyai`へインストールされます。

### Binary Archive

バイナリアーカイブは配布していません。WiX Toolset MSIがサポート対象のWindowsパッケージです。

### Source Build

.NET 8を対象にできる.NET 10 SDKと、リポジトリローカルのWiX tool manifestが必要です。

```powershell
dotnet restore .\Moyai.slnx
dotnet build .\Moyai.slnx --configuration Release --no-restore
dotnet test .\Moyai.slnx --configuration Release --no-build
.\scripts\Build-Installer.ps1 -Version 1.0.4
```

## Configuration

Moyaiは環境変数から設定を読み込みます。データ操作には`MOYAI_DB_PATH`、MCPサーバーには`MOYAI_DB_PATH`と`MOYAI_MCP_URL`が必要です。型、既定値、制約、例は[設定](CONFIG.ja.md)を参照してください。

## Usage

CLIは成功時のJSONを標準出力、構造化エラーを標準エラーへ出力し、成功時は終了コード`0`、失敗時は`1`を返します。全コマンド、オプション、戻り値、安全条件は[コマンド](COMMANDS.ja.md)を参照してください。

## Documentation

- [設定](CONFIG.ja.md)
- [コマンド](COMMANDS.ja.md)
- [MCPセットアップ](MCP_SETUP.ja.md)
- [パッケージ（英語）](PACKAGES.md)
- [セキュリティ（英語）](SECURITY.md)
- [変更履歴](CHANGELOG.ja.md)
- [v1完成ロードマップ](ROADMAP.ja.md)
- [アーキテクチャ判断](docs/adr/0001-initial-architecture.md)

## Security

MCP待受URLはループバックに限定されます。サービスtokenをソース、コマンド履歴、ログ、文書へ記録しないでください。ProviderまたはLifecycle操作を有効にする前に[セキュリティ（英語）](SECURITY.md)を確認してください。

## License

Moyaiは[MIT License](LICENSE)で提供されます。
