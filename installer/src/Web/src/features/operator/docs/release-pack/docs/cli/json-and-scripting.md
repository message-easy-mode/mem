---
title: Use JSON output and scripting
description: Consume stable English JSON, preserve exit codes, collect bounded evidence, and keep secrets out of shell automation.
section: CLI and automation
order: 48
---

# Use JSON output and scripting

Use `--json` when another program, a support workflow, or an AI-assisted diagnostic process needs stable structure.

```bash
mem host status --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

## Stable machine language

JSON property names, status values, error codes, IDs, and command options remain English even when human output is German:

```bash
mem host status --profile home --language de --json | jq
```

Do not parse human-readable output in automation. It may be localized or reformatted.

## Preserve the exit code

A pipeline can hide the CLI exit status. With Bash and `jq`, use `pipefail`:

```bash
set -o pipefail
mem host status --profile home --json | jq
status=$?
printf 'mem status: %s\n' "$status"
```

Current convention:

| Exit code | Meaning |
|---:|---|
| `0` | Requested result met the command's success contract |
| `1` | Usage, local configuration, profile, login, secret-store, or authorization-input failure |
| `2` | The request ran, but the operational result was not ready, healthy, found, valid, or completed |

Read the JSON `status`, `error`, warnings, and details as well as the process exit code.

## Interactive exception

`mem login --device` does not support `--json`. It prints a browser URL and short code, waits, and stores one credential. Automation must not scrape that interactive output or attempt to approve devices automatically.

## Minimal safe evidence bundle

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

Add only the affected resource:

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Keep log pages bounded. Prefer the redacted support report when it contains the needed evidence.

## Common safe errors

```text
cli_profile_required
cli_device_login_required
cli_secret_store_unavailable
cli_device_credential_invalid
agent_secret_retired
installer_token_retired
cli_secret_material_rejected
cli_recovery_arm_not_available
```

The exact error is safe to share. Raw transport response bodies and exception text are deliberately not exposed.

## Secret-handling rules

Never put these in command arguments, environment variables, JSON files, stdin, logs, tickets, or prompts:

- passwords or TOTP values;
- recovery codes;
- CLI device credentials or bearer tokens;
- browser cookies;
- Matrix signing keys or private keys;
- raw database dumps or backup payloads.

MEM explicitly rejects `--password`, `--totp`, `--totp-code`, `--recovery-code`, `--device-credential`, and `--bearer-token`.

## Related documentation

- [CLI security model](security-model.md)
- [CLI troubleshooting](troubleshooting.md)
- [Restore Workspace commands](restore-commands.md)
