---
title: Cancel, retry, and handle restore failures
description: Use server-projected cancellation, preserve terminal evidence, and avoid unsafe mid-operation cleanup.
section: Back up and restore
order: 100
---

# Cancel, retry, and handle restore failures

Restore failures are recoverable only when the operator preserves the source and understands which mutations completed.

## Safe cancellation

The Restore Workspace shows **Cancel** only when the backend reports that cancellation is safe.

Cancellation is unavailable when:

- the restore is already completed, cancelled, or otherwise terminal;
- a filesystem, database, Docker, or route operation is queued or running.

MEM will not cancel halfway through a live mutation. Wait for the operation to finish or fail, then refresh the workspace.

A successful cancellation:

- closes the non-terminal workspace;
- releases temporary restore target claims;
- preserves the backup, logs, evidence, support material, and audit record;
- does not delete a completed restored server.

## Retry boundary

Do not repeatedly click an action after a timeout. Refresh the workspace and inspect the operation status first. Durable operation history may show that the server completed work even when the browser lost the response.

Before retrying:

1. Read the stage summary and blocker list.
2. Inspect **Activity**, **Evidence**, and **Logs**.
3. Record the operation ID and event code.
4. Confirm whether database, containers, routes, or target claims were created.
5. Use a provided cleanup action for a failed Standard Recreate when available.
6. Rerun preflight because target ownership may have changed.

## Source failure versus target failure

Source problems include missing payload files, invalid checksums, absent signing identity, or failed database import. Target problems include occupied slugs or hosts, unavailable platform services, route ownership, and runtime startup failures.

Do not edit a catalog payload to make a source error disappear. Create a new verified backup or re-import the original portable archive.

## No automatic rollback

Standard Recreate explicitly acknowledges that no automatic rollback is promised. A failed operation can leave partial production resources that require guided cleanup or careful operator review.

Never delete `mem-postgres` databases, MEM-owned directories, Docker networks, or NPM routes solely because a screen reports failure. First establish ownership from Configuration, Activity, and Evidence.

## Escalation evidence

Generate a [restore support report](evidence-logs-support.md) before host-level cleanup. Keep the restore-session ID, catalog entry ID, target values, latest error code, operation IDs, timestamps, and any cleanup result.
