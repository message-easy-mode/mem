---
title: Create the new server privately
description: Choose the target stack identity, pass conflict checks, and create the normal MEM runtime without publishing it.
section: Migrate from MEM 0.1.0
order: 70
---

# Create the new server privately

After a successful private test, MEM can materialize the candidate as a normal managed chat server while keeping it private.

## Outcome

The target has a privately healthy normal MEM runtime with preserved Matrix identity, a chosen stack name and Element hostname, and no public route ownership.

## Identity choices

The migrated Matrix public address is locked to the captured source identity. Changing it would create a different Matrix server and is not part of this migration.

Choose and review:

- the normal MEM stack name or slug;
- the preserved Matrix hostname;
- the Element hostname to publish;
- the target database and runtime ownership;
- whether the stack will use the target’s normal shared services and policies.

## Run target preflight

Before mutation, MEM checks for conflicts involving:

- stack name and database identity;
- Matrix and Element hostnames;
- host paths and retained migration workspaces;
- container names and runtime ownership;
- existing public routes;
- other active migration or restore claims.

Resolve a conflict deliberately. Do not delete an existing production stack or route merely to make the preflight green unless you have proven it is obsolete and have a separate recovery path.

## Choose the source-authority posture

The normal guided path uses the verified package and private test as the authoritative source snapshot. It is simpler, but changes made on the legacy server after capture are not included and formal source-freeze/source-restoration evidence is reduced.

The advanced **final-frozen** path creates a final package after formally freezing the source. It provides stronger drift and rollback evidence but requires additional source-side handoff steps. Read [Rollback boundaries](rollback.md) before choosing it.

## Create the server

Complete the confirmation and step-up prompts. MEM creates the normal stack database, Matrix and Element services, configuration, and ownership records. It does not publish the public routes during this step.

Verify that the new normal runtime is healthy and private. Do not proceed if public route ownership is already ambiguous.

## Source usage boundary

For the normal snapshot path, arrange to stop normal use of the old server before go-live. For the final-frozen path, keep the source frozen. In both cases, retain the old host until production verification and acceptance are complete.

Next: [Make the new server live and verify production](go-live.md).
