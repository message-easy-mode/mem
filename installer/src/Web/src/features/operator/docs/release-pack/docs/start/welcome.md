---
title: Welcome to MEM
description: Start here to understand MEM 0.2.0, choose the correct path, and find the first operator tasks.
section: Start here
order: 0
---

# Welcome to MEM

![Message Easy Mode logo](../../assets/brand/mem-logo-docs.png)

MEM means **Message Easy Mode**. MEM 0.2.0 is an open-source control plane for operating self-hosted Matrix and Element services on a conventional Linux and Docker host.

MEM is intended for technically capable operators who want ownership of their communications service without assembling every database, proxy route, certificate, backup procedure, and migration step by hand.

> [!IMPORTANT]
> MEM is the management layer. Matrix remains the communications protocol, Synapse remains the homeserver, and Element remains the primary web client.

## What MEM helps you do

The MEM Control Plane brings the main operator workflows into one private interface:

- prepare and verify the host;
- configure domains and certificates;
- create and inspect Matrix chat servers;
- manage users, federation, and TURN connectivity;
- create and catalogue backups;
- test and perform restores;
- migrate a supported legacy MEM 0.1.0 installation;
- inspect diagnostics, incidents, runtime evidence, and support reports.

The normal public traffic path goes to Element, Synapse, and TURN. The MEM administration surface should remain private.

## Choose your next page

- [What MEM is](what-is-mem.md)
- [Is MEM right for you?](is-mem-right-for-you.md)
- [Install or migrate?](install-or-migrate.md)
- [Requirements and supported environment](requirements.md)
- [MEM product packages](packages.md)
- [Known limitations](known-limitations.md)
- [MEM glossary](glossary.md)

## Documentation status

This **Start here** section is written for MEM 0.2.x and is grounded in the current control-plane, setup, runtime, CLI, migration, recovery, and diagnostics contracts.

The current non-legacy documentation tree is reviewed for MEM 0.2.x. Historical pre-0.2 material is preserved explicitly as legacy rather than mixed into current operating guidance.
