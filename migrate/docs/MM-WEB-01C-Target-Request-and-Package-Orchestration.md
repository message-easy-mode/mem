# MM-WEB-01C — Target Request Intake and Package Orchestration

## Purpose

MM-WEB-01C connects the Source Assistant assessment and stack inventory to the
existing MEM Migrate capture and package-for-intake engines.

The operator can now:

1. select the intended MEM 0.1.0 stack;
2. acknowledge Matrix encrypted-message recovery readiness;
3. import a versioned target migration request;
4. verify the target age-recipient fingerprint;
5. reuse an eligible plaintext capture or create a fresh preview capture;
6. encrypt the selected capture for the target secure intake;
7. recover durable workflow state after browser refresh.

Browser package download and local encrypted-package deletion remain in
MM-WEB-01D.

## Request contract

```json
{
  "schema": "mem-secure-intake-request",
  "schemaVersion": 1,
  "intakeId": "mig_...",
  "packageRevisionId": null,
  "requestKind": "preview",
  "ageRecipient": "age1...",
  "recipientFingerprint": "XXXX-XXXX-XXXX-XXXX",
  "expiresAtUtc": "2026-07-28T12:00:00Z",
  "targetControlPlaneVersion": "0.2.0",
  "sourceStackId": "optional-source-stack-guid"
}
```

The request contains public encryption material only. It never contains the
target age private identity or target operator credentials.

The Source Assistant rejects:

- unknown schema or schema version;
- unsupported request kind;
- invalid Intake or Package Revision IDs;
- invalid native age recipients;
- fingerprint mismatches;
- expired requests;
- a target-request stack ID that differs from the selected source stack.

## Encrypted-message readiness

Before importing a request, the operator must acknowledge that users were told:

- not to sign out of existing trusted Element sessions;
- to verify another device where possible;
- to confirm Secure Backup and its recovery secret work;
- to export room keys as an additional safeguard where appropriate;
- that password reset cannot recreate lost historical E2EE room keys.

The workflow records the acknowledgement time. It never records recovery keys,
security phrases, exported key files or user secrets.

## Durable workflow journal

MM-WEB-01C adds the additive source SQLite table:

```text
source_workflow_runs
```

and records the schema step in:

```text
journal_schema_migrations
```

The workflow stores:

- assessment and source fingerprint;
- selected source stack;
- target request identifiers and expiry;
- public age recipient and recipient fingerprint;
- encryption-readiness acknowledgement time;
- selected capture ID;
- package report;
- lifecycle status and current stage;
- safe failure and cancellation evidence.

A workflow left in `Running` when a new Source Assistant process starts is
classified as `Failed / RecoveryRequired`. The operator must inspect retained
artifacts before retrying.

## Operation model

Only one source-mutating operation can run in the Source Assistant process.

The browser starts operations and then polls durable state. Closing or refreshing
the browser does not cancel the backend operation.

Supported operation stages are:

```text
RequestValidated
CaptureSelected
Capturing
CaptureReady
Packaging
ReadyForDownload
RecoveryRequired
```

Capture cancellation uses the existing capture-service cleanup semantics.
Package cancellation uses the existing age-envelope partial-output cleanup.

## Capture policy

A preview request can:

- select an eligible retained plaintext capture; or
- create a fresh read-only rehearsal capture.

A final request cannot create a fresh capture from this screen. Final capture
requires the controlled source-freeze workflow and an eligible final frozen
capture.

Every selected capture must match the workflow source fingerprint.

The package remains installation-wide under the current archive contract.

## Security boundaries

The Web API does not return:

- source archive paths;
- encrypted package paths;
- target age private identity;
- source signing-key contents;
- database credentials;
- arbitrary shell or filesystem controls.

The Web host continues to bind to loopback by default and uses access-code,
same-site session and CSRF protections introduced by MM-WEB-01A.
