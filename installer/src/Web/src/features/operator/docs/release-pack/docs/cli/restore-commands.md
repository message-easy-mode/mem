---
title: Use Restore Workspace commands
description: Inspect durable restore sessions, run private tests, preflight Standard Recreate, acknowledge production mutation, and retain evidence.
section: CLI and automation
order: 47
---

# Use Restore Workspace commands

Restore commands are catalog-first and session-based. A restore source is identified by `catalogEntryId`; all later work uses the durable `restoreSessionId`.

## List restore sessions

```bash
mem restores list --profile home
mem restores list --page 1 --page-size 25 --profile home --json | jq
```

Available filters include:

```bash
mem restores list --search <text> --profile home --json | jq
mem restores list --status active --profile home --json | jq
mem restores list --target-stack demo --profile home --json | jq
mem restores list --sort-by updatedAtUtc --sort-direction desc --profile home --json | jq
```

The server validates supported filter and sort values. `--page` and `--page-size` must be positive integers.

## Create or resume a workspace

```bash
mem restores create <catalog-entry-id> --profile home --json | jq
```

The server may return an existing active Restore Workspace for the same source. Record the returned restore session ID; do not substitute the catalog ID or upload validation ID.

## Inspect state and evidence

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logs support page, page size, severity, stage, and text filters. `support-report` reads the report already associated with the workspace; it does not create one implicitly.

Terminal restore history remains inspectable even if its source catalog item was later deleted. The source is presented as a deleted backup rather than fabricated as available.

## Run a private test

```bash
mem restores private-test <restore-session-id> --profile home --json | jq
```

A successful private test returns `ready`. It creates isolated staging, not a public production stack.

When evidence shows retained staging can be removed:

```bash
mem restores private-test destroy <restore-session-id> \
  --yes \
  --profile home \
  --json | jq
```

This removes only the private-test containers, network, and workspace. It retains the Backup Catalog source, production stack, and durable restore evidence.

## Preflight Standard Recreate

```bash
mem restores recreate preflight <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --matrix-host <matrix-host> \
  --profile home \
  --json | jq
```

`--matrix-host` and `--requested-domain-id` are optional. Review every check, target claim, hostname conflict, and warning. Do not execute until the preflight result is ready and the target values are deliberate.

## Execute Standard Recreate

```bash
mem restores recreate execute <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --execute-production-recreate \
  --acknowledge-creates-real-stack \
  --acknowledge-mutates-production-postgres \
  --acknowledge-mutates-npm-routes \
  --acknowledge-no-automatic-rollback \
  --profile home \
  --json | jq
```

Use the same target values reviewed during preflight. When you supplied `--matrix-host` or `--requested-domain-id` during preflight, supply them again during execution.

This action can create a real stack, import production PostgreSQL data, start production containers, and change Nginx Proxy Manager routes. There is no automatic rollback. The four acknowledgement flags are mandatory and are not decorative.

Server role and step-up policy remains authoritative. On HTTP 403 the CLI fails closed; complete the protected workflow in the browser or wait for an approved CLI step-up design rather than passing secrets through the shell.

## Cancel or complete handover

```bash
mem restores cancel <restore-session-id> --yes --profile home --json | jq
mem restores handover complete <restore-session-id> --yes --profile home --json | jq
```

Cancellation releases temporary claims only when no queued or running operation could be left half-mutated. It retains the catalog source and audit history. Handover completion marks a verified workspace complete and does not delete the record.

## Related documentation

- [Start and use a Restore Workspace](../backups-and-restores/restore-workspace.md)
- [Recover with Standard Recreate](../backups-and-restores/standard-recreate.md)
- [JSON output and scripting](json-and-scripting.md)
