---
title: Retain or clean up migration resources
description: Keep the legacy source for the chosen retention period and remove packages, captures, and staging resources only at safe boundaries.
section: Migrate from MEM 0.1.0
order: 110
---

# Retain or clean up migration resources

Migration creates source captures, encrypted packages, target working material, conversion output, private staging, a normal target runtime, and durable evidence. They do not all share the same deletion boundary.

## Outcome

The accepted server and required evidence are retained, temporary resources are removed safely, and the legacy source remains available for the recorded retention period.

## Legacy source

MEM 0.2.0 never automatically deletes the old host or its Matrix data. After acceptance:

- keep it for the recorded retention duration;
- prevent accidental normal use or route ownership;
- retain the source assessment, capture, package report, and any final-frozen handoff evidence;
- document who may authorize final disposal;
- dispose of it only after your organization accepts that rollback is no longer required.

## Source Assistant material

The Source Assistant can delete a completed encrypted package and package reports. That action does not remove the source capture, assessment history, journal, live legacy data, or host.

Remove a source capture only when you no longer require it for package recreation, evidence, or rollback planning and the available Source Assistant action explicitly describes that scope.

## Target package retention

During an early cancellation, the target asks whether to retain or remove the encrypted package. Any decrypted target working package is removed. Clearing the target decryption identity means a retained encrypted package cannot simply resume the cancelled session.

For completed migrations, follow the session retention policy and preserve hashes and safe provenance even when payloads are later removed.

## Staging cleanup

Private-test and conversion resources remain migration-owned through acceptance and the first native backup. After that boundary, MEM attempts automatic cleanup.

If cleanup fails:

- do not delete containers or directories until you confirm their migration ID and ownership;
- use the explicit retry or cleanup operation shown by the workspace;
- verify that the accepted normal runtime remains healthy;
- retain the cleanup operation log.

A staging cleanup failure does not invalidate the accepted server.

## Verify success

- The production stack remains managed and healthy.
- The first native backup is retained in the Backup Catalog.
- No staging container owns public routes.
- The legacy source retention record is intact.
- Package, capture, and evidence deletion decisions are documented.

Related: [Resume, cancel, archive, and restore sessions](session-lifecycle.md).
