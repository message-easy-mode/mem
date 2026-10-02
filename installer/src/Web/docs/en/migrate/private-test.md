---
id: "migrate/private-test"
translationKey: "migrate/private-test"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Convert and run the private test"
description: "Convert the legacy payload into a migration-owned candidate and verify it in private Matrix and Element runtimes."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["conversion", "private test", "staging", "candidate", "no public routes"]
route: "/docs/migrate/private-test"
aliases: []
outputPath: "docs/migrate/private-test.md"
preserveLegacyBranding: false
---
# Convert and run the private test

The target converts the validated legacy payload into a migration-owned candidate before creating the normal production server.

## Outcome

Conversion completes successfully and the candidate runs as private Matrix and Element services with no public routes. The operator verifies that the captured server can start and that the expected data is present.

## Run conversion

In **Prepare and test**, start conversion. MEM runs the current migration worker contract, validates its structured progress and output hashes, and records a durable conversion attempt.

Conversion may take time. It continues on the server if you leave or refresh the page. Return to the same Migration Session to review progress, logs, warnings, and the final result.

A failed conversion does not create a normal chat server. Correct the reported issue or produce a new compatible package rather than copying partial output into a stack.

## Migration-owned candidate boundary

Successful conversion creates a verified candidate owned by the Migration Session. It is not:

- a Backup Catalog entry;
- a Restore Workspace;
- a normal managed runtime;
- publicly reachable.

Raw packages, temporary conversion files, failed candidates, and unaccepted candidates remain inside the migration bounded context.

## Start the private test

Create the private staging runtime. MEM imports the converted database, starts private Matrix and Element services, and verifies the candidate on an internal Docker network.

The private test must create **no public Matrix or Element routes**. It must not take ownership of the production hostname.

Review the available checks, including:

- database import and schema readiness;
- Synapse startup and health;
- Element configuration and private access;
- expected source identity;
- representative user, room, event, and media evidence exposed by the workspace;
- absence of public route ownership.

## Failure handling

If private staging fails, inspect conversion and staging logs before retrying. Do not proceed to target creation while the private runtime is unhealthy or its identity is uncertain.

A refresh does not cancel a running staging operation. Use the explicit operation controls where cancellation is supported.

## Retention boundary

The private candidate and staging resources remain migration-owned. MEM normally retains them through acceptance and the first native backup, then attempts cleanup. A cleanup failure can be retried without invalidating the accepted migration.

Next: [Create the new server privately](create-new-server.md).
