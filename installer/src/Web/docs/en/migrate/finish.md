---
id: "migrate/finish"
translationKey: "migrate/finish"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Accept and finish the migration"
description: "Accept the verified server, set legacy retention, create the first native backup, and enter the normal MEM lifecycle."
order: 90
status: "supported"
appliesTo: ["0.2.x"]
tags: ["acceptance", "legacy retention", "first native backup", "completion", "handover"]
route: "/docs/migrate/finish"
aliases: []
outputPath: "docs/migrate/finish.md"
preserveLegacyBranding: false
---
# Accept and finish the migration

Acceptance is the boundary where the verified MEM 0.2.0 server becomes the authoritative managed server and enters the normal backup and restore lifecycle.

## Outcome

The migration is durably accepted, the legacy source has a recorded retention period, the first native MEM backup is created or explicitly retryable, and migration-owned staging cleanup can complete.

## Acceptance prerequisites

Finish only after:

- public route ownership is known;
- production verification has passed and remains valid;
- the Matrix identity and expected service are correct;
- operators understand that new target changes will not synchronize back to the old source;
- the legacy source can remain retained for the chosen period.

## Choose legacy retention

Select the retention duration for the old server. Retention is an operational safety record; it is not an instruction for MEM to delete the source host. Keep the source powered down, isolated, or otherwise controlled according to your rollback plan, but preserve its data and evidence.

## Accept the migrated server

Complete the confirmation and step-up prompts. Acceptance records the target as authoritative and closes the normal source-to-target adoption boundary.

After acceptance:

- the new MEM runtime is the production system;
- new messages and account changes are not copied back to MEM 0.1.0;
- the legacy source is retained, never automatically deleted;
- raw migration artifacts remain migration evidence rather than normal backups.

## First native backup

Finish starts the first native MEM backup of the accepted stack. That backup is the boundary at which the migrated server enters the ordinary Backup Catalog and Restore Workspace lifecycle.

If the baseline backup fails after acceptance, acceptance remains durable. Do not repeat migration acceptance. Use the workspace action to retry only the first native backup and review its evidence.

## Completion and cleanup

Download or retain the completion report. MEM then attempts to remove successful migration staging resources. A cleanup failure is a follow-up operation and does not undo acceptance or the first successful backup.

Verify:

- the session shows accepted/completed state;
- the normal chat-server workspace opens;
- the first native Backup Catalog entry is present when backup succeeds;
- the old source retention record is visible;
- staging cleanup is complete or safely retryable.

Next: [Retain or clean up migration resources](cleanup.md).
