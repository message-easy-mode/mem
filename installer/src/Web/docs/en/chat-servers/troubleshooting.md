---
id: "chat-servers/troubleshooting"
translationKey: "chat-servers/troubleshooting"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Troubleshoot a chat server"
description: "Diagnose creation, readiness, user, route, TURN, federation, and storage problems without assuming success."
order: 110
status: "supported"
appliesTo: ["0.2.x"]
tags: ["troubleshooting", "creation failure", "readiness", "TURN drift", "federation"]
route: "/docs/chat-servers/troubleshooting"
aliases: []
outputPath: "docs/chat-servers/troubleshooting.md"
preserveLegacyBranding: false
---
# Troubleshoot a chat server

## Start with fresh evidence

Open the stack workspace, select **Refresh**, then run **Doctor**. Record the stack slug, checked time, operation ID, report ID, failed check codes, and redacted detail.

Do not diagnose solely from container presence or an old last-verified timestamp.

## Creation failed

The creation dialog explicitly reports failure and does not assume success. Before retrying:

1. return to Chat servers and refresh;
2. check whether the stack, containers, routes, database, or operation were recorded;
3. inspect the failed operation and current diagnostics;
4. correct the underlying domain, certificate, image, Docker, PostgreSQL, storage, or readiness problem;
5. retry the same intended request only when the resulting ownership state is clear.

Do not create a second slug to hide a partially created production identity.

## Synapse configuration or production storage failure

On an official production installation, new stack resources belong beneath:

```text
/var/lib/message-easy-mode/instances
```

The Control Plane and host Docker daemon must address the same physical files through that canonical host-data contract. Do not redirect production instance data into `/home/<user>/mem-data` to work around a failure.

If creation fails around Synapse configuration generation, preserve the failed operation and support report. Check whether the incident names `generate-synapse-config`, but use the supported MEM recovery/correction path rather than manually moving generated configuration files.

## NPM route publication fails with `npm:81` resolution

In containerized production, the Control Plane and Nginx Proxy Manager communicate through the managed `mem-gateway` Docker network. An error such as `Name or service not known (npm:81)` means the Control Plane cannot currently resolve the managed NPM authority.

Collect the incident/support report and verify the Control Plane was launched or recreated through the current supported bootstrap. Manual `docker network connect` can be useful as bounded engineering diagnosis, but it is not the normal operator repair procedure and should not replace the supported runtime/recreation path.

## Matrix or Element is not public

Use **Network & domains** to confirm recorded hosts, route IDs, certificate IDs, and internal delivery. Run Doctor to distinguish internal HTTP, NPM, route, and public HTTPS failures.

The page does not verify authoritative DNS or certificate expiry. Check those separately when public HTTPS fails but internal service checks pass.

## User creation is disabled

Synchronize the Synapse user inventory. Resolve inventory errors before treating the homeserver as empty. The first administrator workflow appears only when Synapse authoritatively reports no active local admin.

For password resets, validate Matrix administrator authority and complete Control Plane step-up. Replace rejected or expired authority rather than repeatedly submitting the reset.

## Voice or video is unreliable

Open **Voice & video** and check both stack connection and platform readiness. A connected Synapse configuration does not make relay traffic reliable when the platform coturn service or relay ports are not ready.

Do not overwrite External or drifted TURN settings manually. Use the reviewed operation or collect technical recovery evidence.

## Federation changes are disabled

MEM refuses to rewrite custom, ambiguous, incomplete, or unsupported federation state automatically. Review the reported Synapse runtime and canonical NPM ingress problem. Complete or restore a supported state before applying another policy.

Do not submit a second federation change while a durable operation is running after a browser disconnect.

## Storage details are unavailable

Confirm the stack manifest and Matrix runtime identity are readable. Do not infer that absent storage evidence means the files are absent. Inspect the host only through approved outage or support procedures.

## Escalation evidence

Retain redacted:

- stack slug and public hosts;
- last verified and Doctor checked times;
- operation and report identifiers;
- failed check or stable problem codes;
- image names and versions;
- route and certificate identifiers;
- TURN or federation state and configuration hashes;
- relevant container state and bounded logs.

Never include passwords, Matrix access tokens, TOTP secrets, recovery codes, TURN shared secrets, signing-key contents, or unredacted configuration files.

For platform installation failures, also see [Installation troubleshooting](../installation/troubleshooting.md). For capability boundaries, see [Known limitations](../start/known-limitations.md).
