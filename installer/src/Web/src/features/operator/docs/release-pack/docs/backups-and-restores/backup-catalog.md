---
title: Understand the Backup Catalog
description: Read recovery source identity, provenance, payload state, integrity, advisories, and lifecycle safely.
section: Back up and restore
order: 20
---

# Understand the Backup Catalog

Open **Backups** to view every recovery-ready source known to MEM. Local captures and imported ZIPs share one catalog because both must enter the same restore workflow.

## Origin and identity

Each entry has a stable catalog entry ID and one origin:

- `local-captured` — captured directly from a managed stack;
- `imported-zip` — validated and materialised from a portable MEM export.

The detail page can also show source stack slug, source backup ID, validation upload ID, manifest version, MEM version, Matrix server name, Matrix host, Element host, capture time, and import time.

## Payload state

The payload state describes the managed recovery material:

- `available` — MEM can resolve the payload for restore;
- `materialising` — a validated archive is being copied into catalog-owned storage;
- `failed` — materialisation or payload preparation failed;
- `removed` — the catalog payload was intentionally deleted.

A catalog record may remain readable after its payload is removed so that provenance and audit history are not silently erased.

## Integrity and advisories

Integrity status can be `valid`, `warning`, `invalid`, or `unknown`.

Imported archives can also carry **advisories**. Advisories are non-blocking operator guidance, such as secure handling of signing material, old-server identity risk, route/TURN context, or regenerated-export provenance. They are deliberately separate from checksum or structural integrity failures.

Do not ignore a warning merely because the Restore button is enabled. Read the integrity summary and every advisory before production recovery.

## Search and filtering

The catalog supports search, origin, stack, and sort filters. Use these fields rather than relying only on a display name. Two captures may have similar names but different backup IDs, times, payload states, or Matrix identities.

## Restore-session boundary

Open an available entry and choose **Restore**. MEM creates or resumes a durable Restore Workspace and redirects to `/restores/<restoreSessionId>`.

The catalog entry remains the source. The restore-session ID is the workspace identity. An upload validation ID is neither.

## Lifecycle boundary

An active restore blocks permanent deletion of its source. The detail page shows whether a payload is present, whether an active Restore Workspace exists, and why deletion is blocked.

Permanent catalog deletion is irreversible. It can remove the managed payload, retained original upload, generated portable exports, and catalog linkage from older restore attempts. Read [Delete and retain recovery material](deletion-retention.md) before using it.
