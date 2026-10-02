---
title: Manage federation
description: Review and apply Public, Restricted, or Local-only Matrix federation with guarded restart and rollback.
section: Operate chat servers
order: 70
---

# Manage federation

## Outcome

Choose how the stack exchanges Matrix federation traffic with other homeservers and apply the change through a reviewed, journalled operation.

Open the stack workspace and select **Federation**.

## Supported modes

**Public federation** permits normal communication with valid Matrix homeservers through the public Matrix route.

**Restricted federation** uses Synapse's built-in exact-domain allowlist. Enter one homeserver domain per line. Wildcards, URLs, IP addresses, paths, ports, and duplicates are rejected. This is an allowlist, not a separate federation border gateway.

**Local-only federation** applies an empty Synapse allowlist and blocks discovery, federation, and signing-key paths at the canonical NPM route. Matrix client access remains publicly available unless you separately restrict it with network or VPN controls.

Local-only does not delete existing users, rooms, messages, media, signing keys, or historical remote room state.

## Review before apply

The editor observes the active Synapse configuration, Matrix runtime, and canonical NPM route. Custom, ambiguous, incomplete, or unsupported state can disable management until the problem is resolved.

Select **Review change**. The review shows:

- current and proposed modes;
- domains added and removed;
- whether Matrix restart is required;
- whether NPM ingress changes;
- impact on existing federated rooms;
- automatic rollback behaviour.

A relevant stack change invalidates the review. Do not reuse a stale review.

## Apply the policy

Confirm the exact reviewed change and complete recent step-up verification when requested.

MEM journals the operation, validates the candidate with the active Synapse image, replaces configuration atomically, performs the controlled Matrix restart, changes NPM ingress when required, and verifies client and federation behaviour.

Connected clients can reconnect briefly during restart. Removing a homeserver from the restricted allowlist can stop new event exchange with that server; existing history remains.

## Failure and rollback

When restart or verification fails after mutation, MEM attempts to restore and verify the exact previous Synapse and ingress state. If the browser disconnects, do not submit a second change while the durable operation is still running.

Use the operation ID and final observed state to decide whether it is safe to retry. An unresolved state requires technical recovery.
