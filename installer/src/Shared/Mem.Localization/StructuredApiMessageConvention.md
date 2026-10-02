# MEM structured API messages and restore events

## Purpose

This convention lets MEM add localisable, machine-readable meaning to newly
written or materially touched API workflows without making the API a runtime
translation service and without breaking existing clients.

A structured descriptor is:

```json
{
  "code": "restore.attempt.not-found",
  "arguments": {
    "restoreSessionId": "20260703-071259Z-example"
  }
}
```

`code` is immutable once released. `arguments` are named lower-camel-case
scalar values required to render the meaning later in a CLI, Web UI, support
report, or another trusted client.

## Boundary

For an affected HostAgent problem response, the additive shape is:

```json
{
  "error": "restore_attempt_not_found",
  "detail": "Restore attempt '20260703-071259Z-example' was not found.",
  "message": {
    "code": "restore.attempt.not-found",
    "arguments": {
      "restoreSessionId": "20260703-071259Z-example"
    }
  }
}
```

- `error` remains the existing stable API error code. Do not rename it.
- `detail` remains raw server diagnostic/operator prose. Do not translate or
  parse it as a machine contract.
- `message` is optional and additive. Existing consumers may ignore it.
- A client may render `message` only when it recognises the code and has a
  trusted local catalogue. Otherwise it must retain the server `detail`.

This L16 foundation deliberately does **not** change existing CLI error
presentation. The CLI currently preserves the full raw HostAgent response for
supportability. Later command-family migrations may selectively interpret a
known `message` descriptor while retaining raw detail in diagnostics.

## Restore structured events

Restore event storage already carries the same two-layer boundary:

```text
eventCode  = stable semantic code
details    = named safe arguments
message    = redacted audit/operator prose
```

New restore events should call the `RestoreStructuredLogService.RecordAsync`
overload that accepts `LocalizedMessage`. It writes the descriptor code and
arguments through the established `eventCode` and `details` fields, so NDJSON
schema version 1 and existing event readers remain compatible.

Do not turn `message` prose into a client contract. Do not add a second
translated sentence to the event stream.

## Authoring rules

1. Use `MemStructuredMessage.Create` or a domain descriptor factory.
2. Codes use lower-case dot-separated segments; hyphens are permitted inside a
   segment. Examples: `restore.private-test.started` and
   `backup-catalog.entry.not-found`.
3. Argument names use lower camel case. Prefer shared names from
   `MemMessageArgumentNames`.
4. Arguments must be small scalar values such as IDs, statuses, counts, or
   timestamps. Do not include paths, credentials, tokens, database dumps,
   private keys, raw command output, or unredacted exceptions.
5. Keep the established `error`, `detail`, event `message`, and event schema
   intact unless a separately approved compatibility decision says otherwise.
6. Add a stable constant in `MemMessageCodes` and a focused regression test
   when publishing a new code.
7. Do not retrofit every existing endpoint. Adopt the pattern forward from new
   or materially changed workflow code.

## Current L16 adoption scope

L16 adopts structured problem descriptors for canonical catalog-backed restore
creation, restore inspection/cancellation, workspace inspection, private-test
availability, and handover completion. It also adopts the typed restore-event
overload for `restore.private-test.started`.

Legacy Backup Catalog reads/mutations, advanced cutover routes, raw diagnostics,
and historical log records remain unchanged. They can adopt this convention only
when a future focused slice touches their workflow.
