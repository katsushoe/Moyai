# Moyai Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

This document is the public command contract for `Moyai.Cli.exe`. MCP tools use the same operation names with underscores.

## Command Groups

| Group | Commands | Purpose |
| :--- | :--- | :--- |
| Project | `project-list`, `project-get`, `project-create`, `project-update`, `project-set-archived`, `project-overview`, `project-changes-since` | Project state and aggregate views |
| Work item | `work-item-list`, `work-item-get`, `work-item-create`, `work-item-update`, `work-item-set-deleted`, `work-item-transition`, `work-item-history`, `item-search` | Work tracking, history, and FTS5 search |
| Collaboration | `relation-add/remove/list`, `comment-add/list`, `task-link-add/remove/list`, `commit-link-add/remove/list` | Persistent WorkItem collaboration records |
| Repository | `repository-status`, `repository-diff`, `repository-commit`, `repository-push`, `repository-pull` | Provider-routed Git operations |
| Token | `token-issue`, `token-rotate`, `token-revoke`, `token-cleanup` | Internal service authentication |
| Release | `release-create/get/list/update/transition`, `release-add/remove/list-items`, `release-add/remove/list-artifacts`, `release-prepare/mark-ready/publish/retry/withdraw`, `release-latest/overview` | Release state, contents, and publishing |
| Build | `build`, `build-start`, `build-get`, `build-list`, `build-artifacts`, `build-clean` | Tracked build execution and artifacts |
| Deployment | `deployment-target-get/update`, `deploy/start/get/list/status/retry/rollback` | Tracked Local and KelpieSSH deployment |

## Common Options

Options use `--kebab-case`. Mutations require `--actor-type` and `--actor-name`; concurrency-protected mutations also require `--expected-revision`. Success is JSON on standard output with exit code `0`. Failure is JSON on standard error with exit code `1`, containing `command`, `summary`, `ok`, `fatal`, and `error`.

## Commands

Project name lookup, duplicate registration checks, and update targeting use ordinal case-insensitive comparison. The originally registered name casing remains canonical unless `project-update` explicitly supplies a new name.

Each command below has the syntax, processing rule, and result contract. Returned Project or WorkItem fields reflect persisted SQLite state; list filters change inclusion, and mutation results contain the new `revision`.

### Project commands

- `project-list`: `project-list [--include-archived]`; returns all non-archived projects unless the flag is present. Example: `Moyai.Cli.exe project-list`.
- `project-get`: `project-get --name <name>`; returns the persisted project or an error when absent. Example: `Moyai.Cli.exe project-get --name Sample`.
- `project-create`: requires `--name --source-path --repository-url --build-provider --deploy-mode --actor-type --actor-name`; optional `--install-path --repository-provider`; creates both the Project and its one Repository association. Names must be unique.
- `project-update`: requires `--current-name --name --git-remote-name --expected-revision --actor-type --actor-name`; optional `--repository-url --repository-provider --description --build-config-json --git-user-name --git-user-email --git-default-branch`; updates and returns the project, rejecting stale revisions. A supplied URL with no Provider re-runs Provider inference; a supplied Provider changes routing explicitly.
- `project-set-archived`: requires `--name --expected-revision --archived --actor-type --actor-name`; archives or restores and returns the project.
- `project-overview`: requires `--project`; optional `--recent-limit` defaults to `10` and must be `1..100`; returns open WorkItem counts, blockers, latest stable release, planned release, and recent events.
- `project-changes-since`: requires `--project --since`; optional `--offset --limit` default to `0` and `50`; returns events strictly after the ISO 8601 timestamp in deterministic chronological order.

### Work item commands

- `work-item-list`: `work-item-list --project <name> [--include-deleted]`; returns persisted items, excluding deleted items by default.
- `work-item-get`: requires `--project --key`; optional `--include-deleted`; returns one item or an error.
- `work-item-create`: requires `--project --type --title --actor-type --actor-name`; `type` is `Issue`, `Bug`, `ChangeRequest`, `Feature`, `Risk`, or `Decision`; creates an atomic project/type key and returns the item.
- `work-item-update`: requires `--project --key --title --priority --expected-revision --actor-type --actor-name`; optional `--description --severity --owner --metadata-json`; priority is `Low`, `Medium`, `High`, or `Critical`; severity is `Minor`, `Major`, or `Critical`; returns the updated item.
- `work-item-set-deleted`: requires `--project --key --expected-revision --deleted --actor-type --actor-name`; soft-deletes or restores and returns the item.
- `work-item-transition`: requires `--project --key --next-status --expected-revision --actor-type --actor-name`; applies the workflow for the item type and returns the item.
- `work-item-history`: requires `--project --key`; returns append-only audit events for the item and its collaboration records.
- `item-search`: requires `--project --query`; optional `--type --status --priority --owner --created-after --updated-after --offset --limit`; searches title, description, and Comment body through FTS5. Deleted WorkItems are excluded. Pagination defaults to offset `0`, limit `50`, with a maximum limit of `100`.

### Collaboration commands

- `relation-add`: requires `--project --source-key --target-key --relation --actor-type --actor-name`; returns the relation and warnings. Supported relations are `relates_to`, `depends_on`, `blocks`, `duplicates`, `caused_by`, `implements`, and `supersedes`. A `depends_on`/`blocks` cycle is saved with `relation_cycle_detected`; reverse `relates_to` duplicates are rejected.
- `relation-remove`: requires `--project --relation-id --actor-type --actor-name`; returns whether a relation was removed. `relation-list` requires `--project --key`.
- `comment-add`: requires `--project --key --body --actor-type --actor-name`; appends an immutable comment. `comment-list` requires `--project --key`.
- `task-link-add`: requires `--project --key --task-system --task-id --relation --actor-type --actor-name`; `hataori` is the standard task system. Remove uses `--project --link-id --actor-type --actor-name`; list uses `--project --key`.
- `commit-link-add`: requires `--project --key --commit-hash --relation --actor-type --actor-name`; relation is `implements`, `fixes`, or `relates_to`. Remove uses `--project --link-id --actor-type --actor-name`; list uses `--project --key`.

### Repository commands

- `repository-status`: `repository-status --project <name>`; returns Provider status.
- `repository-diff`: `repository-diff --project <name>`; returns Provider diff.
- `repository-commit`: requires `--project --message`; creates a Provider commit and returns Provider data.
- `repository-push`: `repository-push --project <name>`; publishes allowed commits and returns Provider data.
- `repository-pull`: `repository-pull --project <name>`; performs the Provider's allowed pull and returns Provider data.

### Token commands

- `token-issue`: requires `--audience --scopes --actor-type --actor-name`; optional `--expires-at`; scopes are comma-separated; returns the newly issued secret once.
- `token-rotate`: same arguments as issue; invalidates the prior audience token and returns the replacement once.
- `token-revoke`: requires `--audience --actor-type --actor-name`; revokes the audience token and returns the lifecycle result.
- `token-cleanup`: requires `--actor-type --actor-name`; deletes expired tokens and returns the deleted count.

### Lifecycle commands

- `build` / `build-start`: require `--project --actor-type --actor-name`; optional `--configuration` defaults to `Release`. Repository Provider status supplies the source commit and dirty state; dirty standard builds are rejected before the configured Build Provider runs.
- `build-get`: requires `--project --build-id`. `build-list --project` returns newest first. `build-artifacts --project --build-id` returns immutable artifact metadata. `build-clean --project --actor-type --actor-name` invokes Provider cleanup while preserving Build and Artifact history.
- `release-create`: requires `--version --channel`; optional `--notes`; creates a draft release in Moyai.
- `release-get` / `release-list`: read Release state; `--include-deleted` includes soft-deleted rows.
- `release-update`: requires `--version --channel --expected-revision`; accepts `--tag-name --commit-hash --notes --planned-at`.
- `release-transition`: requires `--version --next-status --expected-revision` and follows the v1 Release workflow.
- `release-add-item`: requires `--project --version --work-item-key --relation --actor-type --actor-name`; relation is `includes`, `fixes`, `implements`, or `resolves`. Remove requires `--relation-id`; list requires `--project --version`.
- `release-add-artifact`: requires `--project --version --name --artifact-type --platform --architecture --file-name --actor-type --actor-name`; optional metadata is `--build-artifact-id --file-path --download-url --file-size --sha256 --signature-path --signature-url`. Remove requires `--artifact-id`; list requires `--project --version`. File content is not stored.
- `release-prepare` / `release-mark-ready`: require `--project --version --expected-revision --actor-type --actor-name` and move `planned -> preparing -> ready`.
- `release-publish`: requires the same options and explicit approval; persists `publishing` before calling the Provider, then records `released` or `failed`. Repeating an already released version is idempotent and does not call the Provider.
- `release-retry`: requires the same options and explicit approval; moves `failed -> ready` and retries publish. `release-withdraw` withdraws a released version through the Provider and is idempotent after completion.
- `release-latest --project` returns the latest released stable version by `released_at`. `release-overview --project --version` returns the Release, WorkItem relations, and artifact metadata.
- `release-publish`: requires `--project --version --actor-type --actor-name`; publishes an existing release.
- `release-withdraw`: requires the same options; withdraws an existing release.
- `deployment-target-get`: requires `--project`. `deployment-target-update` requires `--project --name --mode --destination-path --expected-revision --actor-type --actor-name`; use revision `0` for first creation. Server mode also requires `--kelpie-target`, and optional `--config-json` never contains credentials.
- `deploy` / `deploy-start`: require `--project --build-id --artifact-id --actor-type --actor-name`; optional `--version` links a Release. Only a succeeded managed Build and its verified immutable Artifact are accepted.
- `deploy-get`, `deploy-status`: require `--project --deployment-id`; `deploy-list` requires `--project`. `deploy-retry` additionally requires `--artifact-id --actor-type --actor-name`. `deploy-rollback` requires actor options and records `rollback_failed` rather than hiding failure.

## Safety Notes

Review Provider targets before commit, push, pull, build, release, or deploy. `release-publish` and `deploy` require explicit approval for the exact target. Never place returned token secrets in logs, source, or documentation.

Moyai v1 models exactly one Repository as part of each Project. It has no independent `repository-register` or `repository-unregister` command; archive the Project to stop using the association.
