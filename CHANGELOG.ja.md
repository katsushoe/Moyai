# 変更履歴

[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## 1.0.5 - 2026-08-29

- 製品動作およびDB形式を変更せず、1.0.4を再パッケージしました。

## 1.0.4 - 2026-08-29

- 関係制約、revision contract、FTS5同期を備えたSQLite schema v4を追加しました。
- WorkItem relation、comment、Hataori task link、commit link、検索、Project Overview、Changes Since操作を追加しました。
- Repository Provider contractへ能力照会、branch、tag操作を追加し、Providerエラーを正規化しました。
- 対応するCLI・MCP操作、テスト、利用者向け文書を追加しました。
- 既存のv1.0.3 DBは、migration前にbackupを作成して自動更新します。

## 1.0.3 - 2026-08-28

- Project更新コマンドとMCPツールからRepository URLおよびProviderを変更できるようにしました。
- Repository URL変更時のProvider再判定と、明示指定によるroutingの上書きに対応しました。
- Moyai v1では独立したregister/unregisterコマンドではなく、各Projectの一部としてRepository紐付けを1つ管理することを明記しました。
- `1.0.2`からDB形式の変更はありません。

## 1.0.2 - 2026-08-28

- 公開文書をプロジェクトのドキュメント標準に沿って再編しました。
- 英語・日本語のREADME、設定、コマンド、MCPセットアップ文書を追加しました。
- パッケージ一覧、セキュリティ方針、変更履歴の正本を追加しました。
- `1.0.1`から製品動作とDB形式の変更はありません。

## 1.0.1 - 2026-08-28

- 公開利用者向けドキュメントを追加しました。
- `1.0.0`から製品動作とDB形式の変更はありません。

## 1.0.0 - 2026-08-28

- CLI、Streamable HTTP MCP、SQLite状態管理、Provider routing、Service認証、Lifecycle操作を備えた最初の正式版です。
