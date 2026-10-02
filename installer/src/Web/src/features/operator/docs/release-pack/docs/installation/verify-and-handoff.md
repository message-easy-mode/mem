---
title: Verify the platform and complete handoff
description: Read the verification evidence, acknowledge the durable handoff, and continue in the private operator Control Plane.
section: Installation
order: 70
---

# Verify the platform and complete handoff

Installation completion and successful verification are separate facts.

## Verification report

The final platform verification validates the durable install run and required managed dependencies such as PostgreSQL, NPM, shared Coturn, the selected production/staging certificate state, and NPM certificate visibility.

Sensitive values remain protected.

## Verify the Control Plane administration boundary

Before completing handoff, confirm that the running Control Plane reports the same private access state in:

- Home → Host status;
- Diagnostics → Control Plane runtime;
- System Information.

For SSH/local-only, expect `127.0.0.1:<port>`. For Trusted LAN, expect the exact selected RFC1918 address.

Diagnostics must not contain `control_plane.exposure.unsupported` for a healthy supported deployment.

Do not accept handoff if MEM reports wildcard, public, multiple, or otherwise unsupported exposure.

## Handoff

After verification succeeds, **Finish setup and open dashboard** confirms the transition into normal operator mode.

A Control Plane restart before finish must reconstruct the same pending handoff. After finish, first-time Setup and first-owner bootstrap are locked.

Matrix and Element stack creation is a separate operator workflow after platform handoff. A successful platform install is not yet proof that the first managed stack can be provisioned.

## Recommended next security actions

- confirm the Platform Owner can sign out and sign back in with MFA;
- keep recovery codes safely offline;
- record the selected private administration mode/address;
- record the Control Plane certificate SHA-256 fingerprint;
- keep the Control Plane out of public DNS, NPM ingress, and Internet-facing NAT;
- retain SSH as the break-glass path even when Trusted LAN is the normal access method;
- review Diagnostics and platform health;
- open **Services → Coturn** and require a current successful functional check when TURN is part of the deployment;
- create the first Matrix + Element stack;
- require Matrix and Element to reach **Healthy**;
- open the stack and run **Doctor**;
- verify the public Matrix and Element routes before calling the installation operationally accepted.

The handoff finishes platform installation. It does not remove the operator's responsibility for host, platform, and stack backups.
