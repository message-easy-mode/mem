---
id: "migrate/rollback"
translationKey: "migrate/rollback"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Understand rollback boundaries"
description: "Understand what MEM can reverse before and after route cutover, and why the final-frozen path provides stronger rollback evidence."
order: 100
status: "supported"
appliesTo: ["0.2.x"]
tags: ["rollback", "final-frozen", "route restoration", "source restoration", "data loss"]
route: "/docs/migrate/rollback"
aliases: []
outputPath: "docs/migrate/rollback.md"
preserveLegacyBranding: false
---
# Understand rollback boundaries

Rollback is not one universal button. The available action depends on whether public routes changed, which source-authority path was used, and whether users have already created new target data.

## Outcome

You can distinguish safe pre-cutover cancellation, target route recovery, and full source restoration, and you understand the data-loss boundary of returning to the old server.

## Before public cutover

Before route publication, the new candidate and normal target runtime are private. Cancelling or cleaning up target-side work does not change or delete the legacy source. This is the safest point to stop.

## After route publication

MEM may restore the exact pre-cutover Nginx Proxy Manager route snapshot when a route operation fails or when an authorized advanced rollback is applied. This changes target-host route ownership only. The target Control Plane does not connect to the legacy host to restart, unfreeze, or modify it.

When route restoration cannot be proven, treat public ownership as unknown. Inspect the recorded route evidence before taking another action.

## Normal snapshot path

The normal guided path uses the verified package and private test as the authoritative snapshot. It does not provide formal proof that the source remained frozen after capture.

Returning users to the old source can lose every message, account change, membership change, media upload, and key event created on the target after the snapshot. The target does not synchronize those changes back.

## Final-frozen path

The advanced final-frozen path requires a final package proving:

- final package kind;
- `sourceFrozen=true`;
- `rehearsalOnly=false`;
- no disallowed drift from the qualified source.

A target-host rollback can restore routes and stop or privatize the target, but the source must remain frozen until the separate source-restoration handoff is applied with MEM Migrate on the old host. This provides stronger evidence; it still does not merge post-cutover target changes into the source.

## Decision rule

- If cutover has not happened, stop and preserve evidence.
- If routes changed but no target user activity occurred, use the recorded route snapshot and advanced recovery controls.
- If users have used the target, explicitly decide whether losing those new changes is acceptable before returning to the source.
- If route ownership is uncertain, diagnose first; do not alternate routes repeatedly.

> [!WARNING]
> Never delete the legacy source merely because the new server opened once. Preserve it through production verification, acceptance, and the chosen retention period.

Related: [Make the new server live and verify production](go-live.md) and [Use migration evidence, logs, and support information](evidence-and-support.md).
