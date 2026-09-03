# MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Values and Placeholders

| 値 | 取得方法 | 例 | 変更条件 |
| :--- | :--- | :--- | :--- |
| DBパス | 書き込み可能なローカルパスを選択 | `C:\Moyai\data\moyai.db` | データ移動時 |
| サーバーURL | 未使用のループバックポートを選択 | `http://127.0.0.1:43120` | ポート競合時 |

例は完全な値です。山括弧付きplaceholderを入力しないでください。

## Prerequisites

x64 MSIを`C:\Moyai`へインストールします。MSIインストールとサービス管理には適切なWindows権限が必要です。

## Authentication and Configuration

設定は`config/moyai.json`を使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。

## Start the Server

```powershell
& 'C:\Moyai\bin\moyaictl.exe' service start
```

設定URLで待受開始したログが合格条件です。

## Register Clients

## インストーラによるクライアント登録

MSIの画面でCodex／Claude Codeと、既存ユーザープロファイルの絶対パスを指定します。対象クライアントを終了してから実行してください。無人インストールでは `MOYAI_CODEX=1`、`MOYAI_CLAUDE=1`、`MOYAI_CLIENT_PROFILE="%USERPROFILE%"` で選択します。未選択時はサービスだけをインストールします。選択は修復・更新・アンインストールで引き継ぎ、更新時は接続登録を保持します。

インストーラと同じ管理CLIを手動でも使用できます。

```powershell
& 'C:\Moyai\bin\moyaictl.exe' configure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' configure claude --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure claude --profile $env:USERPROFILE
```

`--profile`省略時はCLI実行ユーザーを対象とします。登録先はCodexが`<profile>/.codex/config.toml`、Claude Codeが`<profile>/.claude.json`のユーザースコープです。クライアントの独自設定ディレクトリは手動登録してください。クライアント未導入でも事前登録できます。`configure`は`--config`（既定は製品設定）のURLを使い、`unconfigure`はサービス停止中・製品設定不在でも動作します。

既存の同一設定は所有権を取得せず保持し、異なる同名設定は上書きしません。解除・アンインストールではMoyaiが作成し、その後編集されていない項目だけを削除します。他の設定値は保持しますが書式は正規化される場合があります。クライアントの起動やDB操作は行いません。

所有権情報と一時復元情報は`<profile>/.moyai`に保存します。復元情報には既存設定の秘密情報が含まれる場合があるため公開しないでください。MSI失敗時は元のファイルへ戻し、成功時は復元情報を削除します。中断した処理はクライアントを閉じて状況を確認後、`moyaictl client-transaction codex --phase rollback --profile <profile>`（または`claude`）で戻せます。`--phase commit`は適用済み状態を残して復元情報だけを破棄します。別のユーザーは各ユーザーの権限で登録・解除し、設定後はクライアントを再起動してください。[設計判断](docs/adr/0005-client-registration.md)を参照してください。



### Codex

次のStreamable HTTP serverをユーザーMCP設定へ追加し、Codexを再読込します。

```toml
[mcp_servers.moyai]
url = "http://127.0.0.1:43120/mcp"
```

Server名は`moyai`、transportはStreamable HTTP、`get_version`は認証不要、scopeはuserです。設定ファイルの正確な場所はインストール済みCodex版の文書に従います。

### Claude Code

次の完全なserver entryをユーザーMCP設定へ統合し、Claude Codeを再起動します。

```json
{
  "mcpServers": {
    "moyai": {
      "type": "http",
      "url": "http://127.0.0.1:43120/mcp"
    }
  }
}
```

Server名は`moyai`、transportはStreamable HTTP、`get_version`は認証不要です。設定場所はインストール済みClaude Code版の文書に従います。

## Multiple Workspaces

分離境界ごとにserver processとDBを1つ使い、各processへ異なるループバックポートとDBパスを割り当てます。

## Verify the Connection

1. endpointの待受を確認します。
2. clientのTool検出を確認します。
3. `get_version`が`Moyai`とインストール済みのバージョンを返すことを確認します。
4. `list_projects`がJSON互換の構造化データを返すことを確認します。サーバーは各プロジェクト操作前にこのToolを呼ぶようAIクライアントへ指示します。互換性維持のため`project_list`も利用できます。
5. 最初の失敗で停止し、server標準エラーを確認します。

## Troubleshooting

設定は`config/moyai.json`を使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
設定は`config/moyai.json`を使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
- ポート競合: serverとclientの両方を別の未使用ポートへ変更します。
- Toolがない: client設定更新後に再起動または再読込します。
