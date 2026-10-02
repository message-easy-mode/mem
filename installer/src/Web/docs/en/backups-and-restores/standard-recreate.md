---
id: "backups-restores/standard-recreate"
translationKey: "backups-restores/standard-recreate"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Recover with Standard Recreate"
description: "Create a real recovered Matrix + Element stack from a catalog payload through the supported production path."
order: 70
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Standard Recreate", "production restore", "Matrix identity", "PostgreSQL", "Nginx Proxy Manager"]
route: "/docs/backups-and-restores/standard-recreate"
aliases: []
outputPath: "docs/backups-and-restores/standard-recreate.md"
preserveLegacyBranding: false
---
# Recover with Standard Recreate

**Standard Recreate** is the normal supported production recovery path for a Backup Catalog entry. It creates a real managed Matrix + Element stack; it is not a container preview.

## Before execution

Complete the read-only target preflight and confirm:

- the catalog payload is available;
- the backed-up Matrix server identity is present and preserved;
- the target stack slug is unused and unclaimed;
- the original Matrix address is ready for recovery and not claimed;
- the chosen Element host is available and unclaimed;
- required platform services, including coturn when the backup requires a platform rebind, are ready.

Preflight does not reserve the target. Execution repeats the checks and acquires claims atomically.

## Production mutations

The create action requires explicit acknowledgements because it can:

- provision and import a production PostgreSQL database;
- restore Matrix configuration, signing key, and media;
- restore and patch Element configuration;
- create Matrix and Element containers on the MEM runtime network;
- register the recovered stack and database ownership;
- create or update Nginx Proxy Manager routes;
- run internal and public readiness checks.

The action is protected by operator step-up. There is **no automatic rollback** for the production recreate. Preserve the backup and review all target details before confirming.

## Matrix identity

The Matrix server identity comes from the backup and is immutable in the guided workflow. This is necessary for existing Matrix user IDs, rooms, federation identity, and signing material to remain coherent.

The old server or old public route for the same Matrix identity must no longer be active. Running two public homeservers with the same identity can split traffic and damage federation behaviour.

## TURN fidelity

Standard Recreate applies a deterministic TURN policy:

- no recorded TURN association restores as **disconnected**;
- MEM-managed TURN rebinds the stack to the current platform coturn service and blocks when that service is not ready;
- external TURN preserves the backed-up Synapse TURN settings;
- legacy TURN settings without durable association metadata are preserved and classified as external rather than silently replaced.

The restore does not place a test call. Verify voice and video after handover.

## User inventory

Matrix accounts remain in the restored Synapse database. MEM attempts to synchronize its safe Users inventory after the recreate. A synchronization warning does not mean the Matrix accounts were deleted; retry synchronization from the restored stack's Users area.

## Completion boundary

A successful create operation is not final handover. Continue to [Verify and complete the restored server](verify-and-complete.md).
