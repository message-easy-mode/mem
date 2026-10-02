# MM-01 Assessment Contract

## Purpose

MM-01 determines whether the local host appears to contain the exact MEM
v0.1.0 source supported by the migration programme.

It is an evidence-gathering stage, not an upgrade stage.

## Read-only source contract

The assessment may run these operations:

- `docker version`
- `docker ps`
- `docker inspect`
- `docker network ls`
- `docker network inspect`
- `docker exec <postgres> psql ... SELECT ...`
- local HTTP `GET /api/system/config`
- local file metadata and bounded directory enumeration
- read `homeserver.yaml`

It may write only to:

- the selected workspace;
- the selected report output directory.

The assessment must never invoke:

- `docker start`
- `docker stop`
- `docker restart`
- `docker rm`
- `docker exec` with a mutating SQL statement
- `INSERT`, `UPDATE`, `DELETE`, `ALTER`, `DROP`, or `CREATE` against the source;
- NPM route mutation;
- stack file modification.

## Exact database contract

Expected migration:

```text
20260507085953_InitialApplicationSchema
```

Expected tables in schema `app`:

```text
__EFMigrationsHistory
guest_chat_sessions
identity_refresh_tokens
inbox_requests
platform_ingress_bootstrap_state
platform_settings
provisioning_jobs
service_instances
space_user_password_reset_requests
stack_users
stacks
user_account_roles
user_accounts
```

Extra or missing tables, or a different migration history, are not silently
accepted.

## Safe database projection

The assessment reads:

- stack identity and status;
- service identity, image, version, runtime location, and safe route hints;
- whether managed Matrix authority exists as a Boolean;
- record counts;
- active guest-chat and password-reset counts.

It does not read or retain:

- `AdminAccessToken`;
- password hashes;
- refresh-token hashes;
- guest-token hashes;
- reset-token hashes;
- JWT signing keys.

## Path treatment

The private inventory retains canonical local paths because later capture
requires them.

Public JSON and Markdown hide absolute host paths by default. The operator may
opt in to visible paths using:

```text
--include-sensitive-paths
```

## Source fingerprint

The source fingerprint is a SHA-256 digest over stable assessment facts,
including:

- product version;
- database migration and table identities;
- row counts;
- stack and service identity;
- Docker image/container identity;
- managed labels;
- mount mappings;
- stack file sizes and modification times.

Observation timestamps and volatile process health are excluded from the
fingerprint.

A changed fingerprint invalidates a later reviewed migration plan.

## Capture eligibility

`CanProceedToCapture` is true only when:

- one exact supported v0.1.0 database is confirmed;
- Docker is available;
- Matrix services reconcile with stack file inventories;
- each required SQLite database exists;
- each required signing key exists;
- no blocker remains.

Warnings may still require operator review, but blockers fail closed.
