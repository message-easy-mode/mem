---
title: Prepare the Ubuntu host
description: Prepare Ubuntu, resources, Docker, ports, DNS, and private operator access before bootstrap.
section: Installation
order: 10
---

# Prepare the Ubuntu host

Prepare the host before running the MEM bootstrap. The bootstrap performs its own checks and may install missing packages, but it should not be the first time you consider storage, ports, DNS, or private access.

## Supported bootstrap host

The supplied MEM 0.2.0 bootstrap currently accepts:

- Ubuntu 22.04, 24.04, or 26.04;
- `amd64` / `x86_64`;
- `arm64` / `aarch64` only when every required release image is available for that architecture.

Ubuntu 24.04 LTS is the normal release target.

The script must run as root through `sudo`.

## Bootstrap resource requirements

The bootstrap applies a stricter admission floor than the later browser preflight:

- **2 CPU cores** are recommended; fewer produces a warning;
- **3,500 MB RAM** is the hard bootstrap minimum;
- approximately **7,800 MB RAM** is recommended;
- **20,000 MB free disk** on `/` is the hard bootstrap minimum;
- approximately **50,000 MB free disk** is recommended.

The browser preflight also reports lower early-testing warning thresholds. Passing those later checks does not override the bootstrap floor or constitute production sizing.

Matrix history, media, PostgreSQL, container images, diagnostics, backups, restores, and migration workspaces can grow well beyond these minimums.

## Ubuntu Server storage check

On Ubuntu Server with guided LVM, confirm that the logical volume mounted at `/` actually uses the intended disk capacity. A 50 GB disk can otherwise leave roughly half of the volume group unassigned while `/` receives only about 24 GB. MEM correctly evaluates the space visible on `/`, not unused LVM extents.

Before completing Ubuntu installation, edit `ubuntu-lv` on the Storage configuration screen so a simple dedicated MEM server uses essentially the full `ubuntu-vg`. After installation, verify with `df -hT /`, `sudo vgs`, and `sudo lvs`.

See [Prepare Ubuntu Server for an on-premises MEM install](ubuntu-server-on-prem.md) for the full LVM, filesystem, firewall, and split-DNS guidance.

## Production data locations

The official server model separates:

```text
/data                         private Control Plane Docker-volume state
/var/lib/message-easy-mode    host-visible MEM operational data
/opt/mem                      installed software/runtime payloads
```

New production stack data must not depend on the installer account's home directory.

## Host packages

The bootstrap checks and can install:

```text
ca-certificates
curl
gnupg
lsb-release
jq
dnsutils
iproute2
net-tools
libsecret-tools
```

`libsecret-tools` provides `secret-tool`, which the Linux MEM CLI uses with a Secret Service-compatible keyring.

## Docker

The bootstrap can install Docker Engine, containerd, Buildx, and the Docker Compose plugin from Docker's official Ubuntu apt repository.

When Docker is already installed, confirm:

```bash
sudo docker info
sudo docker compose version
```

Use `--skip-docker-install` only when Docker and Compose are already present and you want the bootstrap to fail rather than modify them.

The convenience-script Docker path exists for development and testing. The official apt repository path is the normal installation method.

## Ports and private access

Plan for:

- the private installer port, default TCP **8443**;
- public HTTPS on TCP **443** for Matrix and Element through Nginx Proxy Manager;
- optional public TCP **80** only when you deliberately want an HTTP/redirect topology; the guided deSEC DNS-01 certificate path does not require port 80 for issuance;
- NPM administration on TCP **81**, which should also be restricted;
- TURN TCP/UDP **3478** and UDP relay ports **49160-49200** for production voice/video relay.

The browser preflight also watches 8080 and 5432 for unexpected listeners. Keep PostgreSQL and other administration/data ports private.

Before installation, decide how the operator will reach the private port:

- trusted LAN;
- Tailscale, WireGuard, or another VPN;
- an approved management network;
- SSH local forwarding.

Do not rely on a public IP and a secret URL as the security boundary.

## On-premises DNS and NAT

For public services, DNS must point at the address through which clients can reach the server. On an internal LAN, split DNS can deliberately resolve the same TURN/Matrix/Element names directly to the private server address. This avoids making local functional checks depend on NAT reflection.

DNS does not open ports or create NAT rules. If the server is behind a firewall/router, public TURN reachability still requires the appropriate firewall/NAT policy for TCP/UDP 3478 and UDP 49160-49200.

## Domain and DNS

Prepare:

- a base domain you control;
- a deSEC account and token with permission for that DNS zone;
- an ACME contact email;
- control of the public A/AAAA records that future Matrix and Element hostnames will use.

The guided certificate flow currently supports deSEC DNS-01 and a wildcard certificate. Other DNS providers and existing reverse proxies are not first-class guided paths in MEM 0.2.0.

## Before changing an existing host

Record the existing Docker state:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

If you see `mem-api`, `mem-web`, or an older Matrix Easy Mode Compose deployment, stop and use the migration path. If you see other `mem-*` resources, investigate ownership before continuing.
