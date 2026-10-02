---
id: "installation/private-access-and-setup-code"
translationKey: "installation/private-access-and-setup-code"
locale: "en"
groupId: "installation"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Open the private Control Plane and use the setup code"
description: "Open the self-signed private Control Plane safely and use the one-time setup code to begin first-owner bootstrap."
order: 30
status: "supported"
appliesTo: ["0.2.x"]
tags: ["private access", "setup code", "SSH tunnel", "TLS"]
route: "/docs/installation/private-access-and-setup-code"
aliases: []
outputPath: "docs/installation/private-access-and-setup-code.md"
preserveLegacyBranding: false
---

# Open the private Control Plane and use the setup code

After bootstrap starts `mem-control-plane`, it prints the supported private administration path, browser URL, SSH command when relevant, setup code guidance, and the SHA-256 fingerprint of the Control Plane certificate.

## SSH tunnel / local-only — default

For an Internet/VPS host, MEM binds the Control Plane only to host loopback.

Run the command printed by bootstrap from your workstation. Its shape is:

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:8443:127.0.0.1:8443 \
  <operator>@<server>
```

Then open:

```text
https://127.0.0.1:8443
```

The workstation side of the forward is loopback-bound too, so the SSH tunnel does not accidentally publish MEM to the workstation LAN.

There is no supported direct public Control Plane URL in this mode.

## Trusted LAN — explicit opt-in

On an on-premises host, Proxmox VM, home lab, or management VLAN, bootstrap can bind MEM to one explicitly selected RFC1918 address assigned to the host.

Example:

```text
https://192.168.10.20:8443
```

Use Trusted LAN only when devices on that private network are trusted for administration. Prefer a stable address or DHCP reservation.

Do not forward the Control Plane port through Internet-facing NAT, public DNS, or Nginx Proxy Manager.

SSH remains available as a break-glass/private tunnel path.

## Expected browser certificate warning

MEM uses a locally generated self-signed certificate for the private Control Plane. A browser trust warning is expected.

Bootstrap prints the certificate SHA-256 fingerprint. Compare that fingerprint before accepting the browser exception.

This certificate protects the private Control Plane connection. It is separate from the public wildcard certificate used by Matrix and Element.

## Application authentication still applies

Private network access does not replace MEM authentication. Named login, TOTP MFA, roles, session controls, and recent step-up for high-risk actions remain required according to policy.

The network boundary prevents unnecessary reachability; the application boundary still decides who may operate MEM.

## Use the setup code

Enter the high-entropy `mem_...` code on the first-owner bootstrap screen when requested. Treat it like a password and do not publish it in logs, screenshots, tickets, or chat.

To display the persisted code again from the host:

```bash
sudo ./install.sh --show-setup-token
```

The code can create only a short-lived bootstrap grant while no completed Platform Owner exists. Complete first-owner creation, TOTP enrolment, and recovery-code storage immediately.

After a completed Platform Owner exists, normal named-account login is required.

## Verify the current exposure

The running Control Plane reports its observed Docker host binding in Home → Host status, Diagnostics → Control Plane runtime, and System Information.

A healthy SSH-mode runtime should report approximately:

```text
Control Plane access
SSH tunnel · Loopback only · 127.0.0.1:8443
```

A healthy Trusted-LAN runtime reports the selected private address.

Wildcard/public drift is a Diagnostics error incident rather than a state MEM silently accepts.
