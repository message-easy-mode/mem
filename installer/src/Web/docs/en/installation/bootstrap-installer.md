---
id: "installation/bootstrap-installer"
translationKey: "installation/bootstrap-installer"
locale: "en"
groupId: "installation"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Run the bootstrap installer"
description: "Use the MEM host bootstrap to validate Ubuntu, install Docker prerequisites, and start the private Control Plane."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["bootstrap", "Docker", "installer", "setup code"]
route: "/docs/installation/bootstrap-installer"
aliases: []
outputPath: "docs/installation/bootstrap-installer.md"
preserveLegacyBranding: false
---

# Run the bootstrap installer

The host bootstrap prepares the machine and starts the private MEM Control Plane. The browser workflow performs the managed-platform installation afterward.

## Run a dry check first

```bash
sudo ./install.sh --dry-run
```

The dry run checks the operating system, architecture, CPU, memory, disk, packages, Docker, Compose, current Control Plane state, and private administration plan without modifying the host.

## Start the stable Control Plane

```bash
sudo ./install.sh
```

The normal bootstrap can:

1. validate Ubuntu and architecture;
2. check resources and basic network access;
3. install missing prerequisites and Docker when required;
4. create or preserve Control Plane persistent state;
5. create the long-lived self-signed Control Plane certificate;
6. generate and persist a high-entropy `mem_...` setup code;
7. select the private Control Plane administration boundary;
8. start `mem-control-plane` with the Docker socket and persistent `/data` state;
9. verify health and the exact Docker host binding;
10. print the safe browser/SSH handoff and certificate SHA-256 fingerprint.

## Private administration choice

The default is SSH/local-only:

```text
127.0.0.1:8443 -> 8443/tcp
```

When interactive bootstrap detects eligible RFC1918 addresses, it can also offer an explicit Trusted-LAN choice. MEM binds only to the selected address, never to every interface.

Examples for non-interactive/release use:

```bash
sudo ./install.sh --control-plane-access ssh

sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

`0.0.0.0`, `[::]`, public IPv4 addresses, link-local addresses, and arbitrary unassigned private addresses are not supported Control Plane bindings.

## Existing Control Plane behaviour

Bootstrap inspects the actual Docker `HostIp` for an existing Control Plane.

- a safe loopback binding is preserved;
- a safe assigned Trusted-LAN binding is preserved unless the operator explicitly changes it;
- wildcard, public, stale-private, ambiguous, or otherwise unsupported bindings require reviewed container recreation on a supported private address;
- persistent `/data`, certificate material, setup authority, and host-data state are retained;
- replacement health and the exact Docker host binding are verified before the previous container record is retired.

If a hardening migration fails, a previously safe private runtime may be restarted. An unsafe previous wildcard/public runtime is retained for recovery but is left stopped rather than being automatically re-exposed.

## Useful options

```bash
sudo ./install.sh --dry-run
sudo ./install.sh --yes
sudo ./install.sh --control-plane-access ssh
sudo ./install.sh --control-plane-access trusted-lan --control-plane-bind-address 192.168.10.20
sudo ./install.sh --control-plane-port 9443
sudo ./install.sh --show-setup-token
```

Development/release options also include `--channel`, `--control-plane-image`, CLI payload options, `--skip-docker-install`, `--allow-low-disk`, and the Docker convenience-script override.

Do not use development overrides for a normal production installation.

## Bootstrap transcript and failure evidence

Real bootstrap runs keep bounded root-only evidence under `/var/log/mem/bootstrap/`. Setup codes and common credential/token patterns are redacted. The complete container environment and private key are not captured.

A `--dry-run` deliberately creates no persistent bootstrap transcript.

## What MEM does not do

The supported 0.2.0 bootstrap does not:

- publish the Control Plane on a publicly routable interface;
- create a public NPM proxy host for MEM administration;
- require public MEM admin DNS;
- restore the old 0.1.0 `.env` / `stack.sh` / separate `mem-api` + `mem-web` deployment model.
