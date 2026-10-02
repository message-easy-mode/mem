---
title: Back up and restore chat servers
description: Protect Matrix data with the Backup Catalog and recover it through a durable Restore Workspace.
section: Back up and restore
order: 0
---

# Back up and restore chat servers

MEM treats backup and recovery as a product workflow, not as a collection of Docker commands.

## Outcome

After following this section, you can:

- capture a recovery-ready backup from a managed Matrix + Element stack;
- inspect the backup in the Backup Catalog;
- export an off-host portable ZIP or import one from another MEM server;
- open a durable Restore Workspace;
- run an optional private test before production changes;
- perform the supported Standard Recreate workflow;
- verify the recovered public service and complete handover;
- preserve evidence when a restore is cancelled or fails.

## Canonical recovery model

```text
Live Runtime Stack
  → local capture or imported portable ZIP
  → Backup Catalog entry
  → Restore Workspace
  → optional private test
  → Standard Recreate
  → public verification
  → operator handover
```

The **Backup Catalog is the only supported restore source**. An uploaded ZIP validation ID is an ingestion and provenance reference. It is not a restore-session ID and must not be used as a Restore Workspace route.

## Start here

1. [Create and inspect a backup](create-backup.md).
2. [Understand the Backup Catalog](backup-catalog.md).
3. [Export a portable backup](portable-export.md) and store it away from the MEM host.
4. When recovery is required, [start a Restore Workspace](restore-workspace.md).
5. Prefer the [private test](private-test.md) before production recreate when time permits.
6. Review [Standard Recreate](standard-recreate.md), [target claims](target-claims.md), and [verification and handover](verify-and-complete.md) before executing.

## Restore is not migration

Restore recreates a supported MEM stack from a native Backup Catalog payload while preserving the backed-up Matrix identity. Migration assesses and converts a different or legacy source environment. Read [Restore or migrate?](restore-or-migrate.md) before using a backup to move between unlike installations.

> [!IMPORTANT]
> A server backup does not replace user recovery keys or device key backup. MEM can restore server-side rooms, events, accounts, media, configuration, and identity material that was captured. It cannot recreate end-to-end encryption keys that users never preserved.
