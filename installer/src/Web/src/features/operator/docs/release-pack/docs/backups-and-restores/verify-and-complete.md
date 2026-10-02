---
title: Verify and complete the restored server
description: Run public readiness checks, inspect evidence, complete handover, and prove operator access.
section: Back up and restore
order: 90
---

# Verify and complete the restored server

Standard Recreate creates the stack. The Restore Workspace remains active until public checks pass and the operator explicitly completes handover.

## Run restored-server checks

In **Check the restored server**, run a fresh check. MEM invokes the registered stack's Doctor workflow and projects safe Matrix, Element, route, and connectivity results back into the Restore Workspace.

A URL recorded in configuration is not proof of reachability. Run the check after DNS, NPM, certificate, or network changes rather than relying on an earlier result.

## Review the result

Confirm that:

- the restored stack is registered in **Chat servers**;
- Matrix and Element containers are healthy;
- the Matrix and Element public routes resolve to the intended target;
- public readiness checks pass;
- the Matrix server identity matches the backup;
- the expected users are visible after Users synchronization;
- media needed for recent rooms is present;
- federation policy and TURN state match the recovery plan.

Also sign in to Element with a known Matrix account. Test room history, new messages, media, and—where relevant—federation and voice/video. MEM's automated checks do not prove user-held end-to-end encryption keys or a complete client experience.

## Complete handover

When the latest public checks pass:

1. Expand **Complete and hand over**.
2. Review final verification and any warnings.
3. Acknowledge completion.
4. Open the restored stack from the workspace.
5. Keep the restore-session ID in the change or incident record.

Handover marks the Restore Workspace completed. It does not delete the source backup, private-test history, logs, evidence, or support report.

## After handover

- Create a fresh backup of the recovered stack after confirming service.
- Verify the new backup appears in the Backup Catalog.
- Decide how long to retain the pre-restore source and any old stopped runtime.
- Review users, federation, TURN, storage, and diagnostics from the normal stack workspace.

A completed workspace cannot be cancelled. Normal stack lifecycle and backup retention rules apply from this point.
