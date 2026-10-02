# MEM Migrate Bootstrap

This directory contains the source-controlled one-line installer for MEM Migrate.

## Current implementation boundary

`MM-BOOT-01B` extends the host assurance delivered by `MM-BOOT-01A` and adds a verified, Git-free release installation path.

It:

- supports Ubuntu Server 24.04 on amd64/x86_64;
- requires a reachable existing Docker daemon and never installs or changes Docker;
- preserves a working `age` command or installs the supported Ubuntu package;
- validates HTTPS/archive transport dependencies;
- resolves a flat `release.json` manifest;
- supports exact version and channel requirements for development, stable, and prerelease releases;
- supports a local `--bundle` development/offline proof path;
- downloads the archive and adjacent `.sha256` file over HTTPS;
- verifies the manifest checksum, checksum file, archive checksum, fixed archive root, safe entry types, and inner `SHA256SUMS`;
- invokes the existing atomic installer beneath `/opt/mem/migrate`;
- skips installation safely when the requested release is already current;
- preserves `/var/lib/mem-migrate` state;
- does not require Git, .NET, Node.js, npm, or a source checkout on the source server.

Stable `mem-migrate-start`, `mem-migrate-stop`, and `mem-migrate-status` host commands, plus the complete installed SPA self-test, are delivered by `MM-BOOT-01C`.

## One-line hosted shape

The release host directory must contain:

```text
install-mem-migrate.sh
release.json
mem-migrate-<version>-linux-x64.tar.gz
mem-migrate-<version>-linux-x64.tar.gz.sha256
```

Install or update:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- --release-base-url https://<release-host>
```

Pin the exact version:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host> \
      --version <release-version>
```

Dry-run downloads only `release.json`, not the archive:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host> \
      --dry-run
```

## Local bundle proof

The publisher places the generated contract under the deployment repository's `release-bundle/` directory.

```bash
sudo ./release-bundle/install-mem-migrate.sh \
  --bundle "$(find release-bundle -maxdepth 1 -name '*.tar.gz' -print -quit)" \
  --channel dev
```

The local archive requires `<archive>.sha256` plus a sibling manifest. Component/development bundles use `release.json`; the unified public release uses `mem-migrate-<version>-release.json`, which the bootstrap resolves automatically from the archive filename.

## Publisher integration

The source-controlled helper is:

```text
migrate/scripts/create-release-bundle.sh
```

`publish-to-deploy-repo.sh` continues generating the existing development-repository layout and additionally creates:

```text
release-bundle/
├── install-mem-migrate.sh
├── release.json
├── mem-migrate-<version>-linux-x64.tar.gz
└── mem-migrate-<version>-linux-x64.tar.gz.sha256
```

The archive contains one fixed root:

```text
mem-migrate-release/
├── VERSION
├── BUILD-INFO.json
├── SHA256SUMS
├── payload/
├── scripts/install-current.sh
├── install.sh
├── run-web.sh
├── stop-web.sh
└── status.sh
```

## Docker boundary

The bootstrap validates:

```bash
docker --version
docker info
```

It never installs Docker, changes Docker repositories, enables services, restarts containers, or performs Docker mutations.

## Automated tests

```bash
cd migrate

bash -n bootstrap/install.sh
bash -n bootstrap/tests/bootstrap-tests.sh
bash -n bootstrap/tests/release-bundle-tests.sh
bash -n scripts/create-release-bundle.sh
bash -n scripts/publish-to-deploy-repo.sh

./bootstrap/tests/bootstrap-tests.sh
./bootstrap/tests/release-bundle-tests.sh
```

The fixture suites do not call the real package manager, Docker daemon, network, or `/opt/mem/migrate` installation root.

## Security and state

The bootstrap fails on:

- malformed or unsupported manifests;
- channel, version, or runtime mismatch;
- missing outer checksum files;
- archive checksum disagreement;
- content outside the fixed archive root;
- parent traversal, links, device nodes, pipes, or duplicate archive paths;
- missing or unverified inner files;
- an incomplete payload;
- an unusable `age` command;
- missing or unreachable Docker.

Temporary files use a private directory and are removed on exit. Existing migration journals, captures, packages, and evidence beneath `/var/lib/mem-migrate` are not modified by this slice.
