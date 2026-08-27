# Moyai

Moyaiは、プロジェクトと作業項目をSQLiteで管理し、リポジトリ操作、ビルド、リリース、デプロイをProviderへ委譲するWindows向けツールです。CLIと、stateless Streamable HTTPのMCPサーバーを提供します。

## v1.0.1

- 対応OS: Windows x64
- インストール先: `C:\Moyai`
- CLI: `C:\Moyai\bin\Moyai.Cli.exe`
- MCPサーバー: `C:\Moyai\bin\Moyai.Mcp.exe`
- データベース: 利用者が`MOYAI_DB_PATH`で指定するSQLiteファイル

[Moyai v1.0.1をダウンロード](https://github.com/katsushoe/Moyai/releases/tag/v1.0.1)

## クイックスタート

1. リリースページから`Moyai-1.0.1-x64.msi`と`.sha256`をダウンロードします。
2. SHA-256を検証してMSIを実行します。管理者権限が必要です。
3. PowerShellでデータベースの場所を設定し、CLIを実行します。

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
& 'C:\Moyai\bin\Moyai.Cli.exe' version
& 'C:\Moyai\bin\Moyai.Cli.exe' project-list
```

CLIの正常結果は標準出力へ、構造化されたエラーは標準エラーへJSONで出力されます。CLIはエラーダイアログを表示しません。

MCPサーバーを起動する場合は、ループバックの待受URLも設定します。

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
& 'C:\Moyai\bin\Moyai.Mcp.exe'
```

MCPエンドポイントは`http://127.0.0.1:43120/mcp`です。従来のSSE transportとブラウザーCORSには対応していません。

## ドキュメント

- [インストール・設定・運用ガイド](docs/USER_GUIDE.md)
- [設定リファレンス](CONFIG.md)
- [v1.0.1リリースノート](docs/releases/v1.0.1.md)
- [v1.0.0リリースノート](docs/releases/v1.0.0.md)
- [初期アーキテクチャADR](docs/adr/0001-initial-architecture.md)

## 開発

```powershell
dotnet test .\Moyai.slnx
.\scripts\Build-Installer.ps1
```

Windows配布物はWiX Toolsetベースのx64 MSIです。CIはpushとpull requestに対してビルド、テスト、MSI作成を検証します。

## ライセンス

[MIT License](LICENSE)
