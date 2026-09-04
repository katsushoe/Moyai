# Moyai v1 Acceptance Criteria Traceability

[English](V1_TRACEABILITY.md) | [日本語](V1_TRACEABILITY.ja.md)

This matrix maps the 45 acceptance criteria in section 51 of the v1 specification to implementation and verification evidence. `Verified` means automated evidence exists in this repository. `Partial` means implementation evidence exists, but a required boundary, integration, recovery, or specification decision is not yet verified.

## Summary

- Verified: 43
- Partial: 2
- Not implemented: 0
- Milestone 8 remains incomplete until every `Partial` row is verified or resolved by an explicitly approved specification correction.

## Matrix

| ID | Acceptance criterion | Status | Implementation and evidence | Remaining verification |
| ---: | --- | --- | --- | --- |
| 1 | One central SQLite database manages multiple projects. | Verified | `SqliteProjectRepositoryTests` exercises persisted projects in the shared database. | — |
| 2 | External clients do not operate the database directly. | Verified | `ArchitectureBoundaryTests.McpToolsDoNotDependOnSqlitePersistence` enforces the external Tool boundary. | — |
| 3 | A project can be addressed by name. | Verified | `SqliteProjectRepositoryTests.ProjectOperationsUseOrdinalCaseInsensitiveCanonicalName`. | — |
| 4 | One project maps to one repository. | Verified | Project persistence and validation keep one repository configuration per project. | — |
| 5 | Six standard WorkItem types can be created. | Verified | `WorkItemTests` covers all standard types. | — |
| 6 | Status transitions follow the type-specific workflow. | Verified | `WorkItemTests` and `SqliteWorkItemRepositoryTests` cover valid and invalid transitions. | — |
| 7 | WorkItem relations can be managed. | Verified | `SqliteWorkItemCollaborationRepositoryTests`. | — |
| 8 | WorkItem comments can be retained. | Verified | `SqliteWorkItemCollaborationRepositoryTests`. | — |
| 9 | Hataori task links can be retained. | Verified | `SqliteWorkItemCollaborationRepositoryTests`. | — |
| 10 | Release history can be managed. | Verified | `SqliteReleaseRepositoryTests`. | — |
| 11 | Release Artifact metadata can be managed. | Verified | `SqliteReleaseContentRepositoryTests`. | — |
| 12 | Releases and WorkItems can be related. | Verified | `SqliteReleaseContentRepositoryTests`. | — |
| 13 | Githubie and Buckettie can be selected through the Repository Provider Contract. | Verified | `ProviderRoutingServiceTests` covers configured provider routing. | — |
| 14 | Githubie and Buckettie can implement one common Tool Contract. | Verified | On 2026-09-04, Moyai queried the running Githubie 1.8.6.3 and Buckettie 1.3.20.0 servers and both reported support for `repository_diff` and `repository_commit`. Moyai-routed `repository_diff` also succeeded for the registered GitHub project `Moyai` and Bitbucket project `picturebooks`. | — |
| 15 | Moyai does not execute Git CLI. | Verified | `ArchitectureBoundaryTests.MoyaiSourceDoesNotInvokeGitCli`. | — |
| 16 | Commit, push, tag, and release run through providers. | Partial | Moyai routes mutations through providers. The running Githubie server successfully performed commit, push, tag, and release for Moyai 1.2.1 on 2026-09-04, but Githubie was called directly because Moyai itself was not registered as a Moyai Project; this is not evidence of the Moyai routing path. | Run explicitly approved mutation tests on Moyai-registered GitHub and Bitbucket test projects. |
| 17 | Provider outage returns `provider_unavailable`. | Verified | `McpRepositoryProviderTests.ExecuteAsyncReturnsUnavailableWhenProviderCannotBeReached`. | — |
| 18 | Moyai does not automatically start providers. | Verified | `ArchitectureBoundaryTests.RepositoryAndLifecycleAdaptersDoNotStartProviderProcesses`. | — |
| 19 | Partial Release Publish failure is recorded. | Verified | `ReleaseOrchestrationServiceTests.PublishFailurePersistsFailedAndAllowsRetry`. | — |
| 20 | Optimistic locking prevents lost updates. | Verified | Stale-revision tests cover Project, WorkItem, and Release. | — |
| 21 | WAL mode supports multiple clients. | Verified | `SqliteDatabaseInitializerTests` verifies the database initialization contract. | — |
| 22 | Event history is append-only. | Verified | Schema triggers and initializer tests enforce immutable events. | — |
| 23 | WorkItems can be searched with FTS5. | Verified | `SqliteProjectQueryRepositoryTests.SearchIndexesTitleDescriptionAndCommentsWithFiltersAndPagination`. | — |
| 24 | Project Overview is returned by one Tool call. | Verified | `SqliteProjectQueryRepositoryTests.OverviewAndChangesSinceReturnDeterministicProjectState`. | — |
| 25 | Project Changes Since can be obtained. | Verified | `SqliteProjectQueryRepositoryTests.OverviewAndChangesSinceReturnDeterministicProjectState`. | — |
| 26 | Soft delete is available. | Verified | `SqliteWorkItemRepositoryTests.SetDeletedAsyncSoftDeletesAndRestoresItem` and Project archive tests. | — |
| 27 | Database migration and pre-migration backup are available. | Verified | Automated migration tests pass. On 2026-08-30, the released v1.0.3.0 CLI created a Project database; the current CLI migrated it, retained the Project, and produced a readable SQLite pre-migration backup. | — |
| 28 | CLI and MCP use the same application logic. | Verified | `ArchitectureBoundaryTests.CliAndMcpComposeTheSameApplicationServices` enforces the shared application-service set. | — |
| 29 | Authentication secrets are not stored in the Moyai database. | Verified | Approved clarification on 2026-08-30: this criterion covers external Repository Provider and SSH credentials, as specified by sections 7.6 and 43.2. Those credentials stay in Githubie/Buckettie and KelpieSSH. Moyai-issued internal service tokens are outside this criterion and remain protected operational data in `service_tokens`. | — |
| 30 | Release Artifact bodies are not stored in SQLite. | Verified | Release Artifact persistence stores metadata and paths only. | — |
| 31 | A Build Provider can be configured per project. | Verified | Project model and `BuildServiceTests` exercise provider selection. | — |
| 32 | Standard C#, PHP, and Node Build Providers are available. | Verified | `StandardBuildProviderTests` executes installed .NET, npm, and Composer tools on minimal projects. | — |
| 33 | Every Build records `source_commit` and rejects a dirty standard build. | Verified | `BuildServiceTests` covers clean commit persistence and dirty-tree rejection. | — |
| 34 | BuildArtifact is immutable and stores a file or directory-manifest hash. | Verified | Schema immutability, file SHA-256, and deterministic directory-manifest SHA-256 are tested. | — |
| 35 | Each project supports `local` or `server` Deploy Mode. | Verified | Project domain validation and deployment service routing cover both modes. | — |
| 36 | Local Deploy places a BuildArtifact at `install_path`. | Verified | `DeploymentServiceTests.LocalDeployVerifiesArtifactAndRollbackRestoresPreviousContent`. | — |
| 37 | Server Deploy uses KelpieSSH over Streamable HTTP. | Partial | Streamable HTTP transport exists, but the current lifecycle adapter calls one `<prefix>_deploy` Tool. The KelpieSSH task `kelpiessh-moyai-deploy-contract-20260830` reached 95% after sending an integration-test request to Moyai, then became `Expired` during database maintenance. | Reconfirm the KelpieSSH contract and integration environment, implement staged orchestration, and complete integration tests. |
| 38 | Moyai does not store SSH passwords or private keys. | Verified | DeploymentTarget contains a provider target reference and no credential fields; the ADR documents credential separation. | — |
| 39 | DeploymentTarget is an independent entity. | Verified | Domain, SQLite table, repository, CLI, and MCP surface are implemented. | — |
| 40 | v1 guarantees one DeploymentTarget per project. | Verified | SQLite schema enforces unique `project_id` and initializer constraint tests cover it. | — |
| 41 | Deployment tracks Build ID, source commit, and destination. | Verified | Deployment domain and persistence store all three values. | — |
| 42 | Deploy verification failure is not treated as success. | Verified | `DeploymentServiceTests.LocalDeployWithInvalidArtifactHashPersistsFailedStatus`. | — |
| 43 | Rollback and `rollback_failed` states are retained in history. | Verified | Deployment tests cover successful rollback and persisted `rollback_failed`. | — |
| 44 | A ReleaseArtifact is traceable to its source BuildArtifact. | Verified | `SqliteReleaseContentRepositoryTests` persists and reads the source BuildArtifact reference under SQLite foreign-key constraints. | — |
| 45 | Both Build-to-Deploy and Build-to-Release-to-Deploy are supported. | Verified | `DeploymentServiceTests` covers direct deployment and persisted Build/Release lineage. | — |

## Next Verification Batch

Remaining closure work: run approved mutations on Moyai-registered GitHub and Bitbucket test projects; implement section 19.5 staged KelpieSSH orchestration and complete real-provider integration.
