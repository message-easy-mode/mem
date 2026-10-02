# MEM HostAgent Runtime Backups

`HostAgent/Runtime/Backups` owns MEM's catalog-first backup and recovery workflow.

## Canonical recovery model

```text
Live RuntimeStack
  → local capture or imported portable ZIP
  → Backup Catalog entry
  → Restore Attempt / Restore Workspace
  → optional private test
  → Standard Recreate
  → public verification
  → recovered RuntimeStack
```

Imported ZIP validation is an ingestion and provenance concern. A validation
identifier is never a Restore Attempt, Restore Workspace, private-test, or
Standard Recreate identity.

## Core ownership

```text
Artifacts/LocalBackups      capture host-owned recovery material
Artifacts/PortableExports   create/download portable recovery ZIPs
Artifacts/ValidatedImports  validate uploads and retain provenance archives
Catalog/                    own recovery-ready catalog items and source resolution
Coordination/               own durable Restore Attempt identity and resource claims
RestoreAttempts/List/       expose the canonical /backups/restores list API
Workspace/                  project and operate the canonical Restore Workspace
Verification/PrivateRuntime/PrivateStaging
                           run/destroy catalog-backed private staging evidence
StandardRecreate/           recreate the supported recovered RuntimeStack
AdvancedCutover/            expert-only catalog-backed exceptional cutover tooling
```

## Public route boundary

The supported operator routes are:

```text
GET  /internal/host-agent/backups/restores
POST /internal/host-agent/backups/catalog/{catalogEntryId}/restore-session
GET  /internal/host-agent/backups/restores/{restoreSessionId}/workspace
POST /internal/host-agent/backups/restores/{restoreSessionId}/private-test
POST /internal/host-agent/backups/restores/{restoreSessionId}/standard-recreate/preflight
POST /internal/host-agent/backups/restores/{restoreSessionId}/standard-recreate
POST /internal/host-agent/backups/restores/{restoreSessionId}/cancel
```

The retained upload route family remains intentionally for ingestion/provenance:

```text
/backups/artifacts/validated-imports/{validationId}
/backups/catalog/imports/{validationId}/archive
```

These routes do not create or operate Restore Attempts.

## Endpoint architecture

Carter discovers every `ICarterModule` in the HostAgent assembly when the
application calls `app.MapCarter()`. Endpoints validate the existing
`X-MEM-Agent-Secret` header, bind HTTP input, invoke the owning service, and
return HTTP output. Recovery orchestration belongs in services, not endpoint
adapters.


## R06 legacy-surface removal

Validation IDs remain only within uploaded-ZIP validation, retained-archive provenance, and catalog materialisation. They are not restore session identifiers, workspace route keys, private-test sources, or Standard Recreate sources.
