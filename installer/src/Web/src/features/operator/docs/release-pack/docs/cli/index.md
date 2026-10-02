---
title: Use the MEM CLI
description: Use the release-pinned mem command for named-operator inspection, backup transfer, restore work, JSON evidence, and browser-independent troubleshooting.
section: CLI and automation
order: 40
---

# Use the MEM CLI

The MEM CLI is the scriptable operator interface for the MEM Control Plane. The installed command is:

```bash
mem
```

## Outcome

After this section you can:

- point the CLI at the correct private Control Plane;
- authorize a named CLI device through the browser;
- inspect account, host, and chat-server state;
- transfer and inspect Backup Catalog material;
- inspect and run supported Restore Workspace actions;
- collect stable JSON evidence without depending on the React UI.

## Current MEM 0.2.0 boundary

The CLI uses the same server-side identity, role, capability, audit, and step-up policy as the browser. Running it on the Ubuntu host, over SSH, or from a private workstation does not create a trust bypass.

The current command surface is deliberately narrower than the browser UI:

| Available | Not currently available |
|---|---|
| Profiles, language, device login, account status, logout | Create or remove a chat server |
| Host status and stack inspection/doctor/history | Start or stop a stack |
| Backup Catalog list, inspect, lifecycle, export, import, and deletion | Create a new backup capture |
| Restore Workspace inspection and supported actions | Migration-session control |
| Stable JSON output | Global Diagnostics and Seq control |

Use the Control Plane for tasks not shown by `mem --help`. Do not invent command names from browser labels.

## First operator journey

```bash
mem --version

mem profile create home \
  --server https://mem.example.internal

mem profile select home
mem login --device --profile home
mem account show --profile home
mem host status --profile home
```

For local development on the Control Plane host, an explicit loopback HTTP endpoint is allowed:

```bash
mem profile create local --server http://127.0.0.1:7105
```

Production and private remote profiles must use HTTPS.

## Command families

```text
mem config ...
mem profile ...
mem login --device
mem account show
mem logout
mem host status
mem stack ...
mem backups ...
mem restores ...
mem --version
```

The singular roots `backup` and `restore` remain parser compatibility aliases. New documentation and scripts should use `backups` and `restores`.

## Human output and JSON

Human-readable output can be English or German. Command names, option names, identifiers, JSON property names, machine status values, and error codes remain stable English.

```bash
mem host status --profile home --language de
mem host status --profile home --json | jq
```

`mem login --device` is interactive and intentionally does not support `--json`.

## During an incident

Start with read-only evidence:

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

An exit code of `2` usually means the command ran but the requested operational result was not healthy, ready, found, or completed. Preserve the JSON and the exit code.

## Read next

1. [Install the MEM CLI](install.md).
2. [Create profiles](profiles.md).
3. [Sign in with device login](device-login.md).
4. [Use JSON safely](json-and-scripting.md).
5. [Understand the CLI security model](security-model.md).
