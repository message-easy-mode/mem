# CLI-AUTH-01B — Local Profiles and Default Server Selection Contract

**Status:** Local CLI profile foundation
**Date:** 6 July 2026 (NZ)
**Scope:** `CLI-AUTH-01B` of the MEM CLI named-operator access sub-sprint

## Purpose

Add local, non-secret MEM CLI profiles so an operator can select a private MEM
control-plane endpoint and a preferred human-output language without repeating
those values on every command.

This is a local convenience and transport-selection slice only. It does not
authenticate a CLI device, store a credential, persist the temporary installer
token/cookie, create a recovery grant, or change server-side authorization.

## Commands

```bash
# Create a profile. Production/private endpoints use HTTPS.
mem profile create home --server https://mem.example.internal --language de

# Development-only loopback endpoint is allowed without TLS.
mem profile create dev --server http://localhost:7105

# Make a profile the local default.
mem profile select home

# Equivalent explicit default-profile setting.
mem config set default-profile home
mem config get default-profile

# Inspect or remove local profile metadata.
mem profile list
mem profile remove dev

# Use a profile for one normal command without changing the default.
mem backups list --profile home
```

`profile` and `config` are local-only commands. They never construct the
normal HTTP client or call the MEM control plane.

## Normal command selection

For normal `host`, `stack`, `backups`, and `restores` commands, server
selection is:

```text
--server <url>
    ↓
--host-agent-url <url> (temporary legacy compatibility alias)
    ↓
MEM_SERVER_URL
    ↓
MEM_HOST_AGENT_URL (temporary legacy compatibility input)
    ↓
selected --profile or saved default profile
    ↓
current local-development default: http://localhost:7105
```

A profile selected by `--profile` overrides the saved default profile. An
unknown, malformed, or missing `--profile` value fails safely before the CLI
constructs the normal HTTP client.

The property is still internally named `HostAgentUrl` only because current
commands use transitional Host Agent-backed control-plane routes. `--server`
and profile `server` values are the operator-facing names; this does not make
the future Host Agent a remotely reachable service.

## Language selection

The existing global local language preference remains a backward-compatible
fallback. The final precedence in this slice is:

```text
--language <en|de>
    ↓
MEM_CLI_LANGUAGE
    ↓
selected/default profile language
    ↓
saved global local language preference
    ↓
system locale
    ↓
English fallback
```

Profile language is optional. A profile with no language inherits the saved
global local preference, then the system locale.

Human output can be English or German. Command grammar, JSON property names,
status/error values, IDs, and all other machine contracts remain stable
English.

## Local storage boundary

The store migrates its own writes to schema version 2:

```json
{
  "schemaVersion": 2,
  "language": "de",
  "defaultProfile": "home",
  "profiles": [
    {
      "name": "home",
      "serverUrl": "https://mem.example.internal",
      "language": "de"
    }
  ]
}
```

Schema version 1 language-only files remain readable. The next write upgrades
them to schema version 2 while retaining the language preference.

Profiles may contain only:

- canonical profile name;
- canonical control-plane server URL;
- optional `en` or `de` human-output preference.

They must never contain:

- installer tokens;
- cookies;
- device credentials;
- passwords;
- TOTP values or seeds;
- recovery codes;
- session or runtime metadata;
- raw server responses;
- request headers.

On Unix-like platforms, the config directory and file are written with
owner-only permissions where supported. The store rejects a symlinked
configuration file or target directory.

## Server URL validation

A profile server must be:

- an absolute HTTPS URL with no user info, query string, fragment, or path; or
- an explicit loopback HTTP development URL such as
  `http://localhost:7105`.

The CLI rejects generic non-TLS LAN/VPN/server HTTP URLs and URLs carrying
embedded user credentials or query parameters. There is no `--insecure`
switch.

## Deliberate limits

This slice does not add:

- `mem login`, device authorization, refresh, logout, or account inspection;
- a credential store or a plaintext fallback;
- installer-token persistence or installer-token retirement;
- server-side profile synchronization;
- profile import/export;
- CLI interactive step-up;
- `SEC-AUTH-07A` local recovery socket integration;
- browser-terminal implementation or host-authority bypass.

A named, revocable CLI device session remains `CLI-AUTH-02/03` work. The
profile is only a safe local lookup key and endpoint/language convenience.
