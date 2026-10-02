---
id: "migrate"
translationKey: "migrate"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Migrate from MEM 0.1.0"
description: "Move one legacy MEM 0.1.0 chat server into MEM 0.2.0 through an encrypted, staged, and verified migration."
order: 0
status: "supported"
appliesTo: ["0.2.x"]
tags: ["migration", "MEM 0.1.0", "MEM Migrate", "Source Assistant", "go-live"]
route: "/docs/migrate"
aliases: []
outputPath: "docs/migrate/index.md"
preserveLegacyBranding: false
---
# Migrate from MEM 0.1.0

MEM Migrate moves one supported legacy MEM 0.1.0 chat server into MEM 0.2.0 without treating the old server as a normal backup.

The workflow preserves the selected Matrix identity, encrypts the transfer for the target Control Plane, creates a private candidate, proves the new runtime before public cutover, and records durable evidence throughout.

## Outcome

After following this section, you can:

- install and open the MEM Migrate Source Assistant on the legacy host;
- assess the source without changing it;
- select exactly one source stack;
- import the target Migration Request and create an encrypted package;
- upload, validate, convert, and privately test the package in MEM 0.2.0;
- create the new normal MEM server without publishing it;
- perform a guided route cutover and production verification;
- accept the migrated server, retain the legacy source, and create the first native MEM backup;
- understand cancellation, rollback, cleanup, and evidence boundaries.

## The two workspaces

```text
Legacy MEM 0.1.0 host                         MEM 0.2.0 target
─────────────────────                         ────────────────
MEM Migrate Source Assistant                  Control Plane Migration Workspace
  1. Import request                             1. Create and upload package
  2. Confirm readiness                          2. Review the old server
  3. Create capture                             3. Prepare and test
  4. Create package                             4. Create the new server
  5. Download package                           5. Make the new server live
                                                 6. Finish the migration
```

The Source Assistant reads and packages the selected legacy stack. The target Control Plane owns decryption, conversion, private staging, route cutover, acceptance, and the first native backup.

## Core safety rules

- Migrate **one stack at a time**. The source selection is written into the package and is authoritative.
- Keep the legacy source available until production verification, acceptance, and the chosen retention period are complete.
- Do not expose the Source Assistant to the public internet. Use loopback access or an SSH tunnel.
- Protect user encryption recovery before capture. A password reset cannot recreate missing historical message keys.
- Private staging has no public Matrix or Element routes.
- Route publication is not DNS management. Required DNS records and a usable certificate must already exist.
- A failed live verification does not automatically make the old server public again.
- Migration material does not enter the Backup Catalog until the server is accepted and MEM creates its first native backup.

> [!IMPORTANT]
> Do not install MEM 0.2.0 over the legacy host as a shortcut. Use a separate MEM 0.2.0 target and the guided migration path.

## Start here

1. [Decide whether migration is the correct path and prepare safely](decide-and-prepare.md).
2. [Install and open the Source Assistant privately](install-source-assistant.md).
3. [Assess the source and select one stack](assess-and-select.md).
4. [Create the encrypted migration package](create-package.md).
5. Continue in the target Control Plane with [Upload and review the old server](upload-and-review.md).
6. Read [Rollback boundaries](rollback.md) before the production cutover.
