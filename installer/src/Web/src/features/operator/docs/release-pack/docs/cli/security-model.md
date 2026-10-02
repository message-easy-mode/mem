---
title: Understand the CLI security model
description: Understand named-device authority, installation binding, local secret storage, fail-closed step-up, retired credentials, and reserved recovery.
section: CLI and automation
order: 50
---

# Understand the CLI security model

## Normal authority path

```text
named MEM operator
→ existing browser sign-in and TOTP
→ reviewed CLI device approval
→ server-issued opaque device credential
→ OS Secret Service storage
→ server role and capability policy
→ server audit and step-up policy
```

This authority is the same whether `mem` runs on the host, through SSH, or on a private workstation. Localhost is not a role or authorization bypass.

## Local configuration versus credentials

The profile file contains only non-secret names, server URLs, language preferences, and default selection. The device credential is stored separately through `secret-tool` and keyed by normalized profile plus server URL.

The CLI probes secure storage before starting an authorization. It never falls back to:

- plaintext profile or config files;
- environment variables;
- command arguments;
- stdin;
- shell history;
- copied browser cookies.

## Server-owned session controls

The server binds a CLI device authorization to the current completed MEM installation. Reinstalling or replacing that installation invalidates an old credential even if the URL remains the same.

Current server defaults are:

- pending approval: 10 minutes;
- idle session lifetime: 8 hours;
- absolute session lifetime: 7 days.

These values are server-controlled. `mem account show` and successful login output report the actual expiry values.

## Step-up boundary

The server decides whether a high-risk operation requires recent identity verification. The CLI neither creates nor bypasses that grant.

The current CLI does not implement a secret-safe interactive password-and-TOTP retry. When the server refuses an action with HTTP 403, the CLI fails closed. Use the approved browser workflow for the protected action.

These command options are rejected:

```text
--password
--totp
--totp-code
--recovery-code
--device-credential
--bearer-token
```

## Retired authority

The following must not appear in new scripts or instructions:

```text
--installer-token
MEM_INSTALLER_TOKEN
--agent-secret
MEM_AGENT_SECRET
X-MEM-Agent-Secret
```

`--installer-token` and `--agent-secret` are explicitly rejected. Ambient `MEM_INSTALLER_TOKEN` is ignored. The old shared-secret authority model must not return.

`--host-agent-url` and `MEM_HOST_AGENT_URL` remain hidden address-compatibility inputs only. They provide no special authority and should be replaced by `--server` and `MEM_SERVER_URL`.

## Reserved host-local recovery

```bash
sudo mem auth arm-recovery
```

This command is reserved for a future local Unix-socket or named-pipe recovery bridge. In the current build it returns `cli_recovery_arm_not_available`, prints no recovery grant, and makes no Control Plane network request. Remote server and profile options are rejected.

Do not treat this reserved refusal as an available recovery workflow.

## Safe support material

Generally safe when needed and reviewed:

- `mem --version` output;
- profile names and redacted server URL;
- account status without credential material;
- bounded JSON status, checks, evidence, and redacted support reports;
- exact safe error codes and process exit codes.

Never share credentials, passwords, TOTP or recovery codes, browser cookies, Secret Service output, signing keys, private keys, raw database dumps, or backup payloads.

## Related documentation

- [Sign in with device login](device-login.md)
- [JSON output and scripting](json-and-scripting.md)
- [CLI troubleshooting](troubleshooting.md)
