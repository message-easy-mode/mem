---
id: "architecture"
translationKey: "architecture"
locale: "en"
groupId: "architecture"
groupKey: "architecture"
groupLabel: "Architecture"
groupOrder: 20
title: "Architecture"
description: "High-level architecture of the MEM 0.2.x Control Plane, shared platform services, managed Matrix/Element stacks, and production data boundaries."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Control Plane", "Docker", "Nginx Proxy Manager", "PostgreSQL", "Coturn", "Matrix", "Element"]
route: "/docs/architecture"
aliases: []
outputPath: "docs/architecture/index.md"
preserveLegacyBranding: false
---
# Architecture

MEM 0.2.x is a private **Control Plane** that manages shared platform services and one or more Matrix + Element runtime stacks on an operator-owned Docker host.

## Main runtime layers

```text
private administration
    mem-control-plane

shared platform
    mem-postgres
    mem-npm
    mem-coturn
    optional support tools such as Portainer / Seq

managed chat stacks
    mem-matrix-<slug>
    mem-element-<slug>
    stack PostgreSQL database/role
    NPM routes
    stack files and operation evidence
```

The Control Plane owns Docker orchestration authority through the host Docker socket. That is why its network exposure is deliberately private.

## Private and public traffic are different boundaries

The Control Plane should be reachable only through an explicitly selected Trusted LAN address, VPN, or SSH/local-only path.

Public traffic goes instead to managed services:

```text
Internet / client
    -> 80/443 -> Nginx Proxy Manager -> Matrix / Element
    -> TURN ports -> shared coturn
```

MEM does not require a public admin hostname for the Control Plane.

## `mem-gateway`

Managed platform and stack services communicate on the Docker network `mem-gateway`. NPM carries the `npm` alias there. A containerized Control Plane that must administer NPM also needs membership in that network.

Browser links must never expose internal Docker-only names merely because the server can resolve them.

## Production data boundaries

The production filesystem has three distinct roles:

```text
/data
    private persistent Control Plane volume state

/var/lib/message-easy-mode
    host-visible MEM operational data
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installed MEM software and runtime payloads
```

`/var/lib/message-easy-mode` is bind-mounted into the Control Plane at the same absolute path. This identity-mount is intentional: when MEM passes a stack path to the host Docker daemon, both namespaces must refer to the same physical file.

## One stack

A managed stack contains a Synapse homeserver and Element web client plus stack-specific configuration, database identity, public routes, secrets, readiness evidence, and operation history. PostgreSQL, NPM, and Coturn are shared platform services.

Matrix accounts are separate from MEM Control Plane operator accounts.

## Runtime identity

The running Control Plane exposes `/health/runtime` with durable and process identity:

- `ControlPlaneInstanceId` identifies the persistent Control Plane authority;
- `ApiProcessInstanceId` changes when the API/container process is recreated;
- `runtimeMode` distinguishes development from canonical production;
- `validationState` reports whether the current runtime contract is valid.

A supported container recreation should preserve durable Control Plane state while rotating the API process identity.

## Development versus production

Development may use repository-scoped paths and the `mem-env` harness. Production uses server-owned paths and the official bootstrap. The semantic contract is the same: Docker-host paths and Control-Plane-visible paths must agree whenever both sides need the same resource.

See [Network behavior](../operations/network-behavior.md), [Configuration](../operations/configuration.md), and [Prepare Ubuntu Server for an on-premises MEM install](../installation/ubuntu-server-on-prem.md).
