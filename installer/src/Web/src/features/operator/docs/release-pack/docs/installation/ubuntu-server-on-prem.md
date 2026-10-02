---
title: Prepare Ubuntu Server for an on-premises MEM install
description: Prepare Ubuntu Server storage, networking, DNS, and firewalling for a production-shaped on-premises MEM installation.
section: Installation
order: 5
---

# Prepare Ubuntu Server for an on-premises MEM install

This page records the practical host preparation that matters on a real Ubuntu Server before MEM is installed. It complements [Prepare the Ubuntu host](prepare-host.md) with the storage and LAN details that are easy to miss during a fresh VM installation.

## Recommended starting point

For the normal MEM 0.2.0 release path use:

- Ubuntu Server **24.04 LTS** on `amd64` / `x86_64`;
- at least **3,500 MB RAM** to pass bootstrap, with about **7,800 MB** recommended;
- at least **20,000 MB free on `/`** to pass bootstrap, with about **50,000 MB free** recommended;
- a stable private server address when using Trusted LAN administration;
- working public DNS and, for an on-premises deployment, a deliberate internal DNS plan.

## Ubuntu guided LVM: make sure `/` actually uses the disk

Ubuntu Server's guided LVM layout can place most of a virtual disk in the volume group while assigning only part of it to the root logical volume. A 50 GB test VM can therefore look roughly like this:

```text
virtual disk                 50 GB
ubuntu-vg                    ~48 GB
ubuntu-lv mounted at /       ~24 GB
free space inside ubuntu-vg  ~24 GB
```

MEM checks the free space visible on `/`. Space left unused inside the volume group does not count toward that check.

On the Ubuntu **Storage configuration** screen, inspect the logical volume mounted at `/` before selecting **Done**. For a simple dedicated MEM server, edit `ubuntu-lv` so it uses essentially the full available `ubuntu-vg` capacity.

After installation, verify:

```bash
df -hT /
sudo vgs
sudo lvs
```

For a dedicated server where the root logical volume was already created too small and the volume group still has free extents, the standard LVM expansion is:

```bash
sudo lvextend -l +100%FREE -r /dev/ubuntu-vg/ubuntu-lv
```

Review the device names before running that command. Do not copy it blindly to a host with a different LVM layout.

## Production filesystem model

A supported production installation deliberately separates private Control Plane state, host-visible runtime data, and installed software:

```text
/data
    private Control Plane state inside the persistent
    mem-control-plane-data Docker volume

/var/lib/message-easy-mode
    server-owned host-visible MEM data
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installed MEM software and runtime payloads
```

The official production runtime identity-mounts `/var/lib/message-easy-mode` into the Control Plane at the same absolute path so the Control Plane and the host Docker daemon address the same physical stack files.

A fresh production install must not depend on the login account's home directory for new runtime data. Do not redirect production stack storage to `/home/<user>/mem-data`.

## Private Control Plane access

For an on-premises server, choose one of the supported private administration modes:

- **Trusted LAN** — bind the Control Plane to one explicitly selected RFC1918 address;
- **SSH / local-only** — bind it to loopback and use SSH port forwarding.

Do not publish the Control Plane port through public DNS, Internet NAT, or Nginx Proxy Manager.

## Public service ports

The managed public service normally needs:

```text
443/tcp                HTTPS Matrix and Element
3478/tcp               TURN
3478/udp               TURN
49160-49200/udp        TURN relay range
```

TCP 80 is optional when you deliberately want an HTTP/redirect path. The current guided deSEC DNS-01 certificate flow does not require public port 80 for ACME issuance.

Nginx Proxy Manager administration on TCP 81 and the MEM Control Plane on TCP 8443 are administration surfaces and should remain private/restricted.

## DNS and split DNS

Public DNS should point Matrix, Element, and TURN names at the address through which Internet clients can reach the server.

On an internal LAN it is often useful to use split DNS so the same service hostname resolves directly to the server's private address, for example:

```text
public DNS
turn.example.org  -> public WAN address

internal resolver
turn.example.org  -> 10.10.0.198
```

This avoids making local service checks depend on NAT reflection/hairpin behaviour.

A DNS override only changes name resolution. It does **not** open firewall ports or configure NAT/port forwarding.

## Before running MEM

Confirm the basics from the new server itself:

```bash
. /etc/os-release
echo "$PRETTY_NAME"
dpkg --print-architecture
df -hT /
ip -4 -br addr
getent ahostsv4 turn.example.org
```

When Docker is not already installed, that is expected on a genuinely fresh host; the MEM bootstrap can install the supported Docker packages.
