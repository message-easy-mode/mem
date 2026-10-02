---
id: "start/glossary"
translationKey: "start/glossary"
locale: "en"
groupId: "start"
groupKey: "start"
groupLabel: "Start here"
groupOrder: 0
title: "MEM glossary"
description: "Learn the product, Matrix, recovery, migration, security, and diagnostics terms used throughout MEM."
order: 70
status: "supported"
appliesTo: ["0.2.x"]
tags: ["glossary", "terminology", "Backup Catalog", "Restore Workspace", "federation", "TURN"]
route: "/docs/start/glossary"
aliases: []
outputPath: "docs/start/glossary.md"
preserveLegacyBranding: false
---
# MEM glossary

## Product and operator terms

**MEM** — Message Easy Mode, the project and product family.

**MEM Control Plane** — the private Web and API application that owns operator identity, workflow state, orchestration, diagnostics, and access to MEM-managed Docker and host resources.

**Operator** — a named person authorised to use the Control Plane. Capabilities depend on roles and authentication state.

**Step-up verification** — fresh identity verification required before selected high-risk actions.

**Stack / chat server** — one independently managed Matrix environment, normally with its own Synapse, Element, database, identity, media, configuration, and public hostnames.

## Matrix runtime terms

**Matrix** — the open protocol used by MEM 0.2.0 for messaging and federation.

**Synapse** — the Matrix homeserver implementation managed for each stack.

**Element** — the primary Matrix client supplied as the stack's Web client.

**Federation** — server-to-server communication between independent Matrix homeservers.

**TURN / coturn** — TURN relays call media when peers cannot connect directly; coturn is the shared server implementation.

**Nginx Proxy Manager / NPM** — the supported ingress component mapping public HTTPS hostnames to services and certificates.

## Recovery terms

**Backup Catalog entry** — the durable inventory identity for one recovery source, including provenance, payload state, and integrity.

**Portable backup** — an exportable archive designed for transfer and import.

**Restore attempt** — the durable workflow record for one recovery operation.

**Restore Workspace** — the UI and server state for stages, logs, evidence, private testing, recreation, and completion.

**Private test** — a recovery or migration candidate started without taking production public routes.

**Standard Recreate** — the normal workflow for recreating a stack from a validated Backup Catalog entry.

**Target claim** — a durable reservation preventing conflicting restore attempts from taking the same identity or hostnames.

## Migration and diagnostics terms

**MEM Migrate** — the separate product containing source-version-specific migration knowledge.

**Source Assistant** — the temporary local Web application on the old server.

**Target request** — the target-generated request containing destination identity and encryption recipient.

**Migration package** — the encrypted portable package transferred from source to target.

**Candidate artifact** — a validated conversion output that can be privately staged.

**Production adoption** — the controlled operation giving a verified candidate its production identity and routes.

**Incident** — an operator-relevant failure context with a stable identifier and safe correlated evidence.

**Safe diagnostic event** — a bounded, redacted event suitable for browser APIs and support reports.

**Technical log** — richer Serilog output for console, persistent CLEF, and optional Seq; not automatically browser-safe.

**Support report** — bounded JSON containing safe incident, operation, expected-versus-observed, and optional Docker evidence.

**Runtime reconciliation** — comparison and repair of expected MEM state against observed filesystem and Docker state.
