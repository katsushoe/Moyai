# Moyai v1完成ロードマップ

[English](ROADMAP.md) | [日本語](ROADMAP.ja.md)

このロードマップは、v1正式仕様とv1.0.3で公開済みの機能との差を解消するためのものです。マイルストーンは依存関係順です。Domain model、SQLite migration、Application logic、CLI、MCP Tool、Error contract、Audit event、Test、利用者向け文書が揃った時点で完了と判定します。

## 現在地: v1.0.3

ProjectとWorkItemの基本操作、Type固有のWorkItem遷移、Optimistic Lock、Soft Delete、status/diff/commit/push/pullのRepository Provider routing、Service認証、Lifecycle Provider委譲、Event記録、Migration前Backup、Streamable HTTP MCP、CLI、WiX MSI配布を実装済みです。

現在のLifecycle操作はProviderへ委譲して結果をEventへ記録しますが、仕様で必要なBuild、Release、Deploymentの完全なEntity管理は未実装です。

## マイルストーン1: データ・監査基盤

- 状態: 2026-08-28完了。
- Relation、Comment、Task Link、Commit Link、Release、Release Item、Release Artifact、Build、Build Artifact、Deployment Target、Deploymentのmigrationと永続化contractを追加します。Entity固有repositoryは、永続化とApplication動作を一緒に検証するため、対象機能のマイルストーンで実装します。
- 安定したID、revision、日時、制約、外部キーを定義します。
- Append-only Event HistoryとMigration前Backupを維持します。
- Migration upgrade、失敗時rollback、制約、WAL、同時更新をテストします。

完了条件: v1.0.3の既存DBをデータ損失なく更新でき、すべての新しい可変tableがrevision contractを持ち、外部キー、immutable record、append-only record、1対1 cardinalityをSQLiteが強制し、すべての実行時接続で外部キー検証が有効なこと。状態変更のAudit Eventは、対象機能のEntity固有repositoryと同時に追加します。

## マイルストーン2: WorkItem連携

- 状態: 2026-08-28完了。
- 方向とcycle検証を含むRelationの追加・削除・一覧を実装します。
- Commentの追加・一覧を実装します。
- Hataori Task LinkとCommit Linkの追加・削除・一覧を実装します。
- WorkItem Historyを実装します。
- 対応するCLI commandとMCP Toolを公開します。

完了条件: Acceptance Criteria 7、8、9およびWorkItem History要件がend-to-end testで合格すること。

## マイルストーン3: 検索・Project集約表示

- 状態: 2026-08-28完了。
- FTS5 WorkItem indexを追加し、更新に追従させます。
- 条件指定可能なWorkItem Searchを実装します。
- Project OverviewとChanges Sinceを実装します。
- Archive/Delete表示条件、pagination、安定した並び順を検証します。

完了条件: Acceptance Criteria 23、24、25がCLIとMCPの両方で合格すること。

## マイルストーン4: Repository Contract完成

- 状態: 2026-08-29完了。
- Branchの一覧・作成・削除を追加します。
- Tagの作成・削除・pushを追加します。
- Provider情報とcapability negotiationを完成させます。
- Provider停止、認証、policy、競合、再試行可能エラーを共通形式へ正規化します。
- Githubie adapterとBuckettie adapterで共用するcontract testを追加します。

完了条件: v1 MCP APIの全Repository操作とAcceptance Criteria 13から18が、MoyaiからGitを直接実行せず合格すること。

## マイルストーン5: Release管理

- 状態: 2026-08-29完了。
- Releaseの作成・取得・一覧・更新とstatus遷移を実装します。
- ReleaseとWorkItemの関連、Release Artifact metadataを管理します。
- prepare、mark-ready、publish、retry、withdraw、latest release、release overviewを実装します。
- Publish途中失敗を永続化し、安全なretryとidempotencyを実装します。

完了条件: Acceptance Criteria 10、11、12、19、30とRelease workflowがend-to-end testで合格すること。

## マイルストーン6: Build管理

- 状態: 2026-08-30完了。
- BuildとimmutableなBuild Artifact Entityを実装します。
- build start/get/list/artifacts/cleanとproject buildを実装します。
- C#、Node、PHPの標準Build Providerを追加します。
- source commitを記録し、標準Buildではdirty treeを拒否し、file hashまたはdirectory manifest hashを計算します。
- Build ArtifactをRelease Artifactへ関連付けます。

完了条件: Acceptance Criteria 31から34および44が、再現可能なArtifact metadataとともに合格すること。

## マイルストーン7: Deploy管理

- 状態: 2026-08-30完了。
- 1 Project = 1 DeploymentTargetを実装します。
- `install_path`へのLocal Deployとverifyを実装します。
- SSH secretを保存せず、KelpieSSH Streamable HTTP経由のServer Deployを実装します。
- start/get/list/status/retry/rollbackとrollback_failed履歴を実装します。
- BuildからDeploy、およびBuildからReleaseを経由するDeployの両方へ対応します。

完了条件: Acceptance Criteria 35から43および45が、verify失敗とrollback失敗を含めて合格すること。

## マイルストーン8: v1適合確認・配布

- 2026-08-30完了: 45項目すべての[Acceptance Criteria追跡表](V1_TRACEABILITY.ja.md)を作成しました。42項目は適合、3項目は外部Provider検証が必要です。
- CLI/MCP parity、標準response/error、認可、idempotency、障害復旧testを追加します。
- 2026-08-30完了: 公開済みv1.0.3.0 CLIが作成したDBをMigrationし、Projectデータ保持と読取可能なMigration前Backupを検証しました。
- Release build、全自動test、MSI upgrade/install/uninstall、実機smoke testを実行します。
- 公開文書を更新し、明示的なrelease承認後にだけ公開します。

完了条件: 全Acceptance Criterionに合格根拠、または明示承認済みの仕様訂正があり、必須項目に部分実装が残っていないこと。

## 推奨実行順

マイルストーン1から8まで順番に進めます。マイルストーン5、6、7はProvider委譲だけでは完了とせず、状態、履歴、Artifact、retry、追跡関係をMoyaiが永続管理できることを必須とします。
