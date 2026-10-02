---
title: What MEM is
description: Understand the role of the MEM Control Plane and how it relates to Matrix, Synapse, Element, Docker, and recovery tooling.
section: Start here
order: 10
---

# What MEM is

MEM is an independent open-source **operations, recovery, and migration control plane** for self-hosted Matrix environments. It does not define a new messaging protocol and it does not replace Matrix.

## The underlying communications system

A normal MEM-managed chat server uses familiar upstream components:

- **Matrix** provides the open communications protocol.
- **Synapse** provides the Matrix homeserver.
- **Element Web** provides the browser messaging client.
- **PostgreSQL** stores Synapse server data.
- **Nginx Proxy Manager** provides HTTPS ingress and public routing.
- **coturn** provides TURN relay service for voice and video.

MEM coordinates those components and records the operator workflows around them.

## The Control Plane

The MEM Control Plane is the authoritative administration application. In MEM 0.2.0, the React interface, ASP.NET Core API, authentication, durable workflow state, and privileged HostAgent services operate as one control-plane application. HostAgent is an in-process privileged runtime library, not a separate remote daemon.

The control plane can inspect and change MEM-owned Docker resources, write approved host-side configuration, provision databases, create or verify routes, control workloads, and retain operation evidence.

Because it has access to the Docker socket and MEM data roots, it is highly privileged. Keep it private and protect operator accounts with MFA and appropriate roles.

## The three planes

1. **Control plane** — operator identity, workflows, expected state, diagnostics, and privileged orchestration.
2. **Managed Matrix data plane** — Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn, and per-stack data.
3. **Recovery and migration plane** — Backup Catalog, Restore Workspace, portable artifacts, MEM Migrate, private staging, and production adoption.

Ordinary Matrix users do not send messages through the administration UI.

## What MEM adds

MEM adds controlled workflows around infrastructure that would otherwise be maintained through a mixture of Compose files, shell commands, database commands, YAML, proxy configuration, and operator memory.

Its purpose is not to make the infrastructure invisible. Its purpose is to make ownership, intent, changes, verification, recovery, and failure evidence easier to understand.
