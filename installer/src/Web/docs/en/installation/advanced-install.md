---
id: "installation/advanced-install"
translationKey: "installation/advanced-install"
locale: "en"
groupId: "installation"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Advanced bootstrap options"
description: "Use supported bootstrap overrides for development, release validation, custom private ports, or preinstalled Docker."
order: 25
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Advanced", "bootstrap", "development", "Docker", "CLI"]
route: "/docs/installation/advanced-install"
aliases: []
outputPath: "docs/installation/advanced-install.md"
preserveLegacyBranding: false
---

# Advanced bootstrap options

Use the standard stable bootstrap unless you are developing MEM, validating a release package, or deliberately changing the private Control Plane entry point.

## Change the private administration mode

SSH/local-only is the default and safest general mode:

```bash
sudo ./install.sh --control-plane-access ssh
```

Trusted LAN is explicit:

```bash
sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

The Trusted-LAN address must be a concrete RFC1918 IPv4 address assigned to an eligible host interface. MEM does not accept wildcard or publicly routable Control Plane bindings.

## Change the private Control Plane port

```bash
sudo ./install.sh --control-plane-port 9443
```

The selected host address and port map to HTTPS port 8443 inside `mem-control-plane`. Update the SSH tunnel or Trusted-LAN browser URL accordingly.

Changing the port does not permit public exposure.

## Change an existing private binding

Re-running bootstrap preserves an existing safe loopback or Trusted-LAN address unless an explicit administration-mode change is requested.

A requested change is performed as reviewed container recreation with retained persistent state and post-start host-binding verification. MEM does not mutate a live container's published port in place.

## Stable and dev channels

```bash
sudo ./install.sh --channel stable
sudo ./install.sh --channel dev
```

`stable` is for normal releases. `dev` is for controlled validation.

## Override the Control Plane image

```bash
sudo ./install.sh --control-plane-image mem-control-plane:local
```

Use this for development/release proof only and verify image provenance.

## Other development/release options

Supported advanced flags include CLI payload overrides, `--skip-docker-install`, `--allow-low-disk`, `--use-docker-convenience-script`, `--dry-run`, and `--yes`.

`--yes` accepts reviewed mutation prompts; it does not bypass private-address validation.

## Unsupported advanced paths

MEM 0.2.0 does not support:

- `0.0.0.0:<port>` or `[::]:<port>` Control Plane publication;
- direct Control Plane binding to a public/non-private IP;
- a public MEM administration hostname or NPM proxy route;
- legacy `.env` / `stack.sh` application deployment;
- separate manual `mem-api` and `mem-web` starts;
- bypassing the durable installation plan or exposure validation.

Source modifications can create a custom deployment, but that custom environment is outside the supported MEM 0.2.0 private-administration contract.
