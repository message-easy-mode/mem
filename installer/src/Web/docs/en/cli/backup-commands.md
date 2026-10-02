---
id: "cli/backup-commands"
translationKey: "cli/backup-commands"
locale: "en"
groupId: "cli"
groupKey: "cli"
groupLabel: "CLI and automation"
groupOrder: 80
title: "Use Backup Catalog commands"
description: "Inspect, export, import, and remove Backup Catalog material while keeping catalog identities and upload-provenance identities separate."
order: 46
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "Backup Catalog", "portable export", "import", "deletion"]
route: "/docs/cli/backup-commands"
aliases: []
outputPath: "docs/cli/backup-commands.md"
preserveLegacyBranding: false
---
# Use Backup Catalog commands

The CLI operates on existing Backup Catalog material. It does not currently create a new backup capture; create backups through the Control Plane.

## Keep the identities separate

```text
validationId       retained uploaded-ZIP provenance
catalogEntryId     durable Backup Catalog source
restoreSessionId   Restore Workspace identity
```

A validated portable ZIP must materialise into a catalog entry before it can become a restore source.

## List and inspect catalog entries

```bash
mem backups list --profile home
mem backups list --profile home --json | jq

mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

`lifecycle` is the safest check before deletion because it reports whether the source can be removed and whether an active Restore Workspace blocks the action.

`mem backups list --stack ...` is not supported. List the catalog, then inspect the selected entry.

## Export a portable backup

```bash
mem backups export <catalog-entry-id> \
  --out ./mem-backup.zip \
  --profile home \
  --json | jq
```

MEM creates a server-side portable export, downloads it, creates the local parent directory when needed, and writes the requested path. Choose the path carefully because an existing file can be replaced.

Verify the reported output path, bytes written, warnings, source stack, and catalog ID before moving the ZIP to external storage.

## Import a portable ZIP

```bash
mem backups import ./mem-backup.zip --profile home --json | jq
```

Exit code `0` requires both a valid intake result and a materialised catalog entry ID. A validation record without a catalog entry is not restore-ready.

Inspect retained upload provenance when needed:

```bash
mem backups uploads inspect <validation-id> --profile home --json | jq
```

Delete only the retained uploaded ZIP archive:

```bash
mem backups uploads delete <validation-id> --yes --profile home --json | jq
```

This does not delete the materialised catalog payload or a Restore Workspace.

## Permanently delete a catalog entry

```bash
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
mem backups delete <catalog-entry-id> --yes --profile home --json | jq
```

Deletion is irreversible and requires `--yes`. The CLI reads lifecycle state first and refuses the server mutation when an active restore session blocks deletion. The result reports payload, original archive, portable exports, and detached terminal restore history separately.

The server may require recent step-up for deletion. The current CLI fails closed on HTTP 403 and does not accept passwords, TOTP codes, recovery codes, bearer tokens, or device credentials as options.

## Verify success

After import, export, or deletion:

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem restores list --profile home --json | jq
```

## Related documentation

- [Back up and restore chat servers](../backups-and-restores/index.md)
- [Restore commands](restore-commands.md)
- [CLI security model](security-model.md)
