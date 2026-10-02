---
title: Create and inspect a backup
description: Capture the database, Matrix identity, media, configuration, route, and TURN evidence for one stack.
section: Back up and restore
order: 10
---

# Create and inspect a backup

Create a backup before upgrades, federation changes, TURN changes, storage work, account recovery work, or any operation whose rollback would otherwise depend on memory.

## Before you begin

Confirm that:

- the stack is visible in **Chat servers**;
- the MEM Control Plane can reach Docker and `mem-postgres`;
- the host has enough free space for the database dump and media copy;
- no unrelated host-level maintenance is changing the same files.

## Create the backup

1. Open **Chat servers** and select the stack.
2. Open **Backups & recovery**, or use **Create backup** in the stack workspace header.
3. Wait for the success notice. Record the backup ID and creation time.
4. Open the recovery source in the Backup Catalog.

The capture reads production PostgreSQL and copies recovery material into MEM-managed backup storage. It does not stop the stack or change public routes.

## What MEM captures

A native MEM backup can contain:

- a PostgreSQL dump of the Synapse database;
- `homeserver.yaml`;
- the Matrix signing key;
- the Matrix `media_store` directory;
- Element `config.json`;
- a manifest with stack identity, file statistics, and warnings;
- the Matrix and Element public-route snapshot;
- the effective TURN state recorded from the captured Synapse configuration.

The route and TURN sections are capture-time evidence. MEM does not reconstruct them later from whatever the live stack happens to look like.

## Verify success

In the Backup Catalog, confirm:

- **Origin** is `local-captured`;
- **Payload** is available;
- the source stack and backup ID are correct;
- the captured time and size are plausible;
- integrity is valid, or every warning is understood;
- Matrix server name, Matrix host, and Element host match the intended stack.

A missing signing key is critical because it is part of the Matrix federation identity. Missing media means messages may survive while uploaded files and thumbnails do not. Do not treat either warning as routine.

## Evidence to retain

Keep the backup ID, catalog entry ID, operation ID, warning text, total bytes, and capture time. When the UI reports a captured payload but the expected catalog entry is absent, do not move or rename the backup directory manually. Preserve the operation evidence and use the supported catalog backfill or diagnostics path.

> [!NOTE]
> Matrix clients retain their own end-to-end encryption material. A successful server backup does not prove that every user can decrypt historical encrypted rooms after a device loss or password reset.
