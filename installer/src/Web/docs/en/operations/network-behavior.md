---
id: "operations/network-behavior"
translationKey: "operations/network-behavior"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Network behavior"
description: "Understand private Control Plane exposure, mem-gateway service routing, public Matrix/Element ingress, TURN ports, and split-DNS behavior in MEM 0.2.x."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["network", "Trusted LAN", "SSH", "NPM", "mem-gateway", "TURN", "DNS"]
route: "/docs/operations/network-behavior"
aliases: []
outputPath: "docs/operations/network-behavior.md"
preserveLegacyBranding: false
---
# Network behavior

MEM 0.2.x deliberately separates **private administration** from **public chat traffic**.

## Control Plane

Supported administration shapes are:

```text
SSH / local-only
    127.0.0.1:<control-plane-port>

Trusted LAN
    one selected RFC1918 host address:<control-plane-port>
```

The Control Plane is not a public NPM route and must not be published through Internet-facing NAT or public DNS.

## Public Matrix and Element

Nginx Proxy Manager handles public HTTP/HTTPS routes for managed stacks. Public Matrix and Element HTTPS uses TCP **443**. TCP **80** is optional when the operator deliberately enables an HTTP/redirect path; the current guided deSEC DNS-01 certificate path does not require public port 80 for certificate issuance.

Inside Docker, NPM participates in `mem-gateway` and uses the alias `npm`. The Control Plane must share that managed network when it performs NPM administration.

## TURN

The production coturn service publishes:

```text
3478/tcp
3478/udp
49160-49200/udp
```

If the host is behind NAT, firewall/router policy must make the required public TURN paths reachable. DNS does not create those rules.

## Split DNS

For on-premises installations, internal and external resolvers may intentionally answer the same service name differently:

```text
public:  turn.example.org -> WAN address
inside:  turn.example.org -> private MEM address
```

This is useful for direct LAN service access and for avoiding accidental dependence on NAT hairpinning during local functional checks.

## Browser boundary

A Docker hostname such as `npm`, `mem-npm`, or a stack container name is an internal server authority. The browser should receive only a supported private Control Plane URL or public Matrix/Element URL.

## Troubleshooting

Use server-authored Diagnostics and support reports first. If stack route publication reports `Name or service not known (npm:81)`, inspect the current Control Plane runtime/recreation contract rather than creating a second stack identity to hide the failure.
