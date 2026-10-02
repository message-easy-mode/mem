---
title: Run daily operations safely
description: Use readiness, Doctor, operation history, backups, and dedicated workflows for routine administration.
section: Operate chat servers
order: 90
---

# Run daily operations safely

## Outcome

Operate a healthy chat server without turning every observation into a container mutation.

## Routine checklist

For normal operation:

1. open **Chat servers** and check the latest status and verification time;
2. open the stack workspace and Refresh when current state matters;
3. use **Open Element** for a real user-facing check;
4. run **Doctor** after DNS, certificate, route, firewall, image, or host changes;
5. review recent operations for running, failed, or rolled-back work;
6. synchronize Matrix users before account administration;
7. inspect TURN and federation after related platform changes;
8. create a backup before destructive or identity-affecting work.

## Interpret status carefully

A green historical status proves what the last verification observed. It is not continuous monitoring. A browser can also retain the latest Doctor result only for the current session.

When a user reports an outage, create fresh evidence instead of relying on an old timestamp.

## Use dedicated workflows

The Stack Workspace intentionally avoids generic container start, stop, restart, and YAML-edit controls. Safe operations may need candidate validation, step-up, a durable operation journal, verification, and rollback.

Use:

- the Users workflow for Matrix accounts;
- Voice & video for TURN changes;
- Federation for federation policy;
- Backups and Recovery for data protection;
- Diagnostics for current evidence;
- the Delete workflow for runtime retirement.

Use Portainer or host Docker commands as emergency evidence or outage tooling, not as the normal source of truth for MEM-owned configuration.

## Protect the administration surface

Keep the Control Plane private. It has Docker socket and MEM-owned filesystem authority. Use a trusted LAN, VPN, management network, or SSH tunnel.

Do not include passwords, TOTP secrets, recovery codes, Matrix access tokens, TURN secrets, signing keys, or private configuration values in support notes.

## Before planned change

Record the stack slug, current public hosts, last verified time, recent operation state, and latest usable backup. After the change, run Doctor and retain the new operation or report reference.
