---
id: "migrate/decide-and-prepare"
translationKey: "migrate/decide-and-prepare"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Decide whether to migrate and prepare safely"
description: "Confirm the supported source, protect user encryption recovery, and prepare the source and target before capture."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["migration planning", "supported source", "encryption recovery", "maintenance"]
route: "/docs/migrate/decide-and-prepare"
aliases: []
outputPath: "docs/migrate/decide-and-prepare.md"
preserveLegacyBranding: false
---
# Decide whether to migrate and prepare safely

Use migration when the source is a supported MEM 0.1.0 installation and the destination is a separate MEM 0.2.0 Control Plane. Migration is not a universal Synapse importer and is not the recovery path for a native MEM backup.

## Outcome

You have a confirmed source and target, users have protected their encryption recovery, and the operator has the access, storage, DNS, certificate, and maintenance information required to proceed.

## Before you begin

Confirm all of the following:

- the source is the legacy MEM 0.1.0 product profile detected by MEM Migrate;
- the source Docker daemon is reachable and the legacy containers and data still exist;
- a separate MEM 0.2.0 Control Plane is installed and operational;
- you can sign in as an authorized target operator and complete step-up authentication;
- the target has enough free storage for the encrypted package, decrypted working material, conversion, staging, and the final runtime;
- you control the existing Matrix and Element public hostnames, DNS records, and Nginx Proxy Manager routes;
- the target already has a suitable active certificate for the public hostnames;
- you have a maintenance window for the public cutover and production checks.

If the Source Assistant classifies the source as unsupported or blocked, stop. Do not force the package workflow or copy source files manually into the target runtime.

## Protect encrypted message recovery

Before capture, tell affected users not to sign out of working Element sessions. Each user should confirm at least one dependable recovery path, such as:

- another trusted and verified device;
- working Secure Backup and its recovery secret;
- an exported room-key file stored securely.

A server migration can preserve server-side rooms, events, accounts, media, configuration, and Matrix identity. It cannot recreate end-to-end encryption keys that users never backed up.

## Plan one stack

The Source Assistant may discover more than one stack, but each migration package contains exactly one selected stack. Record:

- the intended source stack slug;
- the Matrix public hostname that must remain unchanged;
- the existing Element hostname;
- the current public route owner;
- the source host and target host;
- the operator responsible for the cutover decision.

Do not start parallel migrations for the same public identity.

## Preserve the source

Take any reasonable host-level safety copy your existing operating procedure requires, but do not present it to MEM 0.2.0 as a native Backup Catalog entry. Keep the legacy host intact and reachable. The guided target workflow does not delete it.

## Verify readiness

You are ready to continue when:

- the source and target are separate and reachable;
- the intended source stack is unambiguous;
- users have been warned about encryption recovery;
- public DNS and certificate ownership are understood;
- you know who can stop normal use of the old server at go-live;
- you have read [Rollback boundaries](rollback.md).

Next: [Install and open the Source Assistant privately](install-source-assistant.md).
