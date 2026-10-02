---
id: "backups-restores/private-test"
translationKey: "backups-restores/private-test"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Run the private restore test"
description: "Prove database import and Synapse startup on an internal Docker network without public routes."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["private test", "staging", "Docker internal network", "Synapse health", "safe recovery"]
route: "/docs/backups-and-restores/private-test"
aliases: []
outputPath: "docs/backups-and-restores/private-test.md"
preserveLegacyBranding: false
---
# Run the private restore test

The private test is optional, but it is the safest way to discover a damaged or incompatible payload before production PostgreSQL, Docker, or public routes are changed.

## Safety boundary

The canonical private test creates disposable recovery infrastructure on an internal Docker network. Its evidence records that:

- the runtime is private-only;
- the Docker network is internal;
- no public routes are created;
- DNS and certificates are not changed;
- production containers and production databases are not touched;
- the recovered database import and Synapse health check can be evaluated independently.

It is not a public preview URL and must not be made one by manually attaching Nginx Proxy Manager routes.

## Run the test

1. Open the Restore Workspace **Standard** tab.
2. Expand **Private test**.
3. Review blockers and choose **Run private test**.
4. Wait for the operation to complete.
5. Review database import, Synapse health, Matrix identity, completion time, Evidence, and Logs.

A successful test proves that the payload could be imported and that the isolated Synapse runtime passed its health check. It does not prove public DNS, certificates, NPM route ownership, federation reachability, Element client behaviour, or live voice/video calls.

## Retained staging runtime

A successful or deliberately retained private test may require explicit destruction. Use **Retire private test** after reviewing the evidence. MEM removes the private containers, internal network, and disposable workspace while retaining the safe historical result in the Restore Workspace.

Do not remove staging containers manually unless the Control Plane is unavailable and you have preserved their staging ID and operation evidence. Manual removal can leave lifecycle history unclear.

## Failure handling

When the test fails:

- read the safe failure summary;
- open **Evidence** and **Logs**;
- record the restore-session ID, operation ID, event code, and staging ID;
- fix source or target prerequisites rather than editing the materialised payload in place;
- rerun only after the reason is understood.

Skipping the private test is permitted because it is optional. Skipping transfers more risk to Standard Recreate and should be an explicit operator decision, not an accidental click-through.
