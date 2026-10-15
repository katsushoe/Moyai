# ADR 0010: Path-selective Repository Provider stash

## Status

Accepted

## Context

Moyai delegates local Git operations through registered Repository Providers. Stash operations need the same project routing, authentication, assertion, and audit controls as other repository mutations.

## Decision

Add typed stash push/list/apply/drop provider operations. Push passes an optional array of repository-relative paths unchanged; omission means all changes. Apply/drop pass a full stash object ID. Listing uses `repository.read`; mutations use `repository.stash.write`. Before dispatch, Moyai queries provider capabilities and fails closed with `provider_capability_missing` if the corresponding operation is not explicitly advertised.

## Consequences

Older providers remain usable for existing operations but do not receive unsupported stash calls. Provider arguments preserve path arrays and IDs without shell or pathspec construction in Moyai; each provider validates paths in its own Git gateway.
