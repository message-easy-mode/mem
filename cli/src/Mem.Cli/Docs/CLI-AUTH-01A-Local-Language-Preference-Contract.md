# CLI-AUTH-01A — Local Language Preference Contract

**Status:** Local CLI convenience foundation  
**Date:** 6 July 2026 (NZ)  
**Scope:** `CLI-AUTH-01A` of the MEM CLI named-operator access sub-sprint

## Purpose

Add a small local language preference so an operator does not need to append
`--language de` to every invocation. This slice is intentionally independent of
normal CLI login, device credentials, server profiles, and the `SEC-AUTH-07A`
recovery bridge.

## Commands

```bash
mem config set language de
mem config get language
```

The setting changes human output only. JSON command names, property names,
status/error values, and IDs remain stable English machine contracts.

## Resolution order

```text
--language <en|de>
    ↓
MEM_CLI_LANGUAGE
    ↓
saved local language preference
    ↓
system locale
    ↓
English
```

## Local storage boundary

The store persists only:

```json
{
  "schemaVersion": 1,
  "language": "de"
}
```

It never stores an installer token, cookie, device credential, password, TOTP
value, recovery code, raw control-plane response, server session, or profile
credential. On Unix-like platforms the directory and file are written with
restrictive owner-only permissions where supported.

Default path resolution is:

1. `$XDG_CONFIG_HOME/mem/config.json`, when `XDG_CONFIG_HOME` is set;
2. the operating system application-data directory plus `mem/config.json`;
3. `$HOME/.config/mem/config.json` as a final fallback.

## Deliberate limits

This slice does not introduce:

- `mem login`, device sessions, credential storage, or a `--server` option;
- server profiles or per-profile language settings;
- installer-token persistence;
- changes to role, MFA, session, or step-up policy;
- local recovery arming or a local socket client.

`mem auth ...` remains independent of the preference store so later
host-local recovery arming is not blocked by missing or unreadable ordinary CLI
configuration.
