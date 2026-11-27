# Moyai

Windows配布物ではMCP操作クライアント`moyaimcp.exe`と、互換性を維持した`moyaictl.exe`を提供します。両者は同じ実装で、現在のユーザーの稼働中MCPホストを呼び出します。トレイからホスト状態とログオン自動起動を管理できます。「MCPサーバー」メニューには状態行・「状態を確認」「起動」「停止」があり、緑・黄・赤・灰のピル型LEDで稼働中・応答なし・エラー・停止中を示します。停止には確認が必要です。[トレイ設定](CONFIG.ja.md#トレイのmcpサーバーメニュー)と[CLI仕様](COMMANDS.ja.md#mcp操作クライアント)を参照してください。

`moyaictl.exe project-create --name Sample`で名前だけのProjectを作成できます。未登録時だけ自動作成する場合は`moyaictl.exe project-ensure --name Sample`を使います。登録済みの設定は保持します。Repository・ビルド・デプロイ設定は`project-configure --name Sample --expected-revision 1`で後から関連付けます。Project作成や作業項目の管理にパスは不要です。[コマンド仕様](COMMANDS.ja.md)と[ADR 0004](docs/adr/0004-name-only-projects.md)を参照してください。

[English](README.md) | [日本語](README.ja.md)

開発候補2.0.4.0で[PR source限定公開create/status](docs/pr-source-publication.ja.md)を追加しました。部分結果を保持し、結果不明は自動再送せずstatusで照合します。導入・実相互受入は別工程です。

Moyaiは、プロジェクトと作業項目をSQLiteで管理し、リポジトリ、ビルド、リリース、デプロイ操作を設定済みProviderへ委譲するWindows向けツールです。JSON CLIとstateless Streamable HTTP MCPサーバーを提供します。

## Getting Started

Buckettie discovery schema 1の任意設定と再接続・エラーの扱いは[Providerユーザーホスト接続](docs/provider-user-host.ja.md)を参照してください。

本節のユーザーセッション方式は未公開の2.0開発版の仕様です。公開済み1.xのMSIは従来のWindows Service方式です。
2.0.3.1のインストーラーは、同梱する所有者解決処理に.NET Framework4.8を使用します。CLI・ホスト・トレイの実行環境は引き続き自己完結型です。

MSIをインストールし、次のコマンドを実行します。

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
& 'C:\Moyai\bin\moyaictl.exe' version
& 'C:\Moyai\bin\moyaictl.exe' project-list
```

MSIはログオンユーザーのprofileを初期化し、新しいprofileではトレイの自動起動を有効にします。ホストはトレイメニュー、または次のコマンドから起動できます。

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
```

endpoint・認証トークンは唯一の設定`<INSTALLFOLDER>\config\moyai.json`に保存します。DB・鍵・ログは`%LOCALAPPDATA%\Akatsukisoft\Moyai`のユーザー領域に保持します。MCPクライアントはMSIまたはCLIの`configure`コマンドから登録します。[MCPセットアップ](MCP_SETUP.ja.md)を参照してください。

## Installation

### Installer

[Moyai v1.0.7](https://github.com/katsushoe/Moyai/releases/tag/v1.0.7)から`Moyai-1.0.7-x64.msi`と`.sha256`ファイルをダウンロードします。両方のSHA-256値が一致することを確認し、管理者権限でMSIを実行してください。Moyaiは`C:\Moyai`へインストールされます。

### Binary Archive

バイナリアーカイブは配布していません。WiX Toolset MSIがサポート対象のWindowsパッケージです。

### Source Build

.NET 8を対象にできる.NET 10 SDKと、リポジトリローカルのWiX tool manifestが必要です。

```powershell
dotnet restore .\Moyai.slnx
dotnet build .\Moyai.slnx --configuration Release --no-restore
dotnet test .\Moyai.slnx --configuration Release --no-build
.\scripts\Build-Installer.ps1 -Version 1.0.7
```

## Configuration

設定は`<INSTALLFOLDER>\config\moyai.json`だけを使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは現在のユーザーの稼働ホストへ接続します。

## Usage

CLIは成功時のJSONを標準出力、構造化エラーを標準エラーへ出力し、成功時は終了コード`0`、失敗時は`1`を返します。全コマンド、オプション、戻り値、安全条件は[コマンド](COMMANDS.ja.md)を参照してください。

## Documentation

- [Provider認証・ローカル秘密情報保護仕様](docs/specifications/Provider_Authentication_Specification.md)
- [認証仕様の移管結果・未実施項目](docs/specifications/provider-authentication-transfer.md)
- [Repository Provider Contract（Gitリモートの解決規則）](docs/specifications/repository-provider-contract.md)
- [KelpieSSH Protocol v2段階Deployの設計判断（英語）](docs/adr/0009-kelpiessh-protocol-v2.md)
- [設定](CONFIG.ja.md)
- [コマンド](COMMANDS.ja.md)
- [MCPセットアップ](MCP_SETUP.ja.md)
- [パッケージ（英語）](PACKAGES.md)
- [セキュリティ（英語）](SECURITY.md)
- [変更履歴](CHANGELOG.ja.md)
- [v1完成ロードマップ](ROADMAP.ja.md)
- [v1受入基準追跡表](V1_TRACEABILITY.ja.md)
- [アーキテクチャ判断](docs/adr/0001-initial-architecture.md)
- [インストーラ管理のWindowsサービス判断（英語）](docs/adr/0002-installer-managed-windows-service.md)
- [ログオンユーザー向けMCPホストとユーザー別状態（英語）](docs/adr/0019-user-session-mcp-host.md)

## Security

MCP待受URLはループバックに限定されます。サービスtokenをソース、コマンド履歴、ログ、文書へ記録しないでください。ProviderまたはLifecycle操作を有効にする前に[セキュリティ（英語）](SECURITY.md)を確認してください。

## License

Moyaiは[MIT License](LICENSE)で提供されます。
