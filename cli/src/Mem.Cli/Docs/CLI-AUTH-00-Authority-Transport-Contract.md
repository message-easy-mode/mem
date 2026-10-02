# CLI-AUTH-00 — MEM CLI Authority and Transport Contract

**Status:** Contract freeze / safe-output foundation  
**Date:** 6 July 2026 (NZ)  
**Scope:** `CLI-AUTH-00` of the MEM CLI named-operator access sub-sprint

## Purpose

This document freezes the CLI authority boundary before implementing persistent
named-device login, CLI step-up, or host-armed recovery. It prevents the
current transitional installer-cookie transport from being mistaken for the
long-term security model.

## Command identity

- Product name: **MEM CLI**
- Installed command: `mem`
- Project namespace: `Mem.Cli`

The published executable must be named `mem`. Development may continue to use
`dotnet run --project ./cli/src/Mem.Cli/Mem.Cli.csproj -- ...`.

## Two authority modes

| Invocation | Authority source | Security rule |
|---|---|---|
| `mem host|stack|backups|restores ...` from a private workstation | Named CLI device session once implemented | Normal server role/capability policy and server-controlled step-up policy |
| The same ordinary `mem ...` command at a host console or over SSH | The same named CLI device session | Exactly the same policy; localhost is never an authority bypass |
| `sudo mem auth arm-recovery` | Approved local host OS privilege and the `SEC-AUTH-07A` recovery bridge | Narrow local-only recovery-arm operation; never a normal remote client operation |
| Browser terminal command broker | Browser/broker session and its own command policy | Remote control-plane context; never console/root authority |

The browser terminal must not gain sudo, recovery-socket, local credential-store,
or broad Docker-equivalent privilege merely because a Platform Owner uses it.

## Current transition

Existing normal CLI commands currently call Host Agent-backed control-plane
routes using `--host-agent-url` / `MEM_HOST_AGENT_URL` and an in-memory
installer-unlock cookie obtained from `--installer-token` /
`MEM_INSTALLER_TOKEN`.

This is transitional compatibility only:

- no browser or CLI shared Host Agent secret is allowed;
- the installer token is not a persistent CLI login;
- the installer cookie is never persisted;
- later `CLI-AUTH-03` replaces normal installer-token use with named,
  revocable, private-control-plane CLI device sessions;
- later normal endpoint configuration is expressed as a control-plane
  `--server` profile, not a generic Host Agent URL.

## `auth` root boundary

`mem auth` is reserved now so later security work cannot accidentally fall
through to the normal HTTP transport.

Until the required server-side contracts exist:

- `mem auth ...` is unavailable and makes no control-plane request;
- it does not create an installer cookie;
- it does not accept a recovery grant;
- it does not use a profile/server or legacy transport option as recovery
  authority.

`SEC-AUTH-07A` owns the local Unix-socket/named-pipe bridge, host-authority
validation, arm-grant state, expiry, single-use behavior, audit, and refusal
semantics. The CLI only consumes that contract in `CLI-AUTH-05`.

## Safe output boundary

CLI-visible failures must not include:

- raw non-success HTTP response bodies;
- unfiltered transport exception text;
- authentication headers, cookies, credentials, grant values, or raw claims;
- container IDs/names;
- internal hostnames or internal URLs;
- NPM internal identifiers;
- data/config filesystem paths;
- arbitrary runtime metadata.

`mem stack inspect` emits a public CLI projection in human and JSON modes. The
CLI still deserializes the current response for compatibility, but it only
prints safe public service identity and public endpoint details.

Successful server-provided safe workflow projections remain server-owned
contracts. This rule applies to accidental/raw failure paths, not to deliberately
redacted, typed operational results.

## Deferred work

This contract does not implement:

- persistent CLI profiles or local language preferences;
- `mem login`, `mem login --device`, `mem logout`, or `mem account show`;
- a device-authorization API or stored CLI device credentials;
- CLI step-up prompt/retry;
- a local recovery socket client;
- a browser terminal broker;
- a `--server` endpoint option.

Those are delivered by later `CLI-AUTH` slices, in dependency order.
