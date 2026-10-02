# MEM CLI Operator Guide

MEM CLI is the scriptable operator interface for Message Easy Mode (MEM). The
deliberately packaged command name is:

```bash
mem
```

Normal commands authenticate to the MEM control plane with the named-device
bearer credential created by `mem login --device`. The command can be run from
a private operator workstation or a shell on the Ubuntu host, but both locations
use the same server-side authority model. Local execution is **not** a role,
session, or step-up bypass.

`mem login --device` starts the normal browser-approved named-device sign-in
journey for a selected local profile. It saves the issued opaque credential only
through the supported OS secret store; it never writes that credential to the
profile, configuration file, environment, shell history, or command arguments.

`mem auth` remains reserved for the future host-local recovery contract. In
particular, `sudo mem auth arm-recovery` will be introduced only when
`SEC-AUTH-07A` supplies its separate Unix-socket-only recovery bridge.

## Canonical identities

```text
catalogEntryId     Backup Catalog inventory, lifecycle, export, delete, and restore creation
restoreSessionId   Restore Workspace inspection and actions
validationId       retained uploaded-ZIP provenance/archive management only
```

A `validationId` is never a Restore Workspace identity. A portable ZIP is validated and materialised into a Backup Catalog item before restore work begins.

```text
portable ZIP
→ validation / retained archive provenance
→ Backup Catalog entry
→ Restore Attempt / Restore Workspace
→ private test / Standard Recreate / cancellation / handover
```

Terminal restore history remains available after its source catalog item is permanently deleted. The workspace presents that source as `Deleted backup`.

## Host command installation

Release and development hosts install the CLI as the stable `mem` command. The
current Linux host contract is:

```text
/opt/mem/cli/<version>/mem   root:root, 0755
/usr/local/bin/mem           symlink to the selected version
```

The script below either publishes from the local source checkout or installs an
already-published single-file binary supplied by release/bootstrap packaging. It
also checks that `secret-tool` is available because `mem login --device` stores
credentials through the operating-system Secret Service provider.

From repository root, for a local development install:

```bash
sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh --version dev
mem --help
```

For a prebuilt release binary:

```bash
sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0
```

Dry-run first when validating packaging or host paths:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh --dry-run --version dev
```

Do not run normal `mem` commands with `sudo`. Normal commands use the invoking
operator user's Secret Service keyring and named-device session. `sudo` is
reserved for installation/upgrade and the future narrow `mem auth arm-recovery`
boundary.

## Development

From repository root:

```bash
dotnet run --project ./cli/src/Mem.Cli/Mem.Cli.csproj -- <command>

dotnet build ./cli/src/Mem.Cli/Mem.Cli.csproj

dotnet test ./cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj
```

Useful development environment for an explicit local control-plane endpoint:

```bash
export MEM_SERVER_URL=http://localhost:7105
```

Show the locally installed CLI version/build identity:

```bash
mem --version
```

The current release command inventory and remaining CLI release-hardening gaps
are tracked in:

```text
Mem.Cli/Docs/CLI-RELEASE-AUDIT-Command-Inventory.md
```

`MEM_HOST_AGENT_URL` remains a temporary server-address compatibility input for older shells, but it is intentionally omitted from normal release help.
`MEM_INSTALLER_TOKEN` is ignored by normal commands after the named-device
cutover. Use `mem login --device` with a named profile instead of installer
tokens.

Most commands support `--json`. JSON contracts retain stable English property
names for automation; human-readable text supports English and German without
changing command names, IDs, or JSON field names.


## Local language and server profiles

Use a local language preference when you want German human output in future
shells without repeating `--language de`:

```bash
mem config set language de
mem config get language
```

You can also create named local profiles for a private control-plane endpoint
and optional language preference:

```bash
mem profile create home \
  --server https://mem.example.internal \
  --language de

mem profile select home
mem profile list

# Use a non-default profile for one normal command.
mem backups list --profile home
```

A profile contains only a local profile name, a canonical server URL, and an
optional `en`/`de` language preference. It never contains an installer token,
cookie, device credential, password, TOTP value, recovery code, raw response,
or session/runtime metadata.

Production/private profile endpoints must use HTTPS. An HTTP profile is
accepted only for an explicit loopback development endpoint such as
`http://localhost:7105`; there is no generic insecure/TLS-bypass switch.

For normal commands, endpoint selection is:

```text
--server <url>
→ --host-agent-url <url> (hidden legacy compatibility alias)
→ MEM_SERVER_URL
→ MEM_HOST_AGENT_URL (hidden legacy compatibility input)
→ selected --profile or saved default profile
→ current localhost development default
```

`--server` and `MEM_SERVER_URL` are the operator-facing names. The legacy `HostAgent` wording remains parser compatibility only; it is not advertised in normal help and it does not make the future Host Agent a remote endpoint.

Language selection is:

```text
--language <en|de>
→ MEM_CLI_LANGUAGE
→ selected/default profile language
→ saved global local preference
→ system locale
→ English
```

JSON contracts retain stable English property names, status/error values, and
IDs. Profiles are local convenience only: they do not authenticate the CLI,
bypass ordinary role/step-up controls, or change server-side policy.

## Browser-approved named-device login

Create or select a non-secret local profile first:

```bash
mem profile create home --server https://mem.example.internal
mem profile select home
```

Then begin the interactive device journey and inspect the resulting account:

```bash
mem login --device --profile home
mem account show --profile home
mem host status --profile home
```

The terminal prints a server-supplied browser approval URL and a short device
code. Open the URL in the normal private-control-plane browser surface, enter
the code, and approve it using the existing named operator session and fresh
step-up verification. The terminal polls using a high-entropy verifier held
only in memory.

On Linux, `mem login --device` requires `secret-tool` and a usable Secret
Service-compatible keyring for the **invoking non-root user**. MEM performs a
write/read/delete probe before it starts an authorization attempt. It fails
closed when the keyring is unavailable and never falls back to a plaintext
file, profile, environment variable, shell history, or process argument.

The command is intentionally interactive and does not support `--json` in this
initial implementation. Re-running it when a valid credential is already
stored is idempotent: it does not create another server session. Use
`mem account show --profile home` to inspect the stored session and
`mem logout --profile home` to revoke the server-side session and remove the
local credential.

## SEC-AUTH authentication

The CLI no longer sends `X-MEM-Agent-Secret`, reads `MEM_AGENT_SECRET`, or has
a development shared-secret fallback. An explicit `--agent-secret` option is
rejected so it cannot be mistaken for an authority mechanism.

Normal host, stack, backup, and restore commands now use the server-issued
named-device credential established by `mem login --device`. The credential is
read from the OS secret store for the selected profile and sent as a bearer
credential to the MEM control plane. It is never written to the local profile,
environment, command arguments, or shell history.

The old explicit `--installer-token` operator surface is retired and rejected.
The ambient `MEM_INSTALLER_TOKEN` environment variable is ignored so a leftover
legacy shell setting cannot regain authority or break an otherwise valid
named-device invocation.

Normal CLI operations keep the same named-session, role, audit, and step-up
rules whether `mem` runs remotely, over SSH, or at an Ubuntu console. When the
control plane refuses a protected operation because a stronger role or recent
identity verification is required, this release fails closed. It does not accept
passwords, TOTP codes, recovery codes, bearer tokens, or device credentials via
command flags, environment variables, or stdin.

The future host-local exception is deliberately narrow:

```bash
sudo mem auth arm-recovery
```

That recovery command is not implemented yet. Current builds reject it with a
stable safe error and reject remote/profile options for it. It will never become
a remote HTTPS fallback, a browser-terminal privilege, a direct SQLite edit, a
raw recovery-grant printout, or a revived historic `mem_` token.

The current step-up and recovery boundary close-out is recorded in:

```text
Mem.Cli/Docs/CLI-SECURITY-01-Step-Up-and-Recovery-Boundary.md
```

## Host and stack commands

Normal host, stack, backup, and restore commands require a selected or explicit
profile with a stored named-device credential. Create one with
`mem login --device` before running the operational commands below.

```text
mem host status

mem stack list
mem stack inspect <slug-or-id>
mem stack doctor <slug-or-id>
mem stack operations <slug-or-id>
```

## Backup Catalog commands

```text
mem backups list
mem backups inspect <catalog-entry-id>
mem backups lifecycle <catalog-entry-id>
mem backups export <catalog-entry-id> --out <path>
mem backups delete <catalog-entry-id> --yes

mem backups import <zip>
mem backups uploads inspect <validation-id>
mem backups uploads delete <validation-id> --yes
```

### Import a portable ZIP

```bash
mem backups import ./mem-stack-export.zip --profile home
```

Successful import output contains:

```text
catalogEntryId       durable recovery identity for restore work
validationId         retained archive provenance identity only
catalogPayloadState  managed payload state
```

Later restore work starts from the returned `catalogEntryId`:

```bash
mem restores create <catalog-entry-id> --profile home
```

### Catalog deletion

`mem backups delete <catalog-entry-id> --yes` is irreversible. The CLI checks source lifecycle first; active Restore Workspaces block permanent deletion. A successful deletion removes the managed payload, catalog entry, and generated portable exports. Completed or cancelled restore history remains available as detached audit history.

`mem backups uploads delete <validation-id> --yes` deletes only the retained uploaded ZIP archive. A materialised catalog payload remains independently usable.

## Restore Workspace commands

```text
mem restores list
mem restores inspect <restore-session-id>
mem restores evidence <restore-session-id>
mem restores logs <restore-session-id>
mem restores support-report <restore-session-id>

mem restores create <catalog-entry-id>
mem restores private-test <restore-session-id>
mem restores private-test destroy <restore-session-id> --yes

mem restores recreate preflight <restore-session-id> ...
mem restores recreate execute <restore-session-id> ... acknowledgements ...
mem restores cancel <restore-session-id> --yes
mem restores handover complete <restore-session-id> --yes
```

### Create or resume a workspace

```bash
mem restores create <catalog-entry-id> --profile home
```

MEM has one active Restore Workspace per exact source. Repeating this command resumes the same active `restoreSessionId`; it does not create duplicate workspaces.

### Private test and explicit cleanup

A private test is isolated from public routing and production runtime. It may retain a private staging runtime for manual inspection:

```bash
mem restores private-test <restore-session-id> --profile home
```

When the result reports `requiresExplicitDestroy: true`, clean up by workspace identity:

```bash
mem restores private-test destroy <restore-session-id> --yes --profile home
```

The cleanup command reads the canonical Restore Workspace evidence to resolve the retained staging runtime. It does not accept a validation ID, filesystem path, or raw staging ID. It removes only isolated staging containers, network, and workspace; it does not change the Backup Catalog source, production stack, or durable workspace evidence.

### Standard Recreate

Preflight is read-only and does not reserve targets or create runtime resources:

```bash
mem restores recreate preflight <restore-session-id> \
  --target-stack <target-stack-slug> \
  --element-host <element-host> \
  --profile home
```

Production execution requires all explicit acknowledgements:

```bash
mem restores recreate execute <restore-session-id> \
  --target-stack <target-stack-slug> \
  --element-host <element-host> \
  --execute-production-recreate \
  --acknowledge-creates-real-stack \
  --acknowledge-mutates-production-postgres \
  --acknowledge-mutates-npm-routes \
  --acknowledge-no-automatic-rollback \
  --profile home
```

Use `mem stack doctor <target-stack-slug>` for post-recreate stack verification.

### Cancellation and handover

```bash
mem restores cancel <restore-session-id> --yes --profile home
```

Cancellation releases temporary claims only when safe and retains catalog source/audit history.

```bash
mem restores handover complete <restore-session-id> --yes --profile home
```

Handover is a terminal audit transition. It does not generate a support report as a hidden side effect.

## Safety

- Do not use production or valuable sources for destructive tests.
- Do not use a `validationId` to create, inspect, or act on a Restore Workspace.
- `mem stack inspect`, including its `--json` projection, deliberately excludes
  container IDs/names, internal hostnames/URLs, NPM identifiers, data/config
  paths, and arbitrary runtime metadata. Use public service details and
  dedicated audited diagnostics rather than relying on hidden host internals.
- Raw non-success control-plane response bodies and transport exception text
  are not emitted in CLI human output or JSON results.
- Run `restores private-test destroy` before cancelling/deleting a source whose private test retained staging.
- Treat a missing support report as an honest not-found response; inspection commands do not create reports incidentally.
