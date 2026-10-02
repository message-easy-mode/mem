# Backups feature organisation

This feature uses an intentionally **Next-style folder convention** without file-based routing.

- `pages/` mirrors the operator journey and URL hierarchy.
- `api/` mirrors the owning HostAgent contract.
- `shared/components/` contains only genuinely reused components.
- `routes.tsx` is explicit React Router configuration. Folder names do not generate URLs.

## Canonical operator journey

```text
/backups
  /import
  /catalog/:catalogEntryId
  /uploads/:validationId

/restores
  /:restoreSessionId
```

The Backup Catalog is the only restore source. Local captures and imported ZIPs
both materialise as catalog entries before an operator may start a Restore
Workspace.

`/backups/uploads/:validationId` remains intentionally: it manages a retained
original ZIP as ingestion/provenance material. It is not a restore session and
does not create one.

## Restore boundary

```text
uploaded ZIP
  → validation and materialisation
  → Backup Catalog item
  → /restores/:restoreSessionId
  → private test / Standard Recreate / evidence / logs
```

There is no `/restore-sessions` route and no browser route that treats a
validation identifier as a restore workspace identity.


## R06 legacy-surface removal

Validation IDs remain only within uploaded-ZIP validation, retained-archive provenance, and catalog materialisation. They are not restore session identifiers, workspace route keys, private-test sources, or Standard Recreate sources.
