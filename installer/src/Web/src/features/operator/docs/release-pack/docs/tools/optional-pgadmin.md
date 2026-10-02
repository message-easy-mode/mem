---
title: Optional pgAdmin
description: Use externally managed pgAdmin only as advanced PostgreSQL inspection tooling; it is not part of the MEM 0.2.x managed platform.
section: Tools
order: 10
---

# Optional pgAdmin

pgAdmin is **not required** for MEM 0.2.x and is not currently a first-class MEM-managed platform service.

If an experienced operator chooses to run pgAdmin separately, treat it as external database-administration tooling rather than as a MEM lifecycle surface.

## Appropriate use

Use it for deliberate read-oriented investigation such as:

- confirming PostgreSQL connectivity;
- inspecting database/schema state during a known incident;
- validating a support hypothesis when MEM evidence points specifically at PostgreSQL.

## Do not use it as a shortcut

Do not edit MEM-owned database state to make a failed operation appear successful. Do not bypass backup/restore, migration, user, or stack lifecycle workflows by modifying records directly.

A direct database edit can make the Control Plane's durable state disagree with Docker resources, files, routes, operation history, or security authority.

## Access and secrets

Keep pgAdmin private and independently authenticated. Do not store or paste database administrator credentials into MEM documentation/support material.

For normal platform evidence start with MEM Diagnostics and the relevant workspace. For container-level investigation use [Portainer](optional-portainer.md) where appropriate.
