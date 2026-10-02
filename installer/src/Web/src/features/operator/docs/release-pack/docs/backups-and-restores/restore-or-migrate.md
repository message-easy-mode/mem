---
title: Choose restore, migration, or ordinary repair
description: Select the correct workflow for native recovery, legacy MEM adoption, host movement, and configuration repair.
section: Back up and restore
order: 130
---

# Choose restore, migration, or ordinary repair

Backup/Restore and Migration are separate bounded workflows in MEM.

## Use restore when

Use the Backup Catalog and Restore Workspace when you have a supported native MEM recovery payload and need to recreate a Matrix + Element stack while preserving the backed-up Matrix identity.

Typical cases include:

- recovering after host or disk loss;
- recreating a removed or damaged MEM 0.2.x stack;
- moving a native portable backup to another compatible MEM installation;
- proving recoverability through a private test.

## Use MEM Migrate when

Use MEM Migrate when the source is a legacy or different installation that must be assessed, captured, converted, staged, adopted, or cut over.

For MEM 0.2.0, the primary supported migration source is the defined MEM 0.1.0 profile. A legacy `mem-api` / `mem-web` server is not converted merely by importing an arbitrary directory as a backup.

Migration owns source assessment, package capture, compatibility, transformation, private staging, cutover, acceptance, rollback planning, and legacy retention. Only after acceptance does the migrated stack enter the normal Backup Catalog lifecycle through a native baseline backup.

## Use ordinary repair when

Use the owning stack, platform, or diagnostics workflow when the runtime still exists and the problem is a repairable configuration, route, certificate, TURN, federation, or container issue.

Do not restore merely to fix one NPM route or a temporary Docker outage. A restore creates a new production stack and can introduce more risk than a scoped repair.

## Decision questions

1. Is the source already a supported Backup Catalog entry?
2. Must the Matrix server identity remain exactly the same?
3. Does the source require conversion or compatibility assessment?
4. Is the current runtime still intact and repairable?
5. Is an old public homeserver still serving the same Matrix identity?
6. Do you have a tested off-host backup and user encryption-key recovery plan?

When the answers are unclear, stop before production mutation. Preserve the current source, collect diagnostics, and choose the workflow that owns the actual problem.
