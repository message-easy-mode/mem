---
title: Requirements and supported environment
description: Review the actual bootstrap admission floor, Ubuntu target, Docker, storage, networking, DNS, and operator-access expectations before installing MEM.
section: Start here
order: 50
---

# Requirements and supported environment

MEM performs several checks, but the operator should prepare the host against the **bootstrap admission floor**, not only the lighter browser preflight warnings.

## Primary release target

The normal MEM 0.2.0 release target is **Ubuntu Server 24.04 LTS on amd64/x86_64**.

The bootstrap can recognise other supported Ubuntu LTS versions and architectures, but passing architecture detection does not prove every upstream image or release artifact exists for that architecture. Use the release capability matrix before relying on ARM.

## Bootstrap admission floor

The official bootstrap currently uses:

- fewer than **2 CPU cores** → warning;
- less than **3,500 MB RAM** → hard stop;
- about **7,800 MB RAM** → recommended level;
- less than **20,000 MB free on `/`** → hard stop;
- about **50,000 MB free on `/`** → recommended level.

The later browser preflight may display lower warning thresholds for early evaluation. Those do not replace the bootstrap floor and are not production-sizing guidance.

Synapse history, media, PostgreSQL, images, logs, diagnostics, backups, restores, and migration workspaces can consume substantially more capacity.

For Ubuntu Server LVM and disk-allocation guidance, see [Prepare Ubuntu Server for an on-premises MEM install](../installation/ubuntu-server-on-prem.md).

## Docker

Docker must be installed and reachable by the Control Plane through the Docker socket. The bootstrap can install the supported Docker Engine and Compose plugin packages on a fresh Ubuntu host.

Docker-socket access is highly privileged. Keep the Control Plane on a trusted LAN, VPN, or SSH-forwarded path rather than exposing it publicly.

## Network and public services

Normal public Matrix and Element HTTPS needs TCP **443**. TCP **80** is optional when you deliberately want an HTTP/redirect topology; the current guided deSEC DNS-01 certificate path does not require public port 80 for certificate issuance. Production TURN additionally uses:

```text
3478/tcp
3478/udp
49160-49200/udp
```

The MEM Control Plane, NPM administration, PostgreSQL, and other management/data ports are private surfaces and should not be opened broadly to the Internet.

## Domain, DNS, and certificates

Prepare a domain, DNS provider access, and an ACME contact email. The current guided path uses Nginx Proxy Manager and deSEC DNS-01 for wildcard certificate issuance.

For an on-premises deployment, decide whether internal clients should use split DNS so public service names resolve directly to the private server address on the LAN.

## Operator workstation and backups

Browser administration requires access to the private Control Plane. On Linux, MEM CLI named-device login uses `secret-tool` and a working Secret Service-compatible keyring for the invoking non-root user.

Keep at least one portable backup copy outside the MEM host. A backup stored only on the failed server disk is not disaster recovery.
