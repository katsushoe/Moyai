# PR source publication

[日本語](pr-source-publication.ja.md)

Development candidate2.0.4.0 implements the agreed Githubie Consumer contract; it is not installed or publicly released. See [ADR0026](adr/0026-pr-source-publication-consumer.md).

Select the registered Project with list_projects. Dedicated MCP pull_request_publish_create requires project, sourceBranch, destinationBranch, expectedSourceSha (40 hex), title, operationId (D-format UUID), and an explicit draft boolean. Optional description defaults to empty. Title accepts1..256 non-control characters excluding whitespace-only; description is at most65536 characters. Source/destination are distinct literal local branch names; no HEAD, revisions, full refs or refspecs. Branch case is significant. UUID and SHA are lower-cased.

CLI uses the same service and schema:
```text
moyaictl pull-request-publish-create --project <registered-name> --source-branch <local-branch> --destination-branch <target> --expected-source-sha <40-hex> --title <text> --operation-id <D-UUID> --draft false [--description <text>]
moyaictl pull-request-publish-status --project <registered-name> --operation-id <same-D-UUID>
```

Keep the original UUID across disconnects/cancellation. Never generate a replacement to bypass an unresolved operation. Status is read-only reconciliation: it cannot create a ref/PR. Existing pull_request_create does not publish branches.

Creation requires both explicit publication capability flags and tools. Windows owner mode uses the verified owner session without signatures; assertion mode requires repository.push plus provider.githubie.pull_request.write. Status requires its own capability and repository.read in assertion mode. Unsupported Providers, legacy tokens and absent capabilities fail before dispatch. Provider Desktop approval, protected-source rejection, exact PR route, clean-tree policy and immutable creation-only remote ref checks still apply. Ordinary direct-push permission is not widened.

The JSON envelope separates request_context from measured Provider data. Both publication_state and pull_request_state use not_attempted/created/already_present/failed/unknown. already_present does not mean this request created the object. Failed calls preserve validated data, PR number/URL/head SHA, observed remote SHA/time and stage. Unavailable/unverified data is null with provider_data_available=false. Unknown responses have requires_reconciliation=true, retryable=false and check_status; do not infer success or resend create. A failure returns nonzero CLI exit with the same JSON. Do not delete a published branch to roll back an unknown PR result.

Local mock tests and candidate hashes do not prove live GitHub/Provider/native-client acceptance. Dedicated fixture, matching Provider/candidate pins, current authorization, concrete approved changes and recovery observations must be agreed before that acceptance.

