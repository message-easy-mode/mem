---
title: Tools
description: Optional operator tools that complement MEM 0.2.x without replacing Control Plane workflows or Diagnostics.
section: Tools
order: 5
---

# Tools

MEM's normal operating model is the private Control Plane, durable operations, Diagnostics, and the MEM CLI. Optional tools can provide lower-level evidence but should not become an alternate product control plane.

## Portainer

[Use Portainer for advanced container diagnostics](optional-portainer.md) when MEM provides a supported private handoff or when an experienced operator needs bounded Docker inspection.

## Seq

Seq is an optional structured-log workspace managed through MEM's Diagnostics/Seq workflow where supported.

## pgAdmin

[Optional pgAdmin](optional-pgadmin.md) is external advanced database tooling. MEM 0.2.x does not require or manage pgAdmin as part of the normal platform install.

## Safety boundary

Use these tools to understand a problem before mutation. Prefer MEM's supported workflow for lifecycle changes, backup/restore, service repair, routes, TURN, and stack operations.
