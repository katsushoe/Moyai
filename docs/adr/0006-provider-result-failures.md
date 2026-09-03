# ADR 0006: Propagate repository business failures

## Status

Accepted for implementation. Installed services are unchanged until separately deployed.

## Context

MCP `isError` describes tool execution, but Githubie/Buckettie also return an application envelope with `ok`. Treating an unset/false `isError` as sufficient success hid `ok:false` in RepositoryProviderResult.Output. The CLI likewise returned zero for business failure JSON.

## Decision

- Repository responses prefer structured content, with the first text content as the legacy JSON fallback. Explicit `ok:false` in either representation is a failure even when MCP `isError` is false or absent; MCP `isError:true` always fails.
- Repository operations require a boolean top-level `ok`. Missing/invalid JSON, null results, absent/non-boolean `ok` produce `provider_invalid_response`. Provider version/capability metadata may omit `ok` for compatibility, but explicit false still fails.
- When both structured and text content are supplied, both must be valid JSON with a boolean `ok` if present. Structured data is the success output; a failing representation supplies failure detail. No recursive search of unrelated nested data is performed.
- Preserve the existing RepositoryProviderResult fields. Business failures set Ok=false, Output=null, normalized ErrorCode, and the original failing JSON in ErrorMessage (including original code, retryability and correlation fields). `repository_not_found` maps to `provider_not_found`.
- The CLI writes success JSON to stdout with exit 0. MCP failures, top-level `ok:false`, invalid JSON or a non-boolean `ok` produce the existing structured stderr envelope and exit 1. Error.message retains the failing payload. CLI arrays, scalars, null and objects without `ok` remain valid for existing non-envelope commands.

## Alternatives

- MCP flag only: rejected because it loses Provider business failures.
- Require `ok` on every CLI result: rejected because existing project lists, records and version results have no such field.
- Add new shared transport dependencies to Application: rejected; response decoding stays in Infrastructure and CLI at their respective boundaries.

## Impact, security and operations

No input/configuration, token/scope, provider routing, retry, repository mutation, database schema or release behavior is added. Failures do not trigger automatic retries or fallback providers. CLI consumers must handle exit 1 for business failures previously misreported as success. Raw response details remain in returned errors, not new logs. Existing installed services are not updated by source changes.

## Implementation, tests and documentation

RepositoryProviderResponse and CliResponse enforce the contracts; the actual adapters/CLI entry point call them. Regression tests cover transport flags, business flags, structured/text results, malformed/absent results, error normalization and non-envelope compatibility. Provider transport integration uses a fake HTTP handler, never a real repository mutation. COMMANDS.md and COMMANDS.ja.md summarize the public behavior.
