# Isolated response-loss acceptance

This console is a dedicated acceptance harness. It does not alter the installed host or normal configuration.

## Dispatch

`Moyai.OwnerConsumerAcceptance loss-case <absolute-private-profile> <absolute-fresh-ready-json> <absolute-case-json>`

The pre-existing fixture plan/database identity must match the ready. The ready must authorize the exact case ID and SHA. The case must use `Commit` and an `owner-response-loss-` ID. The caller and Provider owner must also verify the candidate manifest and obtain human authorization for that exact new mutation before invoking this command.

The harness reserves the case and creates global pending before dispatch. Only the selected local commit tool is allowed as a mutation. Bootstrap and metadata reads pass normally. One mutation is forwarded; any further attempt is blocked. A successful HTTP response is completely buffered (maximum 1 MiB), disposed and replaced by a simulated transport failure. HTTP errors are returned normally. Neither response payload nor credentials are logged. Dispatch count proves forwarding; discarded-response count proves that the complete HTTP response was withheld, not that the underlying commit succeeded.

Expected result: `ResultUnknown=true`, operation `Ok=false`, mutationDispatches1, responsesDiscarded1, verificationPending=true, console exit2. The original evidence and global pending remain. An unexpected result is a failed test, with pending retained. Never rerun the same case to recover.

## Independent recovery

`Moyai.OwnerConsumerAcceptance verify-loss <absolute-private-profile> <case-id> <absolute-observation-json>`

After the Provider owner independently checks the actual repository, this offline command compares the observation against the preserved case. Required observations include schemaVersion1, matching case/project/repository, timestamp after completion, new HEAD and exact parent, exact unique commit message (`message`), exactly one commit, expected branch, clean state, changed paths, all refs, file hashes and zero network operations. The observed HEAD is independent evidence, never represented as a returned commit SHA. Normal `verify` refuses unknown outcomes.

Only a passing independent reconciliation clears global pending. The original unknown outcome, case reservation and result remain preserved. If the operation did not happen or its state differs, do not clear pending, replay or discard files; agree a separate evidence-based recovery procedure. This acceptance does not prove OS/network packet loss, another-user isolation or remote operation success.
