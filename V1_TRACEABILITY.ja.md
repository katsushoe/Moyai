# Moyai v1受入基準追跡表

[English](V1_TRACEABILITY.md) | [日本語](V1_TRACEABILITY.ja.md)

本表はv1仕様書51章の受入基準45項目と、実装・検証根拠を対応付けます。`適合`は本repository内に自動検証根拠がある状態、`部分適合`は実装根拠はあるものの、境界、外部結合、障害復旧、または仕様判断の検証が残る状態です。

## 集計

- 適合: 42項目
- 部分適合: 3項目
- 未実装: 0項目
- 全`部分適合`を検証済みにするか、明示承認された仕様訂正で解決するまでマイルストーン8は未完了です。

## 追跡表

| ID | 受入基準 | 状態 | 実装・検証根拠 | 残る検証 |
| ---: | --- | --- | --- | --- |
| 1 | 1つの中央SQLite DBで複数Projectを管理できる。 | 適合 | `SqliteProjectRepositoryTests`で共有DBへのProject永続化を検証。 | — |
| 2 | 外部ClientはDBを直接操作しない。 | 適合 | `McpToolsDoNotDependOnSqlitePersistence`で外部Tool境界を強制。 | — |
| 3 | Project名でProjectを指定できる。 | 適合 | `ProjectOperationsUseOrdinalCaseInsensitiveCanonicalName`。 | — |
| 4 | 1 Project = 1 Repositoryを保証する。 | 適合 | Project永続化・検証でProjectごとに単一のRepository設定を保持。 | — |
| 5 | 6種類の標準WorkItemを作成できる。 | 適合 | `WorkItemTests`で全標準Typeを検証。 | — |
| 6 | Type固有Workflowに従ってStatus遷移できる。 | 適合 | `WorkItemTests`と`SqliteWorkItemRepositoryTests`で正常・不正遷移を検証。 | — |
| 7 | WorkItem間Relationを管理できる。 | 適合 | `SqliteWorkItemCollaborationRepositoryTests`。 | — |
| 8 | WorkItem Commentを保持できる。 | 適合 | `SqliteWorkItemCollaborationRepositoryTests`。 | — |
| 9 | Hataori TaskとのLinkを保持できる。 | 適合 | `SqliteWorkItemCollaborationRepositoryTests`。 | — |
| 10 | Release Historyを管理できる。 | 適合 | `SqliteReleaseRepositoryTests`。 | — |
| 11 | Release Artifact metadataを管理できる。 | 適合 | `SqliteReleaseContentRepositoryTests`。 | — |
| 12 | ReleaseとWorkItemを関連付けられる。 | 適合 | `SqliteReleaseContentRepositoryTests`。 | — |
| 13 | Repository Provider Contract経由でGithubie/Buckettieを呼び分けられる。 | 適合 | `ProviderRoutingServiceTests`で設定Providerへのroutingを検証。 | — |
| 14 | Githubie/Buckettieが同じ共通Tool Contractを実装できる。 | 部分適合 | 2026-09-04時点の稼働版Githubie 1.8.6.3とBuckettie 1.3.19.0が、いずれも`repository_diff`・`repository_commit`を含むRepository操作への対応を能力照会で返すことを確認しました。 | Moyaiから両稼働Providerを呼び出すconformance testを実行。 |
| 15 | Moyai自身はGit CLIを実行しない。 | 適合 | `MoyaiSourceDoesNotInvokeGitCli`。 | — |
| 16 | Commit/Push/Tag/ReleaseをProvider経由で実行できる。 | 部分適合 | Moyaiは変更をProviderへ委譲します。2026-09-04のMoyai 1.2.1公開では、稼働版Githubieによるcommit・push・tag・releaseが成功しましたが、Moyai自身がMoyai Projectに未登録だったためGithubieを直接利用しており、Moyai経由の実績ではありません。 | Moyaiへ登録したGitHub・Bitbucket検証Projectで、明示承認済みの変更操作testを実行。 |
| 17 | Provider停止時に`provider_unavailable`を返せる。 | 適合 | `ExecuteAsyncReturnsUnavailableWhenProviderCannotBeReached`。 | — |
| 18 | ProviderをMoyaiが自動起動しない。 | 適合 | `RepositoryAndLifecycleAdaptersDoNotStartProviderProcesses`。 | — |
| 19 | Release Publishの途中失敗を記録できる。 | 適合 | `PublishFailurePersistsFailedAndAllowsRetry`。 | — |
| 20 | Optimistic LockでLost Updateを防止できる。 | 適合 | Project、WorkItem、Releaseのstale revision test。 | — |
| 21 | WAL Modeで複数Client利用に対応できる。 | 適合 | `SqliteDatabaseInitializerTests`で初期化contractを検証。 | — |
| 22 | Event Historyをappend-onlyで保存する。 | 適合 | Schema triggerとinitializer testでEvent不変性を強制。 | — |
| 23 | FTS5でWorkItemを検索できる。 | 適合 | `SearchIndexesTitleDescriptionAndCommentsWithFiltersAndPagination`。 | — |
| 24 | Project Overviewを1回のTool呼出しで取得できる。 | 適合 | `OverviewAndChangesSinceReturnDeterministicProjectState`。 | — |
| 25 | Project Changes Sinceを取得できる。 | 適合 | `OverviewAndChangesSinceReturnDeterministicProjectState`。 | — |
| 26 | Soft Deleteを利用できる。 | 適合 | WorkItem soft delete/restoreとProject archive test。 | — |
| 27 | DB MigrationとMigration前Backupができる。 | 適合 | 自動Migration testに加え、2026-08-30に公開済みv1.0.3.0 CLIでProject入りDBを作成し、現行CLIでMigration後もProjectが保持され、読取可能なSQLite事前Backupが生成されることを検証。 | — |
| 28 | CLIとMCPが同じApplication Logicを利用する。 | 適合 | `CliAndMcpComposeTheSameApplicationServices`で共通Application Service集合を強制。 | — |
| 29 | 認証SecretをMoyai DBに保存しない。 | 適合 | 2026-08-30承認済み明確化: 本基準は7.6・43.2節で定義された外部Repository Provider資格情報とSSH資格情報を対象とします。これらはGithubie/BuckettieとKelpieSSHが保持します。Moyai発行の内部service tokenは本基準の対象外であり、`service_tokens`の運用保護対象データとして扱います。 | — |
| 30 | Release Artifact本体をSQLiteに保存しない。 | 適合 | Release Artifactはmetadataとpathのみ永続化。 | — |
| 31 | ProjectごとにBuild Providerを設定できる。 | 適合 | Project modelと`BuildServiceTests`でProvider選択を検証。 | — |
| 32 | C#/PHP/Nodeの標準Build Providerを利用できる。 | 適合 | `StandardBuildProviderTests`で最小Projectに対して.NET、npm、Composerを実行。 | — |
| 33 | Buildが`source_commit`を記録し、Dirty Working Treeを標準Buildにしない。 | 適合 | `BuildServiceTests`でclean commit保存とdirty tree拒否を検証。 | — |
| 34 | BuildArtifactがimmutableで、File HashまたはDirectory Manifest Hashを保持できる。 | 適合 | Schema不変性、file SHA-256、決定的なDirectory Manifest SHA-256を検証。 | — |
| 35 | Projectごとに`local`/`server`のDeploy Modeを設定できる。 | 適合 | Project domain validationとDeployment Service routing。 | — |
| 36 | Local Deployが`install_path`へBuildArtifactを配置できる。 | 適合 | `LocalDeployVerifiesArtifactAndRollbackRestoresPreviousContent`。 | — |
| 37 | Server DeployがStreamable HTTP経由でKelpieSSHを利用できる。 | 部分適合 | Streamable HTTP transportは実装済みですが、現行adapterは単一Tool呼出しです。KelpieSSH側task `kelpiessh-moyai-deploy-contract-20260830`は、Moyaiへのintegration test依頼送信まで進んだ95%時点で、DB保守により`Expired`となっています。 | KelpieSSHの正式contractと結合環境を再確認し、段階orchestrationの実装およびintegration testを完了。 |
| 38 | MoyaiがSSH Password/Private Keyを保存しない。 | 適合 | DeploymentTargetはProvider target参照のみを持ち資格情報fieldなし。ADRにも分離を明記。 | — |
| 39 | DeploymentTargetを独立Entityとして保持できる。 | 適合 | Domain、SQLite table、repository、CLI、MCPを実装。 | — |
| 40 | v1で1 Project = 1 DeploymentTargetを保証する。 | 適合 | SQLiteの`project_id`一意制約とinitializer constraint test。 | — |
| 41 | DeploymentがBuild ID/Source Commit/Destinationを追跡できる。 | 適合 | Deployment domainと永続化が3値を保持。 | — |
| 42 | Deploy Verify失敗を成功扱いしない。 | 適合 | `LocalDeployWithInvalidArtifactHashPersistsFailedStatus`。 | — |
| 43 | Rollback/`rollback_failed`状態を履歴として保持できる。 | 適合 | Deployment testでrollback成功と`rollback_failed`永続化を検証。 | — |
| 44 | ReleaseArtifactを元BuildArtifactまで追跡できる。 | 適合 | `SqliteReleaseContentRepositoryTests`でSQLite外部キー制約下のBuildArtifact参照を保存・取得。 | — |
| 45 | Build→DeployとBuild→Release→Deployの両方を扱える。 | 適合 | `DeploymentServiceTests`で直接DeployとBuild/Release lineage永続化を検証。 | — |

## 次の検証単位

残る完了作業は、稼働中のGithubie/BuckettieをMoyaiから呼び出すconformance test、Moyai登録済みGitHub・Bitbucket検証Projectでの承認済み変更操作test、および仕様19.5のKelpieSSH段階orchestration実装と実Provider結合検証です。
