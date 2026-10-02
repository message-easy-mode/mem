---
id: "installation/check-server"
translationKey: "installation/check-server"
locale: "en"
groupId: "installation"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Run the server checks"
description: "Understand fresh-install, migration, repair, host-check, Docker, storage, and port results."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["preflight", "host checks", "legacy detection", "Docker"]
route: "/docs/installation/check-server"
aliases: []
outputPath: "docs/installation/check-server.md"
preserveLegacyBranding: false
---
# Run the server checks

The Setup start page classifies the host before it offers a fresh installation.

Possible results include:

- fresh install;
- legacy migration required;
- repair or existing resources detected;
- already installed;
- unknown because Docker or state inspection failed.

## Legacy detection is a stop condition

When `mem-api` or `mem-web` is detected without a current installation record, MEM treats the host as a legacy MEM 0.1.0 source.

Do not continue with fresh installation. Keep the source intact and use `mem-migrate`.

The old deployment may also contain PostgreSQL, Nginx Proxy Manager, Compose files, `.env`, `stack.sh`, Matrix containers, data directories, and certificates that the new installer must not overwrite by assumption.

## Existing resources without a current record

When MEM-related containers, networks, or volumes exist but the Control Plane cannot find a current installation record, it offers a repair/investigation path.

Inspect the resources and Diagnostics before continuing. Do not delete containers or volumes solely to clear the warning.

## Start the check

From Setup, choose **Run server checks**. During active first-time Setup, MEM records grouped host-check evidence and keeps a compact summary with the installation plan for later Review and support evidence.

The current checks cover:

### Host

- Ubuntu detection;
- CPU architecture;
- CPU count;
- total memory.

### Docker

- Docker command and daemon reachability;
- Docker Compose plugin;
- Docker data-root location;
- Docker disk usage;
- existing MEM containers;
- existing networks and volumes.

### Storage

- root filesystem free space;
- Docker data-root free space.

### Ports

MEM inspects listeners on:

```text
80
443
8443
8080
5432
```

Port 8443 is expected to be occupied by the installer. Other listeners require operator review unless they are known parts of the intended environment.

## Read the compact results

The Server Checks page keeps blockers, warnings, unavailable checks, and other attention findings visible. Routine successful checks are collapsed by group so the operator can focus on what needs a decision. Expand **Technical details** and **Raw evidence** only when you need the underlying explanation or bounded command evidence.

Most browser checks are advisory. A warning does not automatically mean the host is unusable, and a pass does not prove production capacity. The earlier host bootstrap has already enforced its hard Ubuntu, memory, and disk rules; the browser checks provide a second, runtime-aware operational view.

After Setup completes, development preview is read-only. Detailed check runs are active-session evidence and may not survive a Control Plane restart; the installation keeps the compact check snapshot used by Review and support reporting.

## Continue

Continue to the domain stage only when:

- Docker is reachable;
- no legacy install requires migration;
- unexpected resources are understood;
- storage and port warnings are acceptable;
- you know how the Control Plane will remain private.
