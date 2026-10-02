---
title: Operations guide
description: Day-to-day MEM 0.2.x operating sequence for platform health, stacks, diagnostics, backups, and controlled recovery.
section: Operations
order: 5
---

# Operations guide

Operate MEM through the Control Plane's server-owned state and durable operations rather than by treating Docker containers as the product contract.

## Normal daily sequence

1. Open the private Control Plane.
2. Check Home for public access and platform-service health.
3. Review Diagnostics attention when it is non-zero.
4. Open affected stack/service workspaces and refresh current evidence.
5. Use **Doctor** before making assumptions from container presence alone.
6. Create a backup before high-risk stack work.
7. Use reviewed MEM operations for restart, TURN, federation, backup/restore, migration, and removal.

## Platform versus stack health

A healthy platform means shared services such as PostgreSQL, NPM and Coturn are ready. Each Matrix + Element stack has its own readiness and public-route state.

A successful first-time Setup does not by itself prove the first managed stack can be provisioned. On a new server, create and verify a first stack as part of acceptance.

## Incidents and evidence

When an operation fails, preserve the operation ID and incident/support report before retrying or cleaning resources. A durable failed operation can contain the evidence needed to distinguish a product defect from DNS, firewall, image, storage, or network state.

## Docker and Portainer

Use Docker/Portainer for bounded low-level inspection, not as the normal mutation surface. Do not manually recreate, relabel, reconnect, or delete MEM-owned resources unless a documented recovery procedure explicitly calls for it.

## Backups

Keep portable backups off-host. Test recovery rather than assuming an on-box backup is sufficient.

## Restart/reboot acceptance

After Control Plane recreation or server reboot, verify runtime identity, managed-network/service reachability, platform health, and at least one representative stack before declaring recovery complete.
