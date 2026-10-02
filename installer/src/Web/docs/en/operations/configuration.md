---
id: "operations/configuration"
translationKey: "operations/configuration"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Configuration"
description: "Understand the supported MEM 0.2.x configuration boundaries without editing generated containers or legacy .env deployment files."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["configuration", "Control Plane", "runtime", "paths", "Docker"]
route: "/docs/operations/configuration"
aliases: []
outputPath: "docs/operations/configuration.md"
preserveLegacyBranding: false
---
# Configuration

MEM 0.2.x is configured through the official bootstrap, first-time Setup, server-owned runtime configuration, and supported Control Plane settings. The current product is not operated by hand-editing a legacy deployment `.env` file.

## Bootstrap-owned configuration

The bootstrap establishes the private Control Plane boundary, image identity, persistent volume, host-data bind, certificate, and runtime environment.

For production host-visible data, the canonical root is:

```text
/var/lib/message-easy-mode
```

and the current production children include:

```text
mem-data
instances
platform/coturn
seq
```

The private Control Plane state remains under `/data` inside the persistent `mem-control-plane-data` volume.

## Setup-owned configuration

First-time Setup owns the managed platform plan: domain, certificate workflow, PostgreSQL, NPM, shared Coturn, support tools, and final verification.

After Setup completes, normal changes belong to the corresponding MEM workspace rather than to manual Docker or configuration-file edits.

## Runtime configuration is authoritative

Use `/health/runtime`, System Information, Diagnostics, and the relevant service/stack pages to establish the currently running mode and identity. Do not infer runtime truth from a port number or container name alone.

## Do not edit generated stack files as a normal workflow

Synapse and Element configuration under the managed instance root are MEM-owned operational resources. Manual edits can create drift that MEM may refuse to overwrite.

For supported changes use the appropriate stack, TURN, federation, backup/restore, or migration workflow.

## Secrets

Do not put passwords, TOTP secrets, recovery codes, TURN shared secrets, private keys, access tokens, or unrestricted connection strings into documentation notes, support tickets, or command history.

## Development overrides

The developer harness deliberately supplies repository-scoped paths and runtime-specific networking. Those are development contracts and must not be copied into an official server installation.
