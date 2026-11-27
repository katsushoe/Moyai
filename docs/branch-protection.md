# Hosting branch protection

[English](branch-protection.md) | [日本語](branch-protection.ja.md)

Development candidate2.0.3.0 adds MCP and service-connected CLI operations for Githubie's classic exact-branch protection contract. Use a registered Moyai Project; run list_projects before selecting it. The registered Provider repository mapping determines the target. Installed Provider support, real GitHub permission/plan checks, Desktop approval interaction and actual interoperability are separate acceptance work.

| MCP | CLI | Inputs |
| --- | --- | --- |
| branch_protection_get | moyaictl branch-protection-get | --project Example --branch main |
| branch_protection_update | moyaictl branch-protection-update | --project Example --branch main --edit '{"enforce_admins":true}' |
| branch_protection_delete | moyaictl branch-protection-delete | --project Example --branch main |

Examples are input syntax, not authorization to operate on a real repository. Changes require the specific user's instruction and Githubie's Desktop approval. No auto-approval flag is exposed. For possible creation, explicitly pass `--create-if-missing true`; it does not authorize overwriting an unsupported or inaccessible policy. Githubie observes the branch and decides whether creation is safe.

Edit fields use snake_case: require_pull_request, required_approving_reviews (0–6), allow_force_pushes, allow_deletions, enforce_admins, require_status_checks, status_check_contexts and strict_status_checks. Omitted/null fields preserve existing values; false and0 remain explicit changes. Unknown properties, an empty edit, invalid/wildcard branch names, contradictory options and duplicate/empty/control-character check names are refused. Enabling checks requires an explicit contexts array; disabling reviews/checks cannot simultaneously edit their dependent fields. Creation requires explicit review/approval-count, force, deletion, admin and check decisions in the Provider's final policy. Context replacement cannot silently discard existing GitHub App bindings.

The common result contains ok/operation/project/provider/protection/error and protectionKind=classic_exact_branch. Read protection has branch/canUpdate and a common policy projection. Unknown or unrepresentable settings may return canUpdate=false and policy=null. Mutations return branch/changed/policy; a successful no-op has changed=false and does not establish that this caller changed the remote state. There is no fabricated approved flag. The Provider's normalized full policy, raw API diagnostics, principal lists and credentials are not passed through. Do not use the common read projection as a complete replacement policy.

Explicit branch_protection_read/write capabilities are required. Owner mode also checks owner operations and tools/list; assertion mode requires repository.read for get or provider.githubie.branch_protection.write for update/delete, with registered Project binding. Legacy service tokens and general repository.write cannot substitute. Existing normal settings/scopes/grants are not updated automatically. Unsupported Providers, including Buckettie, advertise false and reject operation dispatch. A Provider upgrade alone does not authorize use.

Githubie owns before/after Desktop approval, supported unrelated-policy preservation, authenticated second-read conflict detection and GitHub Administration/plan permission checks. A403 is branch_protection_access_denied; check both permissions and plan availability. A404 is branch_protection_not_found_or_inaccessible and does not prove that the branch is unprotected. Unknown/unrepresentable security settings are refused before mutation. GitHub REST does not supply an atomic update guarantee between the second read and write.

Moyai writes a durable intent before requesting changes and a final outcome afterward in repository_operation_audit (schema8). It stores operation ID, Project UUID, canonical Provider/operation, branch SHA256, safe outcome/code and UTC time, not policy/request bodies or credentials. Pending intent alone proves neither execution nor non-execution. If intent persistence fails, nothing is dispatched. If final audit persistence fails, operation_audit_result_unknown requires independent observation; do not resend just to repair the audit record.

Update/delete are never automatically retried, including on assertion expiry. Transport loss, malformed/contradictory result channels and uncertain mutation results return non-success with outcome=unknown/retryable=false; independently use get before deciding on any later user-authorized action. Pending is also not success. Errors retain safe Provider code/outcome/suggestedAction/correlation fields, excluding raw messages. Business failures use CLI exit1. A future separate command is a new action, not an automatic replay.

Githubie's protected_branches is its gateway push policy; hosting classic protection and GitHub rulesets are separate. Deleting a classic rule does not remove a ruleset or change the Provider push policy. This local implementation changes no actual host, grant, branch protection, rule, repository, MSI or release. Design: [ADR0025](adr/0025-hosting-branch-protection.md).
