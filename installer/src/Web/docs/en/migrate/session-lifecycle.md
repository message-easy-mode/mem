---
id: "migrate/session-lifecycle"
translationKey: "migrate/session-lifecycle"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Resume, cancel, archive, and restore sessions"
description: "Manage durable Migration Sessions without deleting evidence or confusing an archived session with a cancelled migration."
order: 120
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Migration Session", "resume", "cancel", "archive", "permanent deletion"]
route: "/docs/migrate/session-lifecycle"
aliases: []
outputPath: "docs/migrate/session-lifecycle.md"
preserveLegacyBranding: false
---
# Resume, cancel, archive, and restore sessions

Migration Sessions are durable. Leaving the page or restarting a browser does not turn an in-progress operation into a new migration.

## Outcome

You can return to an existing session, cancel only where safe, archive terminal history, restore it to the list, and understand why most progressed sessions cannot be permanently deleted.

## Resume after refresh or interruption

Open **Migrations**, find the existing session, and continue from its current stage. Running conversion, capture, staging, target creation, cutover, backup, or cleanup operations are server-owned. Review the recorded status before starting another operation.

Do not create a second intake merely because a page was closed or a preview expired. Refresh the stage evidence or readiness snapshot in the existing session.

## Cancel an early session

Cancellation is available only while the target session is still genuinely disposable. The cancellation review requires explicit choices about encrypted target-package retention and acknowledgement that the source host is outside target control.

Target cancellation:

- does not stop, alter, or delete the source server;
- removes decrypted target working material;
- clears the target decryption identity;
- retains redacted audit and safe hashes;
- may retain or remove the encrypted package according to the selected option.

A retained encrypted package cannot resume the cancelled session after the identity is cleared.

## Archive and restore to active list

Terminal sessions can be archived to reduce normal-list noise. Archiving changes visibility, not migration resources or evidence. Use the archived filter to find them and **Restore to active list** when they need normal visibility again.

Archiving is not cancellation and is not permanent deletion.

## Permanent deletion

Permanent deletion is restricted to server-proven disposable early sessions and requires the exact Migration ID. It can remove the session record, package-revision metadata, validation history, and verified target-package remnants while retaining a redacted audit record.

A session is not disposable after it owns or references consequential state such as conversion, staging, a target runtime, production routes, acceptance, first native backup, Backup Catalog or restore provenance, source qualification, or legacy retention.

Accepted or completed sessions and their first-native-backup boundary must be retained.

## Verify the action

After any lifecycle action, confirm the list filter, session status, archived state, and the stated resource/evidence consequences. Never infer deletion from a missing row in the default list.

Related: [Use migration evidence, logs, and support information](evidence-and-support.md).
