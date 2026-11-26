# MCP Setup

## PR source限定公開Tool（開発候補2.0.4.0）

専用`pull_request_publish_create`と`pull_request_publish_status`をホスト接続CLIと同じschemaで提供します。createはoperation UUID/draft明示、両capability、所有者認証またはpush/PR書込の両scopeを要求します。部分結果/結果不明はstatusで読取照合し、変更を自動再送しません。[操作ガイド](docs/pr-source-publication.ja.md)を参照してください。既存PR作成はbranchを公開しません。

## ホスティング側ブランチ保護Tool

開発候補2.0.3.0のbranch_protection_get/update/deleteはCLIと同じ契約です。getは読取専用、update/deleteはProviderのDesktop承認とMoyai永続監査が必要です。明示能力・認証権限を確認し、結果不明は再送しません。[ブランチ保護](docs/branch-protection.ja.md)を参照してください。

## ホスト診断

MCP `runtime_diagnostics` は読取専用で、既存の所有者認証が必要です。安全な出力とトレイの明示的再起動は[診断](docs/runtime-diagnostics.ja.md)を参照してください。


## PR状態ツール（開発版2.0.1.0）

既存Moyai接続へpull_request_get（読取専用）とpull_request_merge（破壊的/open-world）を追加し、登録Projectから対象を解決します。新endpointやクライアント資格は追加しません。マージ必須入力はproject・pullRequestId・strategy・expectedSourceCommit、messageは省略可能です。サービス接続CLIは実MCPスキーマでハイフン形式の対応コマンドを処理します。実行前に[コマンド](COMMANDS.ja.md)と[認証](CONFIG.ja.md)を確認し、PR・方式・40桁source SHAへの本人承認を得てください。unknown/pendingは別getで照合し、マージを自動再送しません。ローカル候補の対象はGithubieだけで、Provider導入・権限変更・実受入完了を意味しません。

稼働中のMCPサービスへCLIから要求する場合は`moyaimcp.exe <コマンド>`を使います。`moyaictl.exe`と実装・配布依存ファイルを共有し、Providerの本人承認と構造化エラーも同じです。サービスのホストは`Moyai.Mcp.exe`です。Windows MSIには両CLI実行名を含めます。`--help`／`--version`はローカル表示、`commands`／`version`は設定上のサービスへ問い合わせます。[コマンド仕様](COMMANDS.ja.md#mcp操作クライアント)を参照してください。

開発中のGithubie Settings拡張は、既存`repository_settings_update`の`changes.local_commit_branches`を使います。MCP/CLIの新Toolやscopeは増やしません。指定配列で置換し、明示空配列も保持します。GETではProviderが省略した追加項目を出力しません。Buckettieは共通8項目の契約を維持します。Providerの人による承認と稼働設定のSettings Tool/scope対応付けが必要で、ソースのpublishだけでは有効化しません。

Buckettie Diffのremote転送は既存`repository_diff`と`repository.read` Assertionを使います。Client引数や権限は増やさず、登録Projectと[CONFIG](CONFIG.ja.md)の設定/対応能力を確認します。明示remoteの拒否でoriginへ退避しません。Githubie向け転送は対象外で、publishだけで実機導入・受入済みとは扱いません。[ADR0016](docs/adr/0016-buckettie-diff-remote.md)を参照してください。

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Values and Placeholders

Repository設定はGithubie／Buckettie共通の`repository_settings_get/update`で操作します。登録ProjectからProviderとRepositoryを決めます。有効化前に[CONFIG](CONFIG.ja.md)の対応能力、Assertion信頼、読取・更新専用scopeを確認してください。Buckettieは読取・更新ともMoyai連携モードが必要です。更新にはProvider側の人の承認が必要で、結果不明なら自動再送せず別途読み取ります。publishだけで稼働設定や権限は変わりません。

| 値 | 取得方法 | 例 | 変更条件 |
| :--- | :--- | :--- | :--- |
| DBパス | 書き込み可能なローカルパスを選択 | `C:\Moyai\data\moyai.db` | データ移動時 |
| サーバーURL | 未使用のループバックポートを選択 | `http://127.0.0.1:43120` | ポート競合時 |

例は完全な値です。山括弧付きplaceholderを入力しないでください。

## Prerequisites

2.0.3.1 MSIの所有者解決処理には.NET Framework4.8が必要です。アプリ本体の実行環境は同梱します。設定所有者を解決できない場合は、既存製品の削除前に失敗します。

x64 MSIでインストール先を選択します。無人導入では`INSTALLFOLDER`で指定でき、修復・更新でも選択済みの場所を保持します。以下の例の`C:\Moyai`は実際のインストール先へ読み替えてください。MSIインストールには適切なWindows権限が必要です。MCPホストとトレイはログオンユーザーとして動き、Windows Serviceは使用しません。

## Authentication and Configuration

2.0では全HTTP要求にユーザー別`apiToken`が必要です。正規設定の明示的な新規初期化時に生成し、`configure codex`／`configure claude`がendpointとBearerヘッダーを登録します。対象profileは現在のユーザーに限定します。認証なし・別profileのトークンはHTTP 401で拒否します。クライアント設定と復元情報は秘密情報として保護してください。

運用設定は`<INSTALLFOLDER>\config\moyai.json`だけです。別パスの`--config`は拒否します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは現在のユーザーの稼働ホストへ接続します。

## Start the Server

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
```

新規profileではトレイがWindowsログオン時にホストを起動します。トレイメニューでホスト起動・停止・状態と、自動起動設定を個別に操作します。CLIでは`host start|stop|restart|status`と`autostart enable|disable|status`を使えます。

## Register Clients

## インストーラによるクライアント登録

MSIの画面でCodex／Claude Codeと、既存ユーザープロファイルの絶対パスを指定できます。対象クライアントを終了してから実行してください。無人インストールでは `MOYAI_CODEX=1`、`MOYAI_CLAUDE=1`、`MOYAI_CLIENT_PROFILE="%USERPROFILE%"` でクライアントを選択します。クライアント選択は任意です。選択は修復・更新で引き継ぎ、更新時は接続登録を保持します。

インストーラと同じ管理CLIを手動でも使用できます。

```powershell
& 'C:\Moyai\bin\moyaictl.exe' configure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' configure claude --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure claude --profile $env:USERPROFILE
```

`--profile`省略時はCLI実行ユーザーを対象とします。登録先はCodexが`<profile>/.codex/config.toml`、Claude Codeが`<profile>/.claude.json`のユーザースコープです。クライアントの独自設定ディレクトリは手動登録してください。クライアント未導入でも事前登録できます。`configure`は`<INSTALLFOLDER>\config\moyai.json`のURLとトークンを使い、`unconfigure`はサービス停止中・製品設定不在でも動作します。

既存の同一設定は所有権を取得せず保持し、異なる同名設定は上書きしません。解除・アンインストールではMoyaiが作成し、その後編集されていない項目だけを削除します。他の設定値は保持しますが書式は正規化される場合があります。クライアントの起動やDB操作は行いません。

所有権情報と一時復元情報は`<profile>/.moyai`に保存します。復元情報には既存設定の秘密情報が含まれる場合があるため公開しないでください。MSI失敗時は元のファイルへ戻し、成功時は復元情報を削除します。中断した処理はクライアントを閉じて状況を確認後、`moyaictl client-transaction codex --phase rollback --profile <profile>`（または`claude`）で戻せます。`--phase commit`は適用済み状態を残して復元情報だけを破棄します。別のユーザーは各ユーザーの権限で登録・解除し、設定後はクライアントを再起動してください。[設計判断](docs/adr/0005-client-registration.md)を参照してください。



### Codex

次のStreamable HTTP serverをユーザーMCP設定へ追加し、Codexを再読込します。

```toml
[mcp_servers.moyai]
url = "http://127.0.0.1:43120/mcp"
http_headers = { Authorization = "Bearer <profile-apiToken>" }
```

Server名は`moyai`、transportはStreamable HTTP、scopeはuserです。例のportとtokenを正規設定の値へ置換するかCLI登録を使います。`get_version`を含む全Toolに認証が必要です。[Codex設定仕様](https://developers.openai.com/codex/config-reference)を参照してください。

### Claude Code

次の完全なserver entryをユーザーMCP設定へ統合し、Claude Codeを再起動します。

```json
{
  "mcpServers": {
    "moyai": {
      "type": "http",
      "url": "http://127.0.0.1:43120/mcp",
      "headers": { "Authorization": "Bearer <profile-apiToken>" }
    }
  }
}
```

Server名は`moyai`、transportはStreamable HTTPで、全要求に認証が必要です。例のportとtokenはprofileの値へ置換します。[Claude Code MCP仕様](https://code.claude.com/docs/en/mcp)を参照してください。

## Multiple Workspaces

分離境界ごとにserver processとDBを1つ使い、各processへ異なるループバックポートとDBパスを割り当てます。

## Verify the Connection

1. endpointの待受を確認します。
2. clientのTool検出を確認します。
3. `get_version`が`Moyai`とインストール済みのバージョンを返すことを確認します。
4. `list_projects`がJSON互換の構造化データを返すことを確認します。サーバーは各プロジェクト操作前にこのToolを呼ぶようAIクライアントへ指示します。互換性維持のため`project_list`も利用できます。
5. 最初の失敗で停止し、server標準エラーを確認します。

## Troubleshooting

設定は`<INSTALLFOLDER>\config\moyai.json`だけを使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
設定は`<INSTALLFOLDER>\config\moyai.json`だけを使用します。[設定仕様](CONFIG.ja.md)を参照してください。CLIの業務コマンドは稼働サービスへ接続します。
- ポート競合: serverとclientの両方を別の未使用ポートへ変更します。
- Toolがない: client設定更新後に再起動または再読込します。
