---
id: "cli/profiles"
translationKey: "cli/profiles"
locale: "en"
groupId: "cli"
groupKey: "cli"
groupLabel: "CLI and automation"
groupOrder: 80
title: "Configure profiles and servers"
description: "Create non-secret profiles for private Control Plane endpoints and choose predictable server and language precedence."
order: 42
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "profiles", "server URL", "language", "configuration"]
route: "/docs/cli/profiles"
aliases: []
outputPath: "docs/cli/profiles.md"
preserveLegacyBranding: false
---
# Configure profiles and servers

A profile identifies one MEM Control Plane and an optional human-output language. It does not authenticate the operator.

## What a profile stores

A profile may contain only:

- a normalized profile name;
- the Control Plane server URL;
- optional `en` or `de` preference;
- default-profile selection.

It must not contain a password, TOTP value, recovery code, browser cookie, installer token, bearer credential, Secret Service value, raw response, or runtime evidence.

The normal Linux configuration file is typically:

```text
~/.config/mem/config.json
```

`XDG_CONFIG_HOME` takes precedence when set. MEM writes the directory as user-only and the file as user read/write, rejects symbolic-link paths, and writes updates atomically.

## Create and select a profile

```bash
mem profile create home \
  --server https://mem.example.internal \
  --language en

mem profile select home
mem profile list
```

Profile names are normalized to lowercase, must be 1–32 characters, must begin and end with a letter or number, and may contain only letters, numbers, and hyphens.

## Server URL rules

Remote and production endpoints must use HTTPS:

```bash
mem profile create home --server https://mem.example.internal
```

HTTP is accepted only for an explicit loopback development endpoint:

```bash
mem profile create local --server http://127.0.0.1:7105
```

The URL must contain only scheme, host, and optional port. User information, query strings, fragments, and non-root paths are rejected. Use the Control Plane address, not a Matrix or Element public URL.

## Default profile

Both commands select the default profile:

```bash
mem profile select home
mem config set default-profile home
```

Use one profile for a single command without changing the default:

```bash
mem host status --profile lab
```

Removing the default profile also clears the default selection:

```bash
mem profile remove old-lab
```

Removing profile metadata does not revoke or delete an associated Secret Service credential. Run `mem logout --profile <name>` before removing a profile when a device session exists.

## Server precedence

For an operational invocation, MEM resolves the server in this order:

```text
--server <url>
MEM_SERVER_URL
selected or default profile
http://localhost:7105 development default
```

The hidden `--host-agent-url` and `MEM_HOST_AGENT_URL` compatibility inputs are still parsed for older scripts but must not be used in new documentation or automation.

Operational commands still require a named profile because the secure credential is keyed by profile and server. An explicit `--server` does not remove that requirement.

## Language precedence

```text
--language <en|de>
MEM_CLI_LANGUAGE
selected/default profile language
global local language preference
system locale
English
```

Examples:

```bash
mem config set language de
mem config get language
mem host status --profile home --language en
MEM_CLI_LANGUAGE=de mem --help
```

JSON field names and machine values remain English regardless of the selected language.

## Verify success

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

Review the server URL before any destructive backup or restore command.

## Related documentation

- [Sign in with device login](device-login.md)
- [Account status and logout](account-and-logout.md)
- [CLI security model](security-model.md)
