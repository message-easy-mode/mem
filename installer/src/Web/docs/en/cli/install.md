---
id: "cli/install"
translationKey: "cli/install"
locale: "en"
groupId: "cli"
groupKey: "cli"
groupLabel: "CLI and automation"
groupOrder: 80
title: "Install the MEM CLI"
description: "Verify or install the root-owned mem host command, provide Secret Service support, and understand the current release-packaging boundary."
order: 41
status: "advanced"
appliesTo: ["0.2.x"]
tags: ["CLI", "install", "host command", "Secret Service", "release packaging"]
route: "/docs/cli/install"
aliases: []
outputPath: "docs/cli/install.md"
preserveLegacyBranding: false
---
# Install the MEM CLI

## Outcome

A correct installation provides one stable command:

```text
/opt/mem/cli/<version>/mem
/usr/local/bin/mem -> /opt/mem/cli/<version>/mem
```

The versioned binary is root-owned and executable. Normal operators invoke `mem` without `sudo`.

## Check whether the release installed it

```bash
command -v mem
mem --version
mem --help
ls -l /usr/local/bin/mem
```

Do not assume the CLI is installed merely because the Control Plane is installed. In the current source baseline, the installer integration exists but is disabled by default until release packaging supplies and enables a prebuilt CLI binary. The public release package must be checked before this page is treated as an automatic-install promise.

## Linux credential-store prerequisite

Device login stores its opaque credential through Secret Service using `secret-tool`:

```bash
sudo apt install libsecret-tools
secret-tool --help
```

Package presence is not enough. The non-root account that runs `mem login --device` must have a usable, unlocked Secret Service-compatible keyring. A headless SSH session may not have one.

MEM fails closed when the secure store is unavailable. It does not fall back to a plaintext file, profile, environment variable, command argument, or stdin.

## Install a supplied release binary

Use the CLI-owned installer when you have the release binary and matching source script:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0
```

The script checks for `secret-tool`, installs the binary with mode `0755`, and updates `/usr/local/bin/mem` to the selected version.

## Development-only source publish

From the repository root on a development machine with the .NET 8 SDK:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --version dev

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --version dev
```

This publishes a self-contained `linux-x64` single-file binary before installing it. It is not a substitute for proving the official release artifact.

## Verify success

```bash
command -v mem
readlink -f /usr/local/bin/mem
ls -l /opt/mem/cli/<version>/mem
mem --version
mem --help
```

If the shell still resolves an older command, run:

```bash
hash -r
```

## Safety and failure handling

- Use `sudo` only for package installation or updating the root-owned command.
- Do not run normal profile, login, backup, or restore commands with `sudo`; root has a different configuration directory and keyring context.
- Do not copy the binary into an operator-writable production path.
- Do not skip the `secret-tool` check for a normal installation.
- Preserve the old version directory until the replacement has passed `mem --version`, `mem --help`, and a device-login proof.

## Related documentation

- [Profiles and servers](profiles.md)
- [Sign in with device login](device-login.md)
- [CLI troubleshooting](troubleshooting.md)
