# Providerユーザーホスト接続

## Githubie Windows所有者接続

Githubie所有者方式は明示的に選択します。Provider設定に`name: githubie`、`toolPrefix: github`、`repository: true`、`authenticationMode: windows_owner`、`connectionMode: githubie_user_host_v1`、選択した`Githubie.Server.exe`の絶対パスを`runtimeExecutable`に指定します。Moyaiは、接続情報の記録がこのパスを示すこと、実行中プロセスのイメージがこのパスであること、プロセスの所有者・セッション・生存、profileとファイルのACLを検証します。実行ファイル内容のハッシュは照合しないため、同じパスでGithubieを更新しても接続できます（CR-2026-11-27）。`runtimeSha256`は非推奨で、指定されていても受け付けて無視します。`endpoint`と`runtimeDirectory`は省略します。現在のWindowsユーザーのGithubie profileはLocalApplicationData既知フォルダーから解決し、資格情報は設定へ書きません。[ADR 0023](adr/0023-same-user-provider-trust.md)も参照してください。

Consumerはprivate profile、discoveryの版と内容、所有者・セッション・プロセス、実行ファイルの固定パスとハッシュ、profileのハッシュ、CurrentUser DPAPIを確認し、sessionとgenerationの両ヘッダーを送信します。業務操作前にProvider版、所有者契約、公開Toolと該当能力を検証します。署名や固定URLへのフォールバックは使わず、結果不明の変更操作は自動再送しません。実Providerとの隔離受入は未完了です。

## Buckettieユーザーホスト接続

Buckettie discovery schema 1を選択して利用するConsumerです。通常環境のProvider接続の有効化や移行は行いません。設計・信頼境界は[ADR 0020](adr/0020-provider-user-host-consumer.md)を参照してください。

```json
{
  "name": "buckettie",
  "toolPrefix": "bitbucket",
  "repository": true,
  "connectionMode": "buckettie_user_host_v1",
  "runtimeDirectory": "<承認されたBuckettie runtimeの絶対パス>",
  "expectedMcpPath": "/mcp",
  "assertionToolScopes": {
    "bitbucket_repository_status": ["repository.read"]
  }
}
```

`runtimeDirectory`には、現在の対話Windowsユーザーが所有する、明示的に承認されたディレクトリを指定してください。`endpoint`は省略します。固定URLへのフォールバックはありません。これらは管理者が選択する公開設定で、Tool引数から任意のパス・URL・headerを受け付けません。既存設定の`connectionMode`は既定で`static`です。他Provider向けにこの動的モードを指定すると拒否します。

Consumerは`host.json`の版・Provider・profile・ACL・reparse・SID・session・PID生存・開始ticksを検証してからCurrentUser DPAPIで復号します。全HTTPに所有者資格を付け、署名対象Toolには従来のProject-bound ES256も追加します。一般設定や引数へ資格を入力・転記しません。

| エラー | 意味 |
|---|---|
| `user_host_not_running` | runtimeまたはdiscoveryがありません。 |
| `host_discovery_invalid` | 構造・資格・接続先が不正です。 |
| `host_discovery_version_unsupported` | 版が未対応です。 |
| `host_provider_mismatch` | Providerが異なります。 |
| `host_profile_mismatch` | runtimeとの対応が異なります。 |
| `host_owner_session_mismatch` | 所有者または対話sessionが異なります。 |
| `host_discovery_stale` | プロセスが終了、またはPIDの世代が異なります。 |
| `host_discovery_unavailable` | ACL・アクセス・IO・復号で信頼できる読取に失敗しました。 |

読取の再接続は最大1回です。変更操作は自動再送しません。送信後の結果が不明な場合はProvider監査と実状態を照合してから判断してください。Providerの起動・停止・修復はProvider担当の操作です。実Providerとの隔離相互運用は未完了で、模擬transport試験だけでは受入完了としません。
