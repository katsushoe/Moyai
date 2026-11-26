# 変更履歴

## 未リリース

## 2.1.0.2

- Githubie所有者方式で、Githubie実行ファイルのSHA-256を照合しないようにしました。同じパスでGithubieを更新しても`discovery_runtime_mismatch`で止まりません。パス、プロセスのイメージ、所有者、セッション、生存、profileとACLの検証は維持します。`runtimeSha256`は非推奨で、指定されていても無視します（CR-2026-11-27）。

- トレイに「MCPサーバー」メニューを追加しました。4状態のピル型LED、Health確認と版表示、期限付き起動停止、Codex・Claude Code全セッションの切断を伝える既定「いいえ」の停止確認に対応します。

- 太い黒縁と白塗りでロープの結び目を図案化したMoyaiのアイコンを、トレイアプリのexe、各ウィンドウ、通知領域、インストール済みアプリ一覧に設定しました。背景は透明で、ライト/ダーク両方のタスクバーに同じ配色を使います。

## 2.1.0.1

- トレイメニューに「バージョン情報」を追加し、製品名、トレイアプリの版数、発行元、アプリの説明を表示します。

## 2.1.0.0

- 通知領域に設定画面を追加し、起動時の自動更新とProviderリクエストのタイムアウトを変更できるようにしました。
- MCPホストが停止しているトレイ起動時に、署名済みの安定版MSIを自動適用します。

## 2.0.4.4 - 2026-11-13（開発候補）

- 設定の設計説明とPR Toolの表示で所有者認証・署名認証を区別し、既存の認証・Provider方針を保持します。
- リソース・インストーラー設定・固定Toolを含む納品/ビルド入力を包装前に固定し、包装後に対象とhashを照合します。全Git状態の確認はcommit前の別条件です。

## 2.0.4.3 - 2026-11-11（開発候補）

- 所有者認証接続で使う正規Provider名 `githubie` を設定取得・明示更新で受け付けます。公開元リスト・PR経路を含むGithubie専用項目でも、Projectとの対応・承認・拒否の扱いを保持します。実Provider受入は別途必要です。

## 2.0.4.2 - 2026-11-10（開発候補）

- GithubieのPR公開元リスト・PR経路を、設定取得・明示更新・変更項目のMCP/CLI応答へ反映します。省略/空配列を区別して型/値を検証し、通常commit許可から自動移行しません。
- Githubie専用項目はBuckettieへの送信前に拒否し、既存認証・Provider承認・結果不明時の扱いを保持します。実Providerとの相互確認は別途必要です。

## 2.0.4.1 - 2026-11-03（開発候補）

- 同じWindowsユーザー/セッションのトレイ重複起動を、画面作成・ホスト操作の前に抑止します。終了時に所有権を解放し、異常終了後も再起動できます。初回更新時には旧版トレイを閉じてください。

## 2.0.4.0 - 2026-11-02（開発候補）

- PR限定公開create/statusの専用Consumerとホスト接続型MCP/CLIを追加しました。UUID/draft明示、両capability/scope、所有者認証を検証します。
- 失敗時も公開/PRの部分結果と要求文脈を保持し、不整合応答を拒否、結果不明の変更は自動再送しません。既存PR作成と固定v5は保持します。

## 2.0.3.2 - 2026-11-02（開発候補）

- トレイの状態画面を閉じられず、再起動メニューが無効のままになる実機受入の問題に対応しました。状態表示と操作エラーを通常の非モーダル画面に変更し、CLI処理の完了時に操作メニューを復帰します。
- 状態画面はタスクバーから選択でき、「閉じる」・Esc・閉じるボタンで画面だけを閉じます。トレイとホストの実行を保持し、再起動の自動再送は行いません。

## 2.0.3.1 - 2026-11-01（開発候補）

- インストール時のエラー1943を修正します。保存済みの設定所有者SIDをトランザクション前に解決し、MSIプロパティ非対応のSDDL欄ではなく、展開に対応するWiX utilityの権限処理で適用します。
- major upgradeの旧製品削除をトランザクションに含め、失敗時に旧版を復旧できる順序へ変更しました。2.0.3.0実導入の失敗記録を保持し、パッケージ表の検証だけで実受入完了とはしません。

## 2.0.3.0 - 2026-10-31（開発候補）

- Githubieのclassic hostingブランチ保護の読取・更新・削除を共通Provider操作、MCP、サービス接続CLIへ追加し、明示部分変更と専用能力・認証権限を確認します。
- 署名鍵から独立したschema8操作監査を追加しました。intent保存不能時は未送信、結果不明時は自動再送しません。Provider Desktop承認とclassic/ruleset/gateway方針の違いを文書化しました。実Provider/GitHub/Desktop/MSI受入は別途残ります。

## 2.0.2.0 - 2026-10-30（開発候補）

- 秘密値を含めない読込済み設定・Provider認証診断、内容比較による設定未反映判定、所有者方式の署名不要表示を追加しました。
- トレイの明示的ホスト再起動、英日状態表示、CLI操作の直列化を追加しました。自動再送・再読込は行わず、導入後の実ホスト/GUI受入は別途行います。


[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## 2.0.1.0 - 2026-10-30（開発版）

- Githubie経由の認証付き`pull_request_get`と、承認対象SHAを指定する`pull_request_merge`、対応するサービス接続CLIを追加しました。
- 明示能力と専用権限、事前の実測source SHA照合を必須にし、pending/unknownを保持してマージを自動再送しません。BuckettieのPR状態経路は無効のままで、実PR受入とインストールは別途行います。

## 2.0.0.2 - 2026-10-25（開発版）

- 運用設定を`<INSTALLFOLDER>\config\moyai.json`だけに固定し、別パス指定とユーザープロファイルへのフォールバックを拒否します。
- ホスト起動・再起動の前に設定を検証し、明示的な初期化だけでファイルを作成します。
- 設定所有ユーザーのアクセス権とSIDをMSI修復・更新でも保持します。DB・ログ・署名鍵はユーザー領域に保持します（ADR0022）。

## 2.0.0.1 - 2026-10-25（ローカル導入版）

- ホスト制御の接続/応答期限を分離し、停止ACK後の再flushを避け、応答喪失を明示的なエラーにしました。
- restartは対象プロセスの終了後に置換起動し、呼出元キャンセルとready期限切れを区別します。
- トレイの状態表示・停止/起動・自動起動の無効化/復帰を本人操作とCLI照合で受入し、自動起動切替中の稼働ホスト保持を確認しました。
- 4桁の製品版をMSI内部版2.0.1へ変換し、更新順序を識別します（ADR0021）。既存profileを保持した更新・MSI修復後のCLI確認は各29件成功しました。追加Windows受入と公開は未完了です。

## 2.0.0.0 - 2026-10-22（開発ビルド）

- マシン共通のWindows Serviceホストを、ログオンユーザー別ホスト・状態・named pipe制御・トレイ管理へ変更します。
- 旧ServiceのDB・設定・鍵をrollback用に保持し、新profileは空DBから開始してProject/Providerを再登録します。
- Service撤去と旧データ保持を行うMSI定義を更新しました。Windows実機のinstall/upgrade受入と公開Releaseは未完了です。

## 1.3.14.0 - 2026-10-21（ローカルパッケージ）

- `moyaimcp.exe`のMCP操作クライアント実行名、先頭の`--config`、ローカル`--version`を追加し、既存`moyaictl`との互換性を維持します。

## 1.3.13.1 - 2026-10-20（ローカルパッケージ）

- `tools/call`の要求内容が不正な場合にAssertion送信ガードを消費しないようにします。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.12.0 - 2026-10-14（開発ビルド）

- Githubie/Buckettie経由で既存local branchへ切り替える、状態変更MCP/CLI操作を追加します。
- 明示capabilityとProject単位のrepository.branch.writeを必須とし、branch拒否コードと結果不明を保持して自動再送しません。
- 稼働設定・実Repositoryは変更していません。実Providerでのbranch切替受入は未完了です。

## 1.3.11.0 - 2026-10-14（開発ビルド）

- 合意済みGithubie契約の設定読取・部分更新をMCP/CLIへ追加し、省略とfalse/配列の明示値を保持します。
- 設定capability、Project Assertion、専用write scope、Providerの人の承認を必須とし、未合意Buckettieとlegacy経路を拒否します。
- 非秘密の登録設定8項目だけを返し、更新結果不明は自動再試行せず、再更新前に読取で確認します。
- 稼働環境へ未導入・公開Release前です。両Providerの結合確認は未完了です。

## 1.3.10.0 - 2026-10-14（開発ビルド）

- schema v7でProject単位のnullable Provider Repository IDを保存します。未設定時は従来のProject名を使い、UUIDと関連データを維持します。
- 楽観的排他制御と監査を使う `project_provider_repository_set`／`project-provider-repository-set` を追加し、対応付けの登録・更新・明示解除を提供します。
- RepositoryとRepositoryを使うRelease Toolへ登録IDを転送し、Project名と署名付きUUID・正規化Repository URL・audience・scopeを維持します。
- 開発ビルドの記録であり、稼働環境への導入や公開Releaseではありません。

## 1.3.9.0 - 2026-10-08（ローカルパッケージ）

- `Project.GitRemoteName`を省略可能にしました。schema v6で`projects`表を再構築し、保存済みの`origin`を`null`（自動解決）へ変換し、それ以外の名前は明示指定として保持します。
- Providerが`remote_resolution` version 1／`repository_url`を表明した場合だけ、`gitRemoteName`をProviderの`remote`引数として転送します。対象はGithubie・Buckettieの`repository_status`とBuckettieの`repository_commit`です。Githubieのcommitとその他の操作には`remote`を送りません。
- Providerのエラーコード`provider_remote_not_found`、`provider_remote_ambiguous`、`provider_remote_mismatch`をそのまま返します。
- Build開始時に`local_head`をSource Commitとして読み取り、`dirty`／`is_dirty`がない場合は`working_tree_clean=false`をDirtyとして扱います。
- 隔離AC37 runtime（2026-10-07／08）は、表示版1.3.8.0のままこのコードを実行していました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.8.0 - 2026-09-27（ローカルパッケージ）

- BuckettieのRelease照会・作成・公開・取り下げにTool単位のProvider Assertionを付与し、旧方式の静的`release.write` Service Tokenを要求しないようにしました。
- BuckettieのRelease照会と作成で、別々の検証済みAssertionを送る回帰テストを追加しました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.7.0 - 2026-09-27（ローカルパッケージ）

- Release系LifecycleとKelpieSSH Deployで、`auth_assertion_expired`の再試行前にProjectのID・Revision・Repository・Provider・Deploy方式を再確認し、変更時は`auth_project_mismatch`で再実行しないようにしました。
- テスト用の旧`Es256AssertionValidator`を非公開にしました。本番の検証はMoyai.ProviderAuthenticationの共通Validatorを使用します。
- ローカルインストール用パッケージであり、公開Releaseではありません。

## 1.3.6.0 - 2026-09-26（ローカルパッケージ）

- ProviderのCapabilityに含まれない`get_version`と`*_provider_capabilities`をBootstrapとして無Assertionで呼び出し、BuckettieのProvider Capability照会を利用可能にしました。
- ProviderのHTTP 401／403による認証拒否を、通信障害の`provider_unavailable`と区別して`provider_authentication_rejected`で返すようにしました。
- Assertion経路のHTTP ClientがRequest Timeout設定ではなく既定の100秒で打ち切られる問題を修正しました。
- ローカルインストール用パッケージであり、公開Releaseではありません。

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
