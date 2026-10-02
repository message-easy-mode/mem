---
id: "backups-restores/deletion-retention"
translationKey: "backups-restores/deletion-retention"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Delete and retain recovery material safely"
description: "Distinguish catalog payload, original upload, portable export, private staging, workspace history, and restored runtime."
order: 120
status: "supported"
appliesTo: ["0.2.x"]
tags: ["retention", "delete backup", "uploaded ZIP", "private staging", "audit"]
route: "/docs/backups-and-restores/deletion-retention"
aliases: []
outputPath: "docs/backups-and-restores/deletion-retention.md"
preserveLegacyBranding: false
---
# Delete and retain recovery material safely

MEM intentionally separates recovery objects so that deleting one does not imply every related record or runtime was erased.

## Objects with separate lifecycles

- **Catalog entry and managed payload** — the recovery source used by Restore Workspace actions.
- **Original uploaded ZIP** — retained ingestion and provenance material for an imported archive.
- **Portable exports** — generated downloads associated with a catalog entry.
- **Private staging runtime** — disposable containers, network, and workspace created by a private test.
- **Restore Workspace** — durable attempt, claims, operations, logs, evidence, and support history.
- **Restored production stack** — a normal managed stack after Standard Recreate.

## Delete the original uploaded ZIP

Deleting a retained uploaded ZIP does not delete the materialised catalog payload, Restore Workspaces, logs, evidence, support reports, or restored stack. Use this when the original transport archive is no longer required but the catalog copy must remain recoverable.

## Retire private staging

Use **Retire private test** to remove disposable private containers, the internal network, and private workspace. The safe historical private-test result remains visible.

## Permanently delete a catalog entry

The catalog detail page allows permanent deletion only when no active restore blocks it. The operation can remove:

- the managed payload;
- the retained original archive when linked;
- server-side portable exports;
- catalog linkage from historical restore attempts.

The deletion is irreversible. A previously downloaded off-host ZIP is outside MEM and remains wherever you stored it.

Historical Restore Workspaces can remain readable after source deletion, but guided actions are blocked because executable source material no longer exists.

## Completed restored servers

Cancelling or deleting a Restore Workspace does not roll back or delete a completed restored stack. Manage that stack through normal stack operations and take a fresh backup before removal.

## Suggested retention policy

Keep at least:

- more than one recent local capture;
- at least one tested off-host portable export;
- the pre-change backup until the change is proven stable;
- restore evidence and support reports for the period required by your operational policy.

Test restoration periodically. Retention without a restore proof is only an assumption.
