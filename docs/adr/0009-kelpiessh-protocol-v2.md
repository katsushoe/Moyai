# ADR 0009: KelpieSSH Protocol v2 staged deployment

## Status

Implemented locally on 2026-09-07. Provider integration and isolated end-to-end verification remain pending until KelpieSSH implements the shared validator and persistent deployment status contract. Installation and publication require separate approval.

## Context

Moyai previously routed server deployment through single `server_deploy` and `server_deploy_rollback` calls and attached a stored service token. KelpieSSH exposes staged deployment operations, while Provider authentication requires every protected call to bind a Project UUID and an immutable target ID. A transport failure after a mutation can leave its result unknown, so blindly repeating the mutation is unsafe.

## Decision

The internal lifecycle routing name remains `server`, with `kelpiessh` accepted as the registered Provider name. Assertions use canonical audience `kelpiessh`, `protocol_version=2`, `resource_kind=kelpie_target`, and the deployment target's immutable `kelpieTarget` value as `resource`. The legacy service token is not resolved or transmitted for server deployment.

Deployment executes `target_status`, `deploy_prepare`, `deploy_upload`, `deploy_activate`, `deploy_verify`, and `deploy_cleanup` in order. Rollback executes `deploy_rollback` against the original deployment ID and then `deploy_cleanup`. Each Tool call creates a bootstrap connection without Authorization and sends a newly issued assertion only on `tools/call`.

Only `auth_assertion_expired`, which guarantees that the Tool did not execute, may be retried once with a new assertion. HTTP, timeout, or MCP transport failures after a mutation trigger one read-only `deploy_status` reconciliation. Moyai never automatically repeats a mutation with an unknown outcome. The Provider must persist deployment ID, Project UUID, target ID, input hash, and state across restarts, make identical input idempotent, and reject conflicting input.

## Alternatives

- Continue the single-call Adapter: rejected because it cannot enforce per-stage scope or reconcile unknown outcomes.
- Reuse one assertion or attach it during MCP initialization: rejected because it crosses the bootstrap boundary and permits replay.
- Retry all failed mutations: rejected because transport failure does not prove that the Provider skipped execution.

## Security and operational impact

The fixed scope mapping is `target.status`, `deploy.prepare`, `deploy.upload`, `deploy.activate`, `deploy.verify`, `deploy.rollback`, `deploy.cleanup`, and `deploy.status`. Artifact upload requires a file artifact and a SHA-256 value. Audit records retain the existing schema by storing Protocol v2 resource context as `resource_kind:resource`; JWT values are never persisted.

Unit and transport tests cover shared-package interoperability, stage order, bootstrap isolation, per-call assertion context, known failure handling, unknown-result reconciliation, routing aliases, service-token omission, and rollback identity. Real KelpieSSH compatibility, process-restart recovery, restricted SSH execution, and trust/replay separation require the Provider-side change before rollout.
