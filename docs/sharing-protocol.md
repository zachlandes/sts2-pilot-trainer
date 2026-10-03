# Sharing protocol v2

This is the Phase 1 contract, exercised through `DeterministicRunSharingServer` and `RunSharingProtocolTests` without a service or game process.
It extends `IRunSharingApi`; it does not implement hosting, verified Steam authentication, persistence, a queue, or remote replay.
The fake's publication predicate proves state transitions only, not the publication standard.
Cloudflare deployment, engine-hosting rights, authentication integration, and resource sizing remain conditional on Phase 0.
No endpoint, credential, account, or automatic upload is added.

## Wire contract

All resources remain under the profile-authorized service URL.
The configured retail client's HTTPS, scope checks, and no-redirect handler remain unchanged.
Requests and responses use JSON with a maximum depth of 64 and reject unknown envelope fields.
Branch, publication-state, and error enums are strings; integer and unknown enum values are refused.
Canonical replay interpretation and identity still belong to `ManifestJson` and `SharedRunIdentity`.

- `GET sharing-protocol`: `SharingProtocol` with minimum/maximum protocol versions and the request-byte ceiling.
- `GET sharing-policy`: exactly one `BranchReadiness` each for Public and Beta.
- `POST runs`: `AdmissionRequest` containing the existing manifest/submission payload, branch, and policy generation.
  A newly admitted request returns HTTP 202 and `SubmissionStatus` in Processing.
  An identical previously completed submission can return its existing terminal status with HTTP 200.
- `GET submissions/{receiptId}`: private status, authenticated as the accepting principal.
- `GET submissions/by-identity/{shareId}`: the same private status, allowing recovery when the acceptance response was lost.
- `GET runs?cursor=…`: a `SharedRunPage`, ordered featured first, then newest submitted time, then full identity.
  The cursor is opaque and belongs to one catalogue revision.
  A changed revision refuses a stale continuation rather than skipping rows.
- `GET runs/{code}`: the existing complete `SharedRun` or HTTP 404.
  Codes are trimmed, case-insensitive twelve-character hexadecimal values and are verified against the downloaded identity.

V2 operations send `Runmobile-Sharing-Version: 2`.
Negotiation must succeed before submission; a service requiring another version refuses before the manifest POST.
An asynchronous-only service must return HTTP 426 to a legacy request before accepting it.
The existing synchronous client method also refuses HTTP 202 rather than treating Processing as a `SharedRun`.
The retail submission surface is not converted to an asynchronous UI in Phase 1; that is Phase 3 work.

The hard request and response limits are 4 MiB each, including the JSON envelope.
The negotiated request limit can lower, never raise, that client ceiling.
Responses are streamed with a byte counter even when Content-Length is absent.
Encoded bodies are not part of this contract; a future compression contract needs an independently enforced decoded-size limit.
Header acquisition and response-body reading each have a ten-second deadline and honor cancellation.
A page has at most 50 rows, a cursor at most 256 characters, and an ETag at most 128 characters.
Name, description, and display-name bounds are 40, 200, and 80 Unicode scalar values.
These are protocol safety bounds, not measurements of cloud capacity or full-run payload size.

A page ETag is scoped to its revision and cursor.
Clients may send If-None-Match and receive HTTP 304 with the same ETag.
`NotModified` means retain the cached page, not replace it with an empty catalogue.
Publication, removal, and a change to the compatibility projection change the revision; exact-code retrieval observes the same visibility policy.
HTTP 401 identifies a missing/expired session, HTTP 403 identifies forbidden access, and another principal's private status returns HTTP 404.
Authentication mechanism and credential acquisition are deliberately unspecified until Phase 0 establishes them.
The fake's fixed session labels and predictable receipt ids are not production authentication or capability tokens.

## Acceptance and completion

`SubmitForPublicationAsync` accepts the existing local publication gate as a callback.
It checks branch readiness before that gate and again before upload; it neither replaces the gate nor trusts its result as remote publication evidence.
The future service must check admission and reserve identity/code/job in one transaction.
Beginning an update advances only the affected branch's generation and closes its admission before preparing any new engine inputs.
The final transactional check decides the race: a losing request creates no receipt, identity reservation, or accepted job.
It says: “Sharing is paused while Runmobile catches up with the latest game update. This run wasn't shared.”
There is no automatic resubmission when admission reopens.

Admission requires fresh upstream observations, an exact mapped `LocalBuild`, a ready validator, an open branch, and the expected generation.
The existing `EnvironmentPreflight.Build` compares version, UTC build date, and content hash; an old recording is not upgraded by updating the client.
Readiness says nothing about the installed recorder's support and cannot stop an otherwise supported local recorder.
The caller supplies the recording's branch; Phase 1 does not retrofit branch provenance into existing recordings or resolve Steam branch metadata.

The full server-computed canonical identity owns idempotency; Idempotency-Key is advisory, not identity authority.
Changing hashed public text produces a new identity.
A short-code collision refuses the second reservation and never replaces the first.
Identity golden vectors include ordinary and Unicode public text and deliberately fail if the canonicalization owner changes.
A future canonicalization change must address these protocol identities before deployment, not silently update the vectors.

An accepted receipt pins its branch generation, exact engine, acceptance time, and expiry.
A duplicate of an admitted identity returns that original receipt, with its original branch and generation, even when the duplicate names the other branch.
The client still requires the receipt's identity and pinned engine to match the recording it submitted.
Processing exposes no published run or public code promise, and appears in neither discovery nor exact-code lookup.
`RunBrowser.Publication` derives its explanation and exposes a code only for Published.
Refused, Failed, and Expired are terminal private outcomes with explanations.
Only the future independently executed real publication gate may authorize Published; the fake substitutes a predicate solely to exercise this contract.

`PublicationAttempt` rejects a completion or retry from an older attempt generation and refuses changes after a terminal outcome.
Its caller must serialize the compare-and-set and catalogue visibility change in one transaction; the fake does so under one lock.
There are at most three attempts and a one-hour processing/drain deadline from acceptance, with explicit Failed or Expired outcomes rather than an indefinitely pending queue.
Jobs accepted before an update can finish under their pinned engine and generation within that deadline.
These bounds do not promise a publication latency or establish a production retry schedule.

A request timeout or lost response means outcome unknown, not refusal or publication.
The client performs no implicit retry.
The accepting principal can query the same canonical identity even while admission is closed, or retry the same submission when admission permits; neither creates another accepted identity.
Overload retry advice is limited to 1–60 seconds and does not trigger an automatic request.

Patch-day admission and existing publication are separate facts.
An existing published code still retrieves the same immutable recording after an update, while its build-specific compatibility projection may have no passing verdict.
The existing browser then hides it by default and exact-code lookup selects it as a refused row.
No manifest is rewritten, no alternate replay path is added, and no passing compatibility verdict is invented.

## Reproduce the contract evidence

```bash
./scripts/test-session.sh tests/Sts2PilotTrainer.Trainer.Tests --filter 'FullyQualifiedName~RunSharing' --nologo
```

The suite exercises both publication and refusal controls, private status, lost acceptance responses, canonical duplicates, concurrent requests, code collisions, branch-update interleavings, pinned draining, stale completion, terminal expiry, exact-code compatibility, revision-scoped pages, conditional reads, malformed data, bounded streaming, deadlines, and incompatible/legacy clients.
Passing it is evidence about the C# protocol and deterministic fake, not persistence durability, Steam proof verification, Cloudflare execution, or real-engine publication.
The next stages remain local persistence/worker implementation and integration into existing client surfaces after the Phase 0 findings permit them.
Any next action requiring an account, terms, credentials, Steam or Cloudflare authentication, spending, access grants, or real uploads must stop for authorization with the action, reason, scope/cost, and safe alternatives recorded first.
