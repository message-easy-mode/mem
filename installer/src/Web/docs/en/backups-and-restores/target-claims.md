---
id: "backups-restores/target-claims"
translationKey: "backups-restores/target-claims"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Resolve target claims and hostname conflicts"
description: "Understand read-only preflight, atomic claims, preserved Matrix identity, and safe conflict resolution."
order: 80
status: "supported"
appliesTo: ["0.2.x"]
tags: ["target claims", "hostname conflict", "Matrix identity", "preflight", "routes"]
route: "/docs/backups-and-restores/target-claims"
aliases: []
outputPath: "docs/backups-and-restores/target-claims.md"
preserveLegacyBranding: false
---
# Resolve target claims and hostname conflicts

MEM coordinates restore targets so that two active attempts cannot safely claim the same stack slug or public host.

## Target values

Standard Recreate uses three important values:

- **Matrix server identity / Matrix host** — preserved from the backup;
- **Target stack slug** — the new MEM runtime identity, often suggested as `<source>-restored`;
- **Element host** — the public Element address, which may reuse the source address when available or use another approved host.

You cannot solve a Matrix-host conflict by casually choosing a different Matrix domain. Changing it would create a different Matrix identity rather than restore the existing one.

## Preflight checks

The read-only preflight assesses:

- catalog source validity and payload availability;
- Matrix server identity preservation;
- target stack manifest and live-runtime availability;
- target stack claim availability;
- Matrix host runtime and claim availability;
- Element host runtime and claim availability;
- route ownership and TURN requirements.

A ready result is current evidence, not a reservation. Another operation could change availability before execution, so Standard Recreate checks again and claims the values atomically.

## Common conflicts

### Stack slug already exists

Choose a different target slug. Do not delete an unrelated stack merely to satisfy preflight.

### Matrix address is still active

Stop or retire the old runtime through its supported lifecycle and confirm the original public route no longer owns the Matrix host. Preserve the old server until you have the final backup and rollback decision you need, but do not leave both publicly active.

### Matrix or Element host is claimed

Open the other active Restore Workspace and decide which attempt owns the target. A safe cancellation releases temporary claims when no operation is queued or running.

### Element host is in use

Select another approved Element host or retire the conflicting route through its owning stack workflow. Do not edit Nginx Proxy Manager out of band unless working through a documented emergency procedure.

## Claim evidence

The Restore Workspace **Configuration** tab records resource type, value, claim status, claim time, release time, and release reason. Keep this evidence when diagnosing a conflict.

A completed restored stack is not undone by cancelling its old workspace. Once production recreation has completed, manage the stack through normal stack operations.
