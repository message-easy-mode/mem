---
title: Upgrade and recreate the Control Plane
description: Understand the MEM 0.2.x durable-state and recreation contract and use release-specific instructions for formal upgrades.
section: Operations
order: 60
---

# Upgrade and recreate the Control Plane

MEM 0.2.x is designed so the canonical Control Plane container can be recreated without discarding its durable authority, but a general release upgrade must still follow the **release-specific instructions** for the target version.

## Durable state that must survive

A supported recreation preserves:

```text
mem-control-plane-data
/var/lib/message-easy-mode
private administration mode/address
ControlPlaneInstanceId
managed platform services and stack data
```

The API process identity should change after recreation while the durable Control Plane identity remains stable.

## Managed network topology matters too

An established containerized Control Plane must be able to reach managed services on `mem-gateway`. A supported recreation must therefore restore/reassert required managed-network membership before the replacement is accepted as operational.

## Do not use volume-destructive Docker commands

Do not use `docker rm -v`, delete `mem-control-plane-data`, or move `/var/lib/message-easy-mode` as an upgrade shortcut.

## Bootstrap is not an arbitrary blind updater

Do not assume that invoking an installer against an already-running canonical Control Plane automatically means "replace it with whatever image I requested". The supported behaviour is release-controlled and may deliberately retain or review an existing runtime.

Follow the exact update/recreation instructions shipped with the release candidate or final release.

## After recreation

Verify:

1. `/health/runtime` is valid and reports the intended runtime;
2. `ControlPlaneInstanceId` is unchanged and `ApiProcessInstanceId` rotated;
3. the private bind is unchanged;
4. platform services are healthy;
5. NPM is reachable from the Control Plane through the managed network;
6. Coturn functional verification is current;
7. a representative managed stack remains Healthy.

A future release may expand the guided upgrade workflow. Until then, prefer explicit release-specific evidence over legacy `stack.sh` or Compose-era upgrade instructions.
