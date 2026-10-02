# MEM CLI Release Audit and Command Inventory

**Date:** 2026-07-08 NZ context  
**Target:** MEM v0.1.1 CLI release hardening  
**Source baseline:** `mem-cli-source-20260708-165500.zip`

This document records the current shipped MEM CLI command surface after the
named-device bearer authentication cutover and host-command installation work.
It is intended to keep release help, operator documentation, tests, and manual
proof aligned.

## Release posture

The deliberately packaged executable is:

```bash
mem
```

Normal operational commands use a named CLI device session created by:

```bash
mem login --device --profile <name>
```

The opaque credential is read from the operating-system Secret Service store and
sent as a bearer credential to the MEM control plane. Normal commands must not
use installer tokens, browser cookies, Host Agent shared secrets, or local-host
trust as authority.

The command may run from a private operator workstation, SSH shell, or Ubuntu
console. The authority model is the same in all locations: named operator,
server-side role/policy checks, audit, and server-owned step-up requirements.

## Command families

| Family | Command | Release status | Auth path | JSON | Notes |
|---|---|---:|---|---:|---|
| Local config | `mem config get language` | Current | Local config only | Yes | Does not construct HTTP client. |
| Local config | `mem config set language <en\|de>` | Current | Local config only | Yes | Human language only; JSON remains English. |
| Local config | `mem config get default-profile` | Current | Local config only | Yes | Non-secret local setting. |
| Local config | `mem config set default-profile <name>` | Current | Local config only | Yes | Profile must be non-secret. |
| Profiles | `mem profile create <name> --server <url> [--language <en\|de>]` | Current | Local config only | Yes | Stores name, server URL, optional language only. |
| Profiles | `mem profile list` | Current | Local config only | Yes | Must not expose credentials. |
| Profiles | `mem profile select <name>` | Current | Local config only | Yes | Sets default profile. |
| Profiles | `mem profile remove <name>` | Current | Local config only | Yes | Removes non-secret profile metadata. |
| Device login | `mem login --device [--profile <name>] [--server <url>]` | Current | Device authorization start/poll | No | Interactive headless/browser-approved login. |
| Account | `mem account show [--profile <name>] [--server <url>]` | Current | Stored device bearer | Yes | Safe identity/session status only. |
| Account | `mem logout [--profile <name>] [--server <url>]` | Current | Stored device bearer | Yes | Revokes server session then removes local credential. |
| Host | `mem host status` | Current | Stored device bearer | Yes | Safe host/control-plane readiness. |
| Stack | `mem stack list` | Current | Stored device bearer | Yes | Runtime inventory. |
| Stack | `mem stack inspect <slug-or-id>` | Current | Stored device bearer | Yes | Safe projection; no Docker IDs/paths. |
| Stack | `mem stack doctor <slug-or-id>` | Current | Stored device bearer | Yes | Starts/reads doctor check. |
| Stack | `mem stack operations <slug-or-id>` | Current | Stored device bearer | Yes | Runtime operation history. |
| Backup Catalog | `mem backups list` | Current | Stored device bearer | Yes | Canonical plural command. |
| Backup Catalog | `mem backups inspect <catalog-entry-id>` | Current | Stored device bearer | Yes | Uses durable catalog identity. |
| Backup Catalog | `mem backups lifecycle <catalog-entry-id>` | Current | Stored device bearer | Yes | Safe lifecycle state. |
| Backup Catalog | `mem backups export <catalog-entry-id> --out <path>` | Current | Stored device bearer | Yes | Portable ZIP export. |
| Backup Catalog | `mem backups delete <catalog-entry-id> --yes` | Current | Stored device bearer + server policy | Yes | Irreversible; server may require step-up. |
| Backup import | `mem backups import <zip>` | Current | Stored device bearer | Yes | Materialises a catalog entry. |
| Backup upload | `mem backups uploads inspect <validation-id>` | Current | Stored device bearer | Yes | Retained upload provenance only. |
| Backup upload | `mem backups uploads delete <validation-id> --yes` | Current | Stored device bearer | Yes | Deletes retained ZIP archive, not catalog payload. |
| Restore | `mem restores list` | Current | Stored device bearer | Yes | Paged durable restore-session inventory. |
| Restore | `mem restores inspect <restore-session-id>` | Current | Stored device bearer | Yes | Canonical workspace identity. |
| Restore | `mem restores evidence <restore-session-id>` | Current | Stored device bearer | Yes | Durable evidence projection. |
| Restore | `mem restores logs <restore-session-id>` | Current | Stored device bearer | Yes | Paged, filtered structured logs. |
| Restore | `mem restores support-report <restore-session-id>` | Current | Stored device bearer | Yes | Reads existing report; does not create one implicitly. |
| Restore | `mem restores create <catalog-entry-id>` | Current | Stored device bearer | Yes | Creates or resumes one active workspace per source. |
| Restore | `mem restores private-test <restore-session-id>` | Current | Stored device bearer | Yes | Runs isolated private test. |
| Restore | `mem restores private-test destroy <restore-session-id> --yes` | Current | Stored device bearer | Yes | Explicit retained-staging cleanup. |
| Restore | `mem restores recreate preflight <restore-session-id> ...` | Current | Stored device bearer | Yes | Read-only Standard Recreate assessment. |
| Restore | `mem restores recreate execute <restore-session-id> ... acknowledgements ...` | Current | Stored device bearer + server policy | Yes | Production mutation; server may require step-up. |
| Restore | `mem restores cancel <restore-session-id> --yes` | Current | Stored device bearer | Yes | Releases temporary claims when safe. |
| Restore | `mem restores handover complete <restore-session-id> --yes` | Current | Stored device bearer | Yes | Terminal audit transition. |
| Version | `mem --version` / `mem version` | Current | None | No | Safe local release identification. |
| Recovery | `sudo mem auth arm-recovery` | Reserved | Future local socket only | Yes for current refusal | Not implemented until SEC-AUTH-07A. |

The singular root aliases `backup` and `restore` remain accepted by the command
router for compatibility. Release help and documentation should prefer the
canonical plural forms `backups` and `restores`.

## Retired and compatibility surfaces

| Surface | Current behavior | Release decision |
|---|---|---|
| `--agent-secret` | Rejected with `agent_secret_retired` | Must not regain authority. |
| `MEM_AGENT_SECRET` | Not used by normal CLI commands | Must not be documented. |
| `X-MEM-Agent-Secret` | Not sent by normal CLI commands | Must not be reintroduced. |
| `--installer-token` | Rejected with `installer_token_retired` | Retired normal operator surface. |
| `MEM_INSTALLER_TOKEN` | Ignored | Leftover shell state must not regain authority. |
| `--host-agent-url` | Hidden compatibility alias for `--server` | Still accepted for older scripts, but omitted from normal release help. |
| `MEM_HOST_AGENT_URL` | Hidden compatibility input for `MEM_SERVER_URL` | Still accepted for older shells, but omitted from normal release help. |
| `mem stack compare` | Not implemented | Must not appear in shipped help. |

## Known release gaps / follow-up slices

1. **Interactive CLI step-up.** High-risk commands already go through server
   policy, but the CLI does not yet provide the intended secure password+TOTP
   retry UX for `step_up_required`. Non-interactive behavior must fail closed.
2. **Host-local recovery.** `mem auth arm-recovery` remains a reserved refusal
   until SEC-AUTH-07A provides the local Unix-socket/named-pipe bridge.
3. **End-to-end release packaging.** Bootstrap stages the host-command
   payload in development proof. Release packaging must supply the official
   prebuilt binary and version.
4. **Compatibility alias retirement window.** `--host-agent-url` and
   `MEM_HOST_AGENT_URL` remain hidden parser compatibility inputs for older
   scripts and shells. Normal help and release docs prefer `--server` and
   `MEM_SERVER_URL`.

## Audit checks before release

Run from repository root:

```bash
dotnet build ./cli/src/Mem.Cli/Mem.Cli.csproj -c Release

dotnet test ./cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj

mem --version
mem --help
```

Review normal help for stale authority or non-release commands:

```bash
mem --help | grep -E -- 'mem login --device|mem account show|mem logout|MEM_SERVER_URL'
mem --help | grep -E -- '--installer-token|MEM_INSTALLER_TOKEN|--agent-secret|MEM_AGENT_SECRET|--host-agent-url|MEM_HOST_AGENT_URL|mem stack compare' && echo 'unexpected stale help item found'
```

Run a real local host-command proof after publishing the binary:

```bash
PUBLISH_DIR="$(mktemp -d)"

dotnet publish ./cli/src/Mem.Cli/Mem.Cli.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR"

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --binary "$PUBLISH_DIR/mem" \
  --version dev

rm -rf "$PUBLISH_DIR"
hash -r

command -v mem
ls -l /usr/local/bin/mem
ls -l /opt/mem/cli/dev/mem
mem --version
mem --help
```
