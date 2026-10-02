---
id: "backups-restores/import-backup"
translationKey: "backups-restores/import-backup"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Import and materialise a portable backup"
description: "Validate a portable ZIP, separate ingestion identity from recovery identity, and create a catalog entry."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["import", "validation", "materialisation", "ZIP", "provenance"]
route: "/docs/backups-and-restores/import-backup"
aliases: []
outputPath: "docs/backups-and-restores/import-backup.md"
preserveLegacyBranding: false
---
# Import and materialise a portable backup

Use **Backups → Import ZIP** to bring a portable MEM backup into the current Control Plane.

## Validation first

1. Select the portable `.zip` file.
2. Choose **Validate**.
3. Review the archive size, entry count, uncompressed size, manifest, checksum results, validation checks, warnings, and errors.
4. Continue only when the result is valid and the manifest identifies the expected stack.

Validation checks the archive structure, manifest, required payload declarations, and file checksums. A failed validation does not create a usable restore source.

## Automatic materialisation

For a valid upload, MEM normally materialises the payload into Backup Catalog storage during ingestion and returns a catalog entry ID. Open that catalog entry to continue.

If a retained valid upload exists without a linked catalog entry, open its upload detail page and use **Materialise**. This copies recovery files into catalog-owned storage; it does not start a restore.

## Keep the identities separate

The upload has a **validation ID** used to inspect or delete the retained source archive. The recovery payload has a **catalog entry ID**. A later restore has a **restore-session ID**.

```text
validation ID  → uploaded ZIP provenance
catalog ID     → recovery-ready managed payload
restore ID     → durable Restore Workspace
```

Do not paste a validation ID into a Restore Workspace URL or treat it as a restore attempt.

## Warnings and advisories

A valid archive may retain non-blocking advisories. Examples include secure handling of the signing key, the requirement to stop an old server before recovering the same Matrix identity, or the fact that route and TURN metadata are snapshots.

Advisories do not mean checksum failure, but they still require operator review.

## Original upload lifecycle

After successful materialisation, the original uploaded ZIP and the catalog payload have separate lifecycles. Deleting the retained uploaded ZIP does not delete:

- the catalog-managed payload;
- restore sessions, logs, evidence, or support reports;
- a restored production stack.

Keep or delete the original upload according to your provenance and storage policy. Do not delete the catalog payload until recovery is no longer required.
