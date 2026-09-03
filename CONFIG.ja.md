# Moyai設定

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MCPクライアント登録は対象ユーザープロファイルを指定し、他の設定を保持します。MSIプロパティは `MOYAI_CODEX`、`MOYAI_CLAUDE`（`1`で選択）、`MOYAI_CLIENT_PROFILE`（既存プロファイルの絶対パス）です。所有権・アンインストール・復元は[MCP設定手順](MCP_SETUP.ja.md)を参照してください。

## 設定ファイル

サービスとCLIはインストール先の`config/moyai.json`を読み込みます。既定位置は実行ファイルの`bin`ディレクトリの親から解決します。`--config <path>`で別ファイルを指定できます。環境変数は設定として読み込みません。CLIはサービスに接続し、DBを直接開きません。設定変更後はサービスを再起動します。

```json
{
  "databasePath": "../data/moyai.db",
  "serverUrl": "http://127.0.0.1:43120",
  "requestTimeoutSeconds": 60,
  "providers": []
}
```

`databasePath`の相対パスはJSONファイルのディレクトリ基準です。`serverUrl`は資格情報を含まないHTTP(S)のループバックURLです。CLIは末尾へ`/mcp`を付けます。`requestTimeoutSeconds`はCLI操作のタイムアウト秒数（1〜3600）です。ファイル不在、不正JSON、未知の設定項目、不正URLは明示的なエラーとなり、環境変数へ切り替えません。

## Provider

各項目は`name`、`endpoint`、`toolPrefix`と任意の`repository`（既定false）で構成します。URLはHTTP(S)ループバック、名前は重複不可です。既存のGithubieルーティング識別子は`githubbie`、prefixは`github`、repositoryはtrueです。Buckettieは`buckettie`／`bitbucket`／trueです。組み込みBuild Providerは`csharp`、`node`、`php`で、同名の外部Provider設定を優先します。KelpieSSH配備は`server`名を使用します。TokenはサービスDBで管理し、JSONへ保存しません。

```json
{"name":"githubbie","endpoint":"http://127.0.0.1:43121/mcp","toolPrefix":"github","repository":true}
```

Projectの`build_config_json`では`configuration`と`artifacts`配列（`name`、`artifact_type`、Project相対の`file_path`）を指定します。配備先は既存のProject設定を使用します。

## インストール・移行

MSIはサービス起動前にCLIの`config-init`を実行します。JSONがない場合だけ初期設定を生成し、以前のレジストリ`McpUrl`があれば取り込みます。既存JSONは検証して保持します。管理者がJSONを編集し、`service stop`、`service start`で反映します。既存JSONを旧MSIプロパティやレジストリで上書きしません。

旧環境変数方式から移行する場合、更新前にDBパス・待受URL・Provider設定をJSONへ明示的に移してください。開発MSIを承認なく稼働環境へ適用しません。アンインストールでは配布バイナリとサービス登録を削除し、利用者設定・DB・ログは保持します。

## Windows起動時の自動起動

サービス名は`Moyai`、自動起動、実行アカウントはLocalServiceです。設定はLocalServiceが読み取り、管理者が更新します。data・logsにはLocalServiceの変更権限を設定します。LocalServiceは利用者のファイル権限やネットワークドライブ割り当てを引き継ぎません。

管理CLIは`service register`（登録）、`service start`（起動）、`service status`（状態）、`service pause`（一時停止）、`service resume`（再開）、`service stop`（停止）、`service unregister`（登録解除）です。変更にはWindows権限が必要です。解除には停止状態が必要で、DB・設定は削除しません。一時停止中は新しいHTTP要求を503で拒否し、受付済み処理は完了できます。再開は受付を戻し、停止はホストを終了します。業務CLIは接続できない場合エラーとなり、SQLiteへ直接アクセスしません。

サービスログはWindows Applicationイベントログへ出力します。非対話のエラーではダイアログを表示しません。隔離試験では別DB・別ポートのJSONを用意し、`Moyai.Mcp.exe --config <path>`で起動できます。リリース前には管理者権限のある隔離WindowsでInstall・Upgrade・一時停止・再開・Uninstall・再起動・DB保持を検証します。
