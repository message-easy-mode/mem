---
title: Install MEM 0.2.0
description: Follow the supported fresh-install path from Ubuntu bootstrap through managed-platform handoff.
section: Installation
order: 0
---

# Install MEM 0.2.0

This guide covers a **fresh MEM 0.2.0 installation** on a supported Ubuntu and Docker host.

MEM 0.2.0 uses a two-stage installation model:

1. a small host bootstrap prepares Ubuntu, Docker, the private MEM Control Plane container, its private administration path, and the one-time setup code;
2. first-owner bootstrap creates the named Platform Owner and MFA authority, then the authenticated MEM Control Plane checks the host, validates the public-domain plan, installs the managed platform, verifies the result, and hands the operator into normal Control Plane use.

> [!IMPORTANT]
> **MEM 0.2.0 does not support directly exposing the Control Plane on a publicly routable network interface.**
>
> On explicitly selected trusted private networks, direct LAN administration is supported. Everywhere else, SSH tunnelling is the supported remote-management method.

## Choose the correct path first

Use this guide for a new first-time Setup when the private MEM Control Plane has been bootstrapped but the managed MEM platform has not yet completed installation.

Stop and use MEM Migrate when the server contains the legacy `mem-api` or `mem-web` containers. The Control Plane detects those names and directs the operator away from a destructive fresh install.

If MEM-managed containers, networks, or volumes exist without a current installation record, treat the host as a repair or investigation case. Do not delete resources merely to make the fresh-install path appear.

See [Install or migrate?](../start/install-or-migrate.md) before continuing when the server has ever hosted MEM.

## Control Plane state and existing MEM 0.2.0 runtime names

A fresh MEM 0.2.0 bootstrap stores Control Plane state in the Docker volume `mem-control-plane-data`. Do not rename, copy, or replace that volume manually; it contains the persistent Control Plane state that must survive container recreation and upgrades.

Earlier MEM 0.2.0 development and preview installations used the runtime name `mem-installer` and volume `mem-installer-data`. The current bootstrap recognises that exact legacy runtime, presents a reviewed migration to `mem-control-plane`, reuses the existing volume and certificate paths, applies the current private-administration binding policy, and verifies the replacement before retiring the old container record. An upgraded installation may therefore continue using the legacy volume name `mem-installer-data` permanently.

If verification fails, bootstrap follows its reviewed rollback policy. It must not silently reopen a wildcard or publicly routable Control Plane binding merely to restore availability.

## Installation sequence

1. [Prepare Ubuntu Server for an on-premises MEM install](ubuntu-server-on-prem.md) when using a local VM/server, then [Prepare the Ubuntu host](prepare-host.md)
2. [Run the bootstrap installer](bootstrap-installer.md)
3. [Open the private Control Plane and use the setup code](private-access-and-setup-code.md)
4. [Create the first Platform Owner](first-platform-owner.md)
5. [Run the server checks](check-server.md)
6. [Choose the public domain and certificate plan](domain-and-certificate.md)
7. [Review and run the install plan](review-and-install.md)
8. [Verify the platform and complete handoff](verify-and-handoff.md)

Use [Installation troubleshooting](troubleshooting.md) when a stage does not complete.

## What a fresh install creates

The host bootstrap creates the private `mem-control-plane` runtime and its persistent state. Authenticated first-time Setup then prepares or verifies:

- the `mem-gateway` Docker network;
- PostgreSQL as `mem-postgres`;
- Nginx Proxy Manager as `mem-npm`;
- shared Coturn as the platform TURN service;
- persistent platform volumes;
- the selected wildcard certificate and its NPM import;
- selected support tools where enabled;
- durable installation, verification, and diagnostic evidence.

It does **not** create separate MEM API and Web application containers. The ASP.NET Core API, HostAgent runtime services, and built React application run together inside the private Control Plane process.

The first Matrix + Element stack is created after platform handoff.

## Security boundary

The Control Plane mounts the Docker socket and is highly privileged. MEM therefore treats its network exposure as a release security boundary.

Supported administration is:

```text
Internet/VPS
→ 127.0.0.1 only
→ SSH tunnel

Explicit trusted LAN
→ one selected RFC1918 host address
→ direct private HTTPS
```

MEM does not require a public `admin` hostname and must not create a public NPM route for the Control Plane.
