---
id: "backups-restores/restore-workspace"
translationKey: "backups-restores/restore-workspace"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Start and use a Restore Workspace"
description: "Create or resume the durable catalog-backed workspace that owns one recovery attempt."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Restore Workspace", "restore session", "stages", "evidence", "audit"]
route: "/docs/backups-and-restores/restore-workspace"
aliases: []
outputPath: "docs/backups-and-restores/restore-workspace.md"
preserveLegacyBranding: false
---
# Start and use a Restore Workspace

A Restore Workspace is the durable control surface for one catalog-backed recovery attempt.

## Start or resume

1. Open **Backups** and select an available catalog entry.
2. Review provenance, payload state, integrity, advisories, and Matrix identity.
3. Choose **Restore**.
4. When an active workspace already exists, the same action becomes **Resume**.

MEM redirects to `/restores/<restoreSessionId>`. The restore-session ID remains stable across refreshes and is the reference to keep in incident notes.

## Standard workflow

The **Standard** tab presents five operator steps:

1. **Backup ready** — confirms the catalog source is usable.
2. **Private test** — optional isolated database import and Synapse health proof.
3. **Choose and create restored server** — preflight target details and execute Standard Recreate.
4. **Check restored server** — run fresh public Matrix, Element, route, and connectivity checks.
5. **Complete and hand over** — acknowledge the verified service and retain the workspace as an audit record.

The current actionable, failed, running, or latest completed step opens automatically when the workspace is revisited.

## Workspace tabs

- **Standard** — guided recovery stages.
- **Activity** — durable stage and event timeline.
- **Logs** — structured, filterable restore events.
- **Evidence** — curated successes, warnings, and failures.
- **Configuration** — safe source, target, claim, and operation facts.
- **Advanced** — exceptional tools; unavailable tools show an explicit reason.

## Source and target are separate

The source card identifies the catalog payload. The target is initially unselected. Standard Recreate preserves the Matrix server identity from the backup while allowing a new stack slug and an available Element host.

## Durable evidence

Refreshing the browser does not create a new attempt. Operations, target claims, warnings, errors, evidence, and logs remain associated with the restore-session ID.

When the catalog payload is later permanently deleted, the workspace remains readable for audit and support, but guided actions that need the source are blocked.

> [!IMPORTANT]
> Do not operate a restore from the uploaded-ZIP detail page. Validation and retained archive management are ingestion concerns. All recovery execution begins from the Backup Catalog and continues in the Restore Workspace.
