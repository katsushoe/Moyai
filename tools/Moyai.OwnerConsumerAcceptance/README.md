# Owner Consumer acceptance console

This Windows console exercises Moyai's actual Project/Application routing using a new private SQLite DB. It does not start a host, open a control pipe, resolve signing services, register a Provider Repository or change normal configuration. Buckettie owns its dedicated Repository, approvals, profile and lifecycle.

Build this project, then execute its DLL with one of the following argument sets:

```
prepare <new-absolute-consumer-profile> <provider-repository-id> <absolute-fixture-source> <repository-url>
read <consumer-profile> <absolute-provider-ready-file>
case <consumer-profile> <absolute-provider-ready-file> <absolute-case-file>
verify <consumer-profile> <case-id> <absolute-independent-observation-file>
```

Obtain the Repository identity from its owner before `prepare`. Preparation creates owner/SYSTEM-only directory ACLs, `fixture.db` and `fixture-plan.json`. Return its public Project UUID to Buckettie. Do not place tokens or discovery credentials in those public records.

The owner-provided ready JSON must contain `ready=true`, `provider_id="buckettie"`, version at least2.0.1.0, `authentication_mode="windows_owner"`, numeric `owner_protocol_version=1`, matching `project_uuid` and `repository_id`, absolute `runtime`, its `profile_id`, and `generation` as an object with positive numeric `process_id` and `process_start_utc_ticks`. Require `ready_at_utc` and `expires_at_utc`: the selected window must be active with more than65 seconds remaining for the console's60-second timeout. This is a new generation handshake; old signed ready records are invalid. The resolver independently checks owner/session/ACL/process and the pinned generation before HTTP. Each connection checks live version, contract and operation capabilities again.

`read` invokes version, capabilities, status and diff through ProviderRoutingService, stopping at the first failure. Output includes stable result codes and outcomes, not raw Provider output or credentials. A successful read does not establish mutation acceptance.

`case` requires its `caseId` in ready's `approved_cases` array and its exact file SHA256 in ready's `approved_case_sha256` object, keyed by that ID. Agree the case JSON/hash with the Provider owner. Mutation cases require the human owner's approval of the concrete changes; readiness for reads does not approve mutations. Push refusal requires `expectedErrorCode="provider_policy_rejected"` for Moyai's common result and `expectedProviderErrorCode="protected_branch"` for the structured Provider detail in ErrorMessage. Both must match; another policy rejection or missing/unstructured detail fails. Only the stable matched Provider code is saved, never raw error detail. Preserve earlier frozen candidates and failed evidence; a revised candidate does not clear an earlier pending case or authorize replay.

Case JSON uses exact camelCase property names; unknown properties fail parsing. `FixtureCase` has:

| Property | Requirement |
|---|---|
| `caseId`, `operation`, `expectedOk`, `expectedErrorCode` | Exact agreed identity/operation/result; only SettingsGet, Commit, CommitSelected, protected-branch Push refusal and existing BranchSwitch are supported |
| `expectedSettings` | SettingsGet: the nine-field object below; successful result and null expected error required |
| `message`, `patch` | Commit message; selected commit additionally requires its agreed patch |
| `branch` | Existing BranchSwitch target, equal to expectedBranch |
| `expectedHead` | Full object ID: parent/precondition for commits; final observed HEAD for Push refusal/BranchSwitch |
| `expectedBranch`, `expectedClean` | Explicit postcondition for every mutation |
| `expectedChangedPaths` | Exact committed paths for Commit/CommitSelected |
| `expectedUnchangedRefs` | Complete map of other refs and expected IDs; the active expected branch ref is derived from the returned commit or expectedHead |
| `expectedFileSha256` | Expected postcondition hashes for the named fixture files |

`FixtureSettings` is an acceptance DTO for Buckettie's exact nine public fields: direct_push_branches, protected_branches, pull_branches, tag_pattern, tag_target_branch, require_clean_working_tree, commit_author_name, commit_author_email, pull_request_routes (array of source/destination pairs). Missing/wrong-type/extra fields and duplicate JSON names fail validation. Null author values remain distinct from empty strings; array order is compared. SettingsGet compares all nine values and reports only fixed mismatched field names, never raw Provider output. Repository path/remote/HEAD/Project UUID are separate registration evidence.

Every case, including SettingsGet, reserves a unique marker and creates `mutation-pending.json` before dispatch. A failed comparison, unknown result or crash keeps the global pending record and blocks all later cases. SettingsGet clears pending only after all nine comparisons succeed. Mutation responses remain pending even when the expected code matches: exit2 means independent verification is required, not acceptance or permission to resend. Commit success must contain a full `data.commit_hash` from the Provider result; do not substitute expectedHead for a missing return value. Per-case reservations are never removed.

The Provider owner reconciles actual Git state and supplies `FixtureObservation` JSON with `schemaVersion=1`, `caseId`, `projectId`, `repositoryId`, `observedAtUtc` (after the saved response), `branch`, full `head`, nullable `parentHead`, explicit `clean`, `changedPaths`, `commitCount` (new commits created by this case), `networkOperations` (Git/remote activity), complete `refs`, and named `fileSha256`. For a commit, the independent HEAD must equal the returned SHA, parent must equal expectedHead, one commit must contain exactly expectedChangedPaths, other refs and file hashes must match, and networkOperations must be0. Push refusal/branch switch require no new commits, the agreed final HEAD/state and zero network operations. This is owner-exported evidence; Moyai does not run raw Git or modify the Provider fixture to create it.

`verify` reads only the dedicated DB/journal and the independent observation; it never contacts a Provider. Wrong identity, stale observation, mismatched state and unknown outcomes retain pending. A fully matched verification writes its safe result before clearing pending. Exit0 means acceptance passed; exit1 means refusal/mismatch; exit2 is returned only by `case` while post-verification is pending. Do not remove a reservation or pending record merely to retry; unknown/crash recovery requires explicit owner reconciliation outside this verification shortcut.

Provider owners verify actual HEAD/diff/audit and preserved unselected content after local commits. Remote PR/release success needs separately approved dedicated remote fixtures and is not implemented by this console's initial case set. Unit MCP simulations establish Consumer behavior only. Keep the original CR open until its agreed gates pass, then invalidate ready and have the Provider owner stop its own fixture.
