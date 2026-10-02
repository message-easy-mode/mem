---
id: "migrate/migration-or-restore"
translationKey: "migrate/migration-or-restore"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Choose migration, backup and restore, or ordinary repair"
description: "Use the correct MEM workflow and understand when migration material becomes an ordinary Backup Catalog source."
order: 140
status: "supported"
appliesTo: ["0.2.x"]
tags: ["migration", "backup", "restore", "repair", "Backup Catalog"]
route: "/docs/migrate/migration-or-restore"
aliases: []
outputPath: "docs/migrate/migration-or-restore.md"
preserveLegacyBranding: false
---
# Choose migration, backup and restore, or ordinary repair

Migration and Backup/Restore share validation and runtime capabilities, but they are separate operator workflows with different sources and evidence.

## Outcome

You choose the correct workflow and do not place unaccepted migration material into the Backup Catalog.

## Use migration when

- the source is a supported legacy MEM 0.1.0 installation;
- the source must be assessed and converted before it can become a normal MEM 0.2.0 stack;
- you need Source Assistant capture, encrypted target intake, private staging, guided route cutover, and acceptance.

Migration owns raw packages, conversion attempts, candidates, staging, production adoption, rollback evidence, and legacy-source retention.

## Use Backup and Restore when

- the source is an existing native MEM Backup Catalog entry or a supported portable MEM backup export;
- you are recovering or recreating a MEM-managed stack from a native recovery payload;
- no legacy-source assessment or conversion is required.

A Restore Workspace does not accept a raw migration package as its source.

## Use ordinary repair when

- the existing managed stack remains the intended server;
- the problem is a stopped container, certificate, DNS, TURN, federation, storage, account, or runtime-health issue;
- recreating or adopting a different source is unnecessary.

Use stack diagnostics and normal operations rather than creating a migration or restore attempt for a routine fault.

## The acceptance boundary

Before acceptance, these remain migration-only material:

- encrypted source package;
- validated source archive;
- conversion output;
- failed or temporary candidate;
- private staging runtime;
- unaccepted normal target runtime.

They must not appear as ordinary Backup Catalog entries.

After production verification and acceptance, MEM creates the first native backup. That accepted stack and backup then enter the normal Backup Catalog and Restore Workspace lifecycle.

## Decision table

| Situation | Correct workflow |
|---|---|
| Move supported MEM 0.1.0 into MEM 0.2.0 | Migration |
| Recover MEM 0.2.0 from a native portable backup | Backup and Restore |
| Test a Backup Catalog source privately | Restore Workspace private test |
| Fix TURN, federation, DNS, or a stopped managed container | Ordinary operation or diagnostics |
| Import an arbitrary Synapse server | Not supported by this migration guide |

Related: [Back up and restore chat servers](../backups-and-restores/index.md).
