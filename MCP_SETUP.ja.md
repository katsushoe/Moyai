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
3. `get_version`が`Moyai`と`1.0.7.0`を返すことを確認します。
4. `list_projects`がJSON互換の構造化データを返すことを確認します。サーバーは各プロジェクト操作前にこのToolを呼ぶようAIクライアントへ指示します。互換性維持のため`project_list`も利用できます。
5. 最初の失敗で停止し、server標準エラーを確認します。

## Troubleshooting

設定は`config/moyai.json`を使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
設定は`config/moyai.json`を使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
- ポート競合: serverとclientの両方を別の未使用ポートへ変更します。
- Toolがない: client設定更新後に再起動または再読込します。
