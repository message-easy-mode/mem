# CLI-AUTH-03B-01 — Secure Credential Store Foundation

## Purpose

Establish the first supported persistent credential boundary for a future
`mem login --device` flow.

MEM CLI device credentials are opaque bearer values. They must never be placed
in:

- `config.json` or CLI profiles;
- an environment variable;
- a shell argument or shell history;
- process arguments;
- terminal output;
- JSON output;
- diagnostics, support reports, or raw error output.

This slice introduces only the secure-store abstraction and Linux Secret Service
implementation. It does **not** add `mem login`, `mem logout`, normal command
bearer transport, or a local recovery shortcut.

## Supported runtime contract

On Linux, MEM CLI uses a Secret Service-compatible keyring through
`secret-tool` (provided by `libsecret-tools` on Ubuntu-family systems).

Before a future login starts a browser approval request, the store performs a
write/read/delete probe. If the keyring or `secret-tool` is unavailable, the
future login must refuse safely. It must never fall back to a plaintext file.

The safe key identity uses only:

```text
application = matrix-easy-mode
kind        = cli-device-session
profile     = local profile name
server      = canonical profile server URL
```

The opaque credential is passed to `secret-tool store` through standard input,
never command-line arguments.

## Non-goals

- No `mem login` command yet.
- No credential persistence in the normal profile/config JSON file.
- No installer-token retirement yet.
- No normal command bearer transport yet.
- No `mem logout`/server revocation route yet.
- No browser or API changes.
- No migration.

## Touched files

```text
Mem.Cli/Config/CliDeviceCredentialStore.cs
Mem.Cli/Config/SecretToolCliDeviceCredentialStore.cs
Mem.Cli/Docs/CLI-AUTH-03B-01-Secure-Credential-Store-Contract.md
Mem.Cli.Tests/Mem.Cli.Tests/Config/SecretToolCliDeviceCredentialStoreTests.cs
```

## Apply

```bash
cd "$(git rev-parse --show-toplevel)"

./apply-slice.sh --target cli --dry-run \
  ~/Downloads/CLI-AUTH-03B-01-Secure-Credential-Store-Foundation.zip

./apply-slice.sh --target cli \
  ~/Downloads/CLI-AUTH-03B-01-Secure-Credential-Store-Foundation.zip

git diff --check
git status --short
git diff --stat
git diff
```

## Validation

Build first:

```bash
dotnet build ./cli/src/Mem.Cli/Mem.Cli.csproj
```

Focused tests:

```bash
dotnet test ./cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~SecretToolCliDeviceCredentialStoreTests"
```

Full CLI suite:

```bash
dotnet test ./cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj
```

## Manual supported-store check

This slice deliberately does not create a real credential or add a login
command. On a target Linux operator environment, confirm only that the intended
secure-store client is installed:

```bash
command -v secret-tool
secret-tool --help >/dev/null
```

Do not manually store a real MEM credential with `secret-tool`. The later login
slice performs a safe keyring round-trip probe before it starts a device
authorization request.

## Expected outcome

- The CLI has a testable, fail-closed secure credential-store boundary.
- Linux Secret Service is the only supported persistent store in this slice.
- Credential values are written through stdin, never argv.
- Profile/config files remain non-secret.
