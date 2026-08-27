# Moyai v1 Completion Roadmap

[English](ROADMAP.md) | [日本語](ROADMAP.ja.md)

This roadmap closes the gap between the v1 specification and the functionality released in v1.0.3. Milestones are ordered by dependency. A milestone is complete only when its domain model, SQLite migration, application logic, CLI, MCP tools, error contract, audit events, tests, and user documentation are complete.

## Baseline: v1.0.3

Available today: Project and WorkItem basics, type-specific WorkItem transitions, optimistic locking, soft deletion, repository Provider routing for status/diff/commit/push/pull, service authentication, lifecycle Provider delegation, event recording, migration backup, Streamable HTTP MCP, CLI, and WiX MSI delivery.

Lifecycle operations currently delegate to Providers and record events; they do not yet manage the complete Build, Release, or Deployment entities required by the specification.

## Milestone 1: Data and Audit Foundation

- Add migrations and repositories for relations, comments, task links, commit links, releases, release items, release artifacts, builds, build artifacts, deployment targets, and deployments.
- Define stable IDs, revision fields, timestamps, constraints, and foreign keys.
- Preserve append-only event history and migration-before-backup behavior.
- Add migration upgrade, rollback-on-failure, constraint, WAL, and concurrency tests.

Exit criteria: an existing v1.0.3 database upgrades without data loss, and every new mutable entity uses optimistic locking and audit events.

## Milestone 2: WorkItem Collaboration

- Implement relation add/remove/list with direction and cycle validation.
- Implement comment add/list.
- Implement Hataori task link and commit link add/remove/list.
- Implement WorkItem history.
- Expose matching CLI commands and MCP tools.

Exit criteria: Acceptance Criteria 7, 8, 9, and the WorkItem history requirements pass end-to-end tests.

## Milestone 3: Search and Project Views

- Add and maintain the FTS5 WorkItem index.
- Implement filtered WorkItem search.
- Implement Project Overview and Changes Since.
- Verify archived/deleted visibility, pagination, and deterministic ordering.

Exit criteria: Acceptance Criteria 23, 24, and 25 pass through both CLI and MCP.

## Milestone 4: Repository Contract Completion

- Add branch list/create/delete.
- Add tag create/delete/push.
- Complete Provider information and capability negotiation.
- Normalize Provider errors, including unavailable, authentication, policy, conflict, and retryable failures.
- Add contract tests shared by Githubie and Buckettie adapters.

Exit criteria: all repository operations in the v1 MCP API and Acceptance Criteria 13 through 18 pass without Moyai invoking Git directly.

## Milestone 5: Release Management

- Implement Release create/get/list/update and status transitions.
- Implement Release–WorkItem and Release Artifact metadata management.
- Implement prepare, mark-ready, publish, retry, withdraw, latest release, and release overview.
- Persist partial publish failures and support safe retry/idempotency.

Exit criteria: Acceptance Criteria 10, 11, 12, 19, 30, and the release workflow pass end-to-end tests.

## Milestone 6: Build Management

- Implement Build and immutable Build Artifact entities.
- Implement build start/get/list/artifacts/clean and project build.
- Add standard C#, Node, and PHP Build Providers.
- Record source commit, reject dirty standard builds, and calculate file or directory-manifest hashes.
- Link Build Artifacts to Release Artifacts.

Exit criteria: Acceptance Criteria 31 through 34 and 44 pass with reproducible artifact metadata.

## Milestone 7: Deployment Management

- Implement the one-Project-to-one-DeploymentTarget model.
- Implement local deployment to `install_path` with verification.
- Implement server deployment through KelpieSSH Streamable HTTP without storing SSH secrets.
- Implement start/get/list/status/retry/rollback and rollback-failed history.
- Support Build-to-Deploy and Build-to-Release-to-Deploy flows.

Exit criteria: Acceptance Criteria 35 through 43 and 45 pass, including failed verification and rollback scenarios.

## Milestone 8: v1 Conformance and Distribution

- Build a traceability matrix for all 45 Acceptance Criteria.
- Add CLI/MCP parity, standard response/error, authorization, idempotency, and recovery tests.
- Run migration tests from the released v1.0.3 database.
- Perform Release build, full automated tests, MSI upgrade/install/uninstall, and installed-machine smoke tests.
- Update all public documentation and publish only after explicit release approval.

Exit criteria: every v1 Acceptance Criterion has passing evidence or an explicitly approved specification correction; no required item remains partial.

## Recommended Execution Order

Execute Milestones 1 through 8 in order. Milestones 5, 6, and 7 must not be considered complete on Provider delegation alone: their state, history, artifacts, retry behavior, and traceability must be persisted by Moyai.
