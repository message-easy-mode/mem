---
id: "migrate/go-live"
translationKey: "migrate/go-live"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Make the new server live and verify production"
description: "Review route ownership, publish the new Matrix and Element routes, and verify the public production service."
order: 80
status: "supported"
appliesTo: ["0.2.x"]
tags: ["go-live", "Nginx Proxy Manager", "routes", "certificate", "production verification"]
route: "/docs/migrate/go-live"
aliases: []
outputPath: "docs/migrate/go-live.md"
preserveLegacyBranding: false
---
# Make the new server live and verify production

Go-live transfers public proxy-route ownership from the old service path to the new MEM 0.2.0 runtime. It is the highest-impact step in the guided workflow.

## Outcome

The new Matrix and Element services own the intended public routes and pass durable production verification. The old source remains retained for the rollback and acceptance boundary.

## Before you begin

Confirm:

- the new normal runtime is privately healthy;
- the Matrix hostname is correct and unchanged;
- the Element hostname is correct;
- DNS already resolves to the target ingress path;
- an active target certificate covers the public hostnames;
- you understand the current Nginx Proxy Manager route owner;
- users have stopped normal use of the old server, or the final-frozen source is still frozen;
- you have read [Rollback boundaries](rollback.md).

MEM changes Nginx Proxy Manager routes. It does not create DNS records and does not promise to issue a missing certificate during cutover.

## Review readiness

The workspace creates a time-bounded readiness or preview result. Review all blockers and warnings immediately before applying. If the preview expires or the underlying state changes, refresh it rather than reusing stale confirmation data.

Complete step-up authentication when required.

## Publish routes

MEM snapshots the previous route state, selects the active certificate, and applies the Matrix and Element route changes for the new runtime.

If route publication fails, MEM attempts to restore the previous route state. When restoration succeeds, the new server remains private. When route ownership cannot be confirmed, stop the normal retry path and use the advanced recovery evidence to determine which runtime is public.

## Verify production

Run the production checks shown by the workspace. They cover the durable runtime and route ownership rather than only a browser page. Review at least:

- target container and runtime ownership;
- Nginx Proxy Manager route targets and certificate selection;
- public Matrix readiness;
- public Element availability;
- database and migration verification evidence;
- any warnings about source authority or incomplete checks.

## Failed verification boundary

A route change may have succeeded even when a later production check fails. MEM does not automatically roll back solely because verification failed, because doing so could replace a reachable new server with an uncertain old route.

First determine which server is public. Rerun safe checks where appropriate or use the advanced recovery controls. Do not repeatedly press go-live while route ownership is unknown.

Next: [Accept and finish the migration](finish.md).
