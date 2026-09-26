# 変更履歴

[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## 未公開

## 1.3.5.0 - 2026-09-26（ローカルパッケージ）

- `mode=legacy`の移行期間外に、Assertion方式でないProviderのRelease操作が静的Service Tokenで続行できた問題を修正し、`authentication_unavailable`で拒否するようにしました。
- Broker方式のKey Protectorで、応答を読み取る前に受信バッファを消去していた問題を修正しました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.4.0 - 2026-09-25（ローカルパッケージ）

- Assertion方式のLifecycle Provider（Githubie Release）では、静的Service Token（`release.write`）を要求しないようにしました。1.3.3.0でもTokenの事前検査によりProvider呼び出し前にRelease公開が失敗していた問題を修正します。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.3.0 - 2026-09-25（ローカルパッケージ）

- GithubieへのRelease作成・公開・照会（`github_release_get`、`github_tag_get`、`github_release_create`、`github_release_update`）に、Tool単位のProvider Assertionを付与するようにしました。Assertion必須のGithubieへMoyai経由でReleaseを公開できなかった問題を修正します。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.2.0 - 2026-09-25（ローカルパッケージ）

- Buckettieへ状態変更操作（読み取り以外）を委譲する前に、Bootstrapの`bitbucket_provider_capabilities`で`integration_mode`を確認し、`moyai`でない場合は`provider_integration_mode_mismatch`で拒否するようにしました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.1.0 - 2026-09-24（ローカルパッケージ）

- 内部routing名`githubbie`を維持したまま、Provider AssertionのProvider IDとAudienceを正規ID`githubie`へ解決するようにしました。
- Provider用共通認証パッケージ（Moyai.ProviderAuthentication 1.0.2）とKelpieSSH向けLifecycle Protocol v2・段階Deploy Adapterを含めました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.0.0 - 2026-09-07（ローカルパッケージ）

- 操作ごとのES256 Provider Assertion、共通Claim・Replay検証、秘密情報のEnvelope暗号化、署名鍵・KEK更新、サービス経由のMCP／CLI鍵管理6操作を追加しました。
- CNG、Keychain、Secret Service、認証済みBrokerのKey Protectorを追加しました。Repository認証は未構成時に操作を拒否し、旧方式は最大7日の明示的な移行期間に限定します。
- macOS／Linux実機と実Providerの移行・結合検証は継続中です。ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.2.3 - 2026-09-04

- `branch_create`でliteral branch名または完全なcommit SHAの作成元を明示必須とし、不正なrevision式をProvider実行前に拒否して、Repository Provider契約へ無変更で転送するようにしました。
- Release公開時にProvider側のdraftを先に作成し、登録済み成果物とリリースノートをGithubieまたはBuckettieの正確なProvider引数契約で渡すよう修正しました。
- Lifecycle Providerの業務失敗をRelease成功扱いする問題を修正し、誤分類済みReleaseを訂正する遷移を追加しました。
- `tag_create`で作成元のliteral branch名または完全なcommit SHAを必須とし、Githubieへ無変更で転送するようにしました。
- Tag作成時にGithubieで必須となるnull許容の注釈メッセージ項目を送信するようにしました。

## 1.2.1 - 2026-09-04

- Repository Providerの業務失敗を成功扱いする問題と、CLIが業務失敗でも終了コード0を返す問題を修正しました。構造化・テキスト応答の検証と回帰テストを追加しました。

- Codex／Claude Codeのユーザー単位の接続登録・解除CLI、インストーラのクライアント・プロファイル選択、所有設定の保護と失敗時復元を追加しました。

## 1.2.0 - 2026-09-03

- ローカルインストーラを1.2.0へ更新し、実機サービス・CLI・設定保持・DB全23テーブルの件数一致を確認しました。

- `project-rename` / `project_rename`を追加しました。設定と関連データを保持して名前だけを変更し、revision検査と監査履歴を記録します。

- 名前だけでのProject作成、`project-ensure`による未登録時だけの作成、`project-configure`による後からの設定に対応しました。Repository・ビルド・デプロイの必要設定は操作実行時に検査します。

- インストーラによるLocalServiceでのWindowsサービス自動起動、停止・登録解除、既存DB保持の構成を追加しました。
- 環境変数によるサービス設定を永続JSONへ変更し、CLIの業務コマンドをサービス接続方式へ統一しました。
- CLIを`moyaictl.exe`へ改名し、`service start`、`stop`、`pause`、`resume`、`register`、`unregister`、`status`サブコマンドを追加しました。
- 1.1.1のMSI実機更新、一時停止・再開・停止・起動、設定保持、DB全23テーブルの件数一致を確認しました。1.2.0では隔離Windows VMで17項目に合格し、サービス登録・解除、アンインストール時のDB・設定保持、再インストール後のデータ保持を確認しました。

## 1.0.7 - 2026-08-31

- MCPのProject探索指示、`list_projects` Tool別名、未登録エラーの登録済みProject候補を追加しました。
- v1受入基準の追跡を完成させ、Architecture、Build、Deployment、Release Contentの検証を拡充しました。

## 1.0.6 - 2026-08-30

- Release Domain Model、SQLite永続化、Application Service、対応するCLI／MCP操作を追加しました。
- 再試行可能エラーとPolicy拒否を含むRepository Providerエラー正規化を完成させました。
- Project検索、重複登録判定、変更対象の解決をOrdinalな大文字小文字非区別比較へ統一しました。

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
