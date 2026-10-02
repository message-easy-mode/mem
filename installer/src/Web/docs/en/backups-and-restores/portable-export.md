---
id: "backups-restores/portable-export"
translationKey: "backups-restores/portable-export"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Export a portable backup"
description: "Generate a fresh portable ZIP from catalog-managed recovery material and store it off-host."
order: 30
status: "supported"
appliesTo: ["0.2.x"]
tags: ["portable export", "ZIP", "off-host backup", "checksums", "signing key"]
route: "/docs/backups-and-restores/portable-export"
aliases: []
outputPath: "docs/backups-and-restores/portable-export.md"
preserveLegacyBranding: false
---
# Export a portable backup

A local backup on the MEM host protects against an application mistake. It does not protect against host loss, disk failure, theft, or destructive administrator action. Export important catalog entries and copy them to independent storage.

## Create the export

1. Open **Backups**.
2. Select an entry whose payload is available.
3. In **Portable export**, create and download the ZIP.
4. Record the catalog entry ID, export filename, size, and download time.
5. Copy the ZIP to protected off-host storage.

MEM generates the portable archive from the catalog-managed payload. For an imported backup, the new export does not depend on retaining the originally uploaded ZIP.

## Portable archive contents

The export manifest describes:

- source stack and Matrix server identity;
- PostgreSQL dump presence;
- Matrix configuration, signing key, and media;
- Element configuration;
- Matrix and Element route hosts;
- TURN state and restore intent;
- restore-policy requirements;
- included files and warnings;
- the checksums file used during later import validation.

The browser response does not expose host filesystem paths.

## Security handling

Treat the ZIP as highly sensitive. It may contain:

- private room and account data from PostgreSQL;
- media uploaded by users;
- Matrix configuration;
- the Matrix signing key;
- service topology and domain information.

Encrypt storage where practical, restrict access, and avoid placing the archive in ordinary shared folders or public issue trackers.

## Verify the off-host copy

Confirm that the copied file size matches the downloaded file and that it can be read from the destination. The strongest operational proof is to import it on a non-production MEM environment and run a [private test](private-test.md).

A portable ZIP is a MEM recovery artifact, not a universal Synapse migration format. Compatibility still depends on the manifest, payload, supported database/runtime policy, and the target MEM release.

## Retention interaction

Deleting the catalog entry can also delete server-side portable exports associated with that entry. The copy you downloaded is outside MEM's control. Maintain an independent retention policy and periodically prove that off-host archives are readable.
