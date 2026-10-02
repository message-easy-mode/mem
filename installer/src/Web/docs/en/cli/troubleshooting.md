---
id: "cli/troubleshooting"
translationKey: "cli/troubleshooting"
locale: "en"
groupId: "cli"
groupKey: "cli"
groupLabel: "CLI and automation"
groupOrder: 80
title: "Troubleshoot the MEM CLI"
description: "Work from command installation through profile, keyring, login, reachability, authorization, and resource-specific evidence."
order: 49
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "troubleshooting", "keyring", "authorization", "connectivity"]
route: "/docs/cli/troubleshooting"
aliases: []
outputPath: "docs/cli/troubleshooting.md"
preserveLegacyBranding: false
---
# Troubleshoot the MEM CLI

Work from the local command outward. Do not begin with destructive backup or restore actions.

## 1. Verify the installed binary

```bash
command -v mem
readlink -f "$(command -v mem)"
mem --version
mem --help
```

If the command is absent, remember that current source contains installer integration but leaves it disabled by default unless release packaging supplies and enables the binary. Follow [Install the MEM CLI](install.md).

If an old binary is cached:

```bash
hash -r
```

## 2. Verify local configuration

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

Typical fixes:

```bash
mem profile create home --server https://mem.example.internal
mem profile select home
```

Profile errors include invalid names, non-HTTPS remote URLs, a missing profile, malformed JSON, unsupported schema, unsafe symbolic-link paths, and unreadable configuration.

## 3. Verify the non-root keyring

```bash
command -v secret-tool
secret-tool --help
```

Install the client when missing:

```bash
sudo apt install libsecret-tools
```

Then return to the normal user. Do not run login with `sudo`. A headless SSH shell may have `secret-tool` installed but no unlocked Secret Service session. MEM intentionally fails instead of writing plaintext credentials.

## 4. Verify account state

```bash
mem account show --profile home --json | jq
```

When signed out or rejected:

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

If the Control Plane was reinstalled, old credentials are installation-bound and must be replaced.

## 5. Verify private Control Plane reachability

```bash
mem host status --profile home --json | jq
```

Check the profile URL, private DNS, VPN or management network, TLS certificate, and reverse-proxy reachability. Do not change the profile to a public Matrix or Element URL.

A 401 means the server rejected the CLI device session. A 403 means the role or recent-verification policy refused the action. The current CLI does not accept password or TOTP step-up material through the shell.

## 6. Narrow to the affected resource

Chat server:

```bash
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Backup Catalog:

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

Restore Workspace:

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

## Common symptoms

### `cli_profile_required`

Operational credentials are keyed by a named profile and server. Create/select a profile; `--server` alone is not enough.

### `cli_secret_store_unavailable`

The keyring is absent, locked, or not connected to the current shell session. Fix Secret Service for the invoking non-root user.

### `cli_device_login_required`

No credential exists for the selected profile. Run device login.

### `cli_device_credential_invalid`

Remove the invalid local session with `mem logout`, then sign in again.

### `agent_secret_retired` or `installer_token_retired`

Remove the retired option from the command. Do not replace it with an environment variable. Use named-device login.

### Exit code `2`

The command syntax and local authority were accepted, but the operational result did not meet success criteria. Inspect the returned status, warnings, checks, and detail.

### UI unavailable but CLI works

Keep to read-only evidence first. The CLI does not currently expose global Diagnostics, Seq, stack lifecycle, backup creation, or migration-session commands. Do not invent substitutes or mutate Docker outside the documented recovery boundary.

## Related documentation

- [JSON output and scripting](json-and-scripting.md)
- [CLI security model](security-model.md)
- [Inspect the host and chat servers](host-and-stack-commands.md)
