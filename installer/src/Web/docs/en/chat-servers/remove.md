---
id: "chat-servers/remove"
translationKey: "chat-servers/remove"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Remove a chat server"
description: "Remove listed stacks durably and recover interrupted, incomplete, or unlisted stack records through guarded Runtime Reconciliation."
order: 100
status: "supported"
appliesTo: ["0.2.x"]
tags: ["remove stack", "destroy runtime", "runtime reconciliation", "interrupted removal", "data retention", "step-up", "routes"]
route: "/docs/chat-servers/remove"
aliases: []
outputPath: "docs/chat-servers/remove.md"
preserveLegacyBranding: false
---
# Remove a chat server

## Outcome

Remove the active Matrix and Element runtime and public routes while understanding that the normal list-page action keeps the database and local files.

## Before you begin

Removal interrupts all users of the stack. Before proceeding:

- create and verify a recent backup;
- record the Matrix and Element public hosts;
- confirm there is no running migration, restore, backup, TURN, or federation operation;
- decide whether retained database and files are intentional;
- tell users that Element access and Matrix traffic will stop.

## Current Delete behaviour

From **Chat servers**, select **Delete** beside the stack and review the confirmation.

The current operator action requests removal of:

- the Matrix and Element containers;
- their public Nginx Proxy Manager routes.

It deliberately keeps:

- the stack PostgreSQL database;
- local Matrix and Element files.

Recent step-up verification is required before the server accepts the high-risk mutation.

## Durable removal operation

After validation and step-up, MEM accepts removal as a durable server operation and returns an operation reference. The browser tracks stages such as route removal, Matrix and Element container removal, retained-data handling, runtime-inventory finalisation, and manifest removal.

The removal continues under a bounded Control Plane lifetime if the page is refreshed, the tab is closed, or the initiating HTTP connection disappears. Returning to **Chat servers** restores progress tracking for the accepted operation without submitting a second destroy request.

MEM treats already-absent routes and containers as safe idempotent skips. This allows a new explicitly confirmed operation to resume cleanup after an earlier partial attempt, for example when one container has already been removed and another remains stopped. A real cleanup error ends the normal list-page operation as failed instead of silently discarding the remaining ownership record.

> [!IMPORTANT]
> Do not press Delete again merely because the browser lost progress temporarily. Reopen **Chat servers** and let MEM reconnect to the existing operation. If the operation reaches a terminal failure, open Diagnostics and review the failed stage before confirming a new attempt.

> [!IMPORTANT]
> A stack disappearing from the active runtime list does not prove that its data was erased. The success notice explicitly distinguishes runtime removal from database and file handling.

## Recover a stack that is missing from Chat servers

The normal **Chat servers** inventory is backed by active runtime manifests. A failed creation or interrupted removal can leave durable `RuntimeStack`, service-instance, or route records after the manifest has disappeared. Such a stack may remain visible in Docker, Nginx Proxy Manager, or **Diagnostics → Runtime reconciliation** while no Delete button is available on the normal list.

Runtime Reconciliation compares the manifest store, durable database rows, service identities, route targets, and effective NPM state. It classifies manifestless active records as one of the following:

- **Interrupted removal** — one or more recorded NPM routes are already absent, consistent with teardown that stopped before durable finalisation.
- **Incomplete creation** — durable service records exist, but no runtime manifest or public routes were recorded.
- **Unlisted runtime** — the recorded services and NPM routes still agree, but the manifest is missing.
- **Ownership ambiguous** or **Inspection unavailable** — evidence is conflicting or incomplete, so no destructive action is offered.

Where MEM can prove bounded ownership, the page offers a server-authored action such as **Finish removal**, **Clean incomplete creation**, or **Remove unlisted stack**. The operator must:

1. review the classification and service/route counts;
2. type `REMOVE <exact-slug>`;
3. complete recent step-up verification;
4. track the accepted durable destroy operation.

The recovery request is deliberately constrained to exact containers and NPM routes. It retains the Matrix database, database role, and local stack files, and it keeps `force=false`. Before mutation, MEM revalidates recorded container name and ID, MEM ownership labels, Control Plane instance identity, runtime mode, service identity, data-path mounts, public hostname, NPM upstream target, port, and certificate identity. Any mismatch fails closed.

Already-absent routes or containers are safe idempotent skips. A stopped ownership-verified container can be removed. MEM then removes stale service and route rows, marks the runtime stack destroyed, releases the original slug, and records the terminal operation result.

> [!WARNING]
> Do not use broad Docker cleanup, manual SQLite edits, or direct NPM deletion to make reconciliation counters look clean. If MEM labels ownership as ambiguous, preserve the evidence and investigate it rather than forcing removal.

## After removal

Confirm that:

- the stack no longer appears as an active runtime;
- the public routes no longer serve the stack;
- the durable destroy operation reached **Succeeded** rather than remaining running or failed;
- retained data paths and database ownership are documented.

Do not manually delete retained resources merely because the stack is absent from the list. Stronger data removal or recovery decisions must be deliberate and should follow the supported lifecycle tooling available for the installed release.

## When not to use Delete

Do not use runtime deletion as a substitute for:

- migration to a new server;
- restore rehearsal;
- hostname change;
- temporary outage troubleshooting;
- clearing a failed creation without first checking the operation and manifest state.

Use the bounded migration, recovery, or diagnostics workflow for those outcomes.
