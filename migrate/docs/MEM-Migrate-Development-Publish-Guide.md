# MEM Migrate Development Build, Publish and Two-Sided Installation Guide

**Status:** Authoritative development deployment workflow
**Applies to:** MEM Migrate CLI, Source Assistant, Control Plane development conversion worker, and source-server installation
**Target runtime:** Self-contained Linux x64
**Canonical source:** `$MEM_REPO_ROOT/migrate/`
**Deployment-only repository:** `$MEM_MIGRATE_DEPLOY_REPO/`

## Portable development paths

From anywhere inside the MEM checkout, initialize these variables once in each shell used for the workflow:

```bash
export MEM_REPO_ROOT="$(git rev-parse --show-toplevel)"
export MEM_MIGRATE_DEPLOY_REPO="${MEM_MIGRATE_DEPLOY_REPO:-$(cd "$MEM_REPO_ROOT/.." && pwd -P)/mem-migrate}"
```

`MEM_MIGRATE_DEPLOY_REPO` matches the publisher's default sibling-repository layout but can be overridden explicitly. The examples below avoid developer-specific absolute paths.

## 1. Purpose

This guide defines the complete development workflow for:

1. validating and building MEM Migrate from canonical source;
2. publishing the CLI and Source Assistant as one self-contained Linux payload;
3. creating a verified release bundle;
4. synchronising the matching target conversion worker on the Control Plane development host;
5. delivering the same publication to a MEM v0.1.0 source server;
6. installing source-side dependencies and the full Source Assistant product;
7. proving that both sides use the same release and source commit;
8. running the Source Assistant through a loopback SSH tunnel.

MEM Migrate is a two-sided migration component:

```text
Canonical source
$MEM_REPO_ROOT/migrate/

        │ build and publish once
        ▼

Deployment repository
$MEM_MIGRATE_DEPLOY_REPO/

        ├──────────────────────────────────────────────┐
        │                                              │
        ▼                                              ▼

Control Plane development host              MEM v0.1.0 source server
/opt/mem/migrate/dev                        /opt/mem/migrate/current

mem-migrate conversion worker               mem-migrate CLI
libe_sqlite3.so                              mem-migrate-web
matching self-contained runtime              libe_sqlite3.so
                                             compiled React assets
                                             matching self-contained runtime
```

A source-server-only publication is incomplete. Before migration testing, the Control Plane development worker and the source-server installation must come from the same publication.

## 2. Important boundaries

### 2.1 Canonical source

All source changes are made in:

```text
$MEM_REPO_ROOT/migrate/
```

Do not edit generated binaries in the deployment repository or installed release directories.

### 2.2 Deployment repository

Generated deployment content is written to:

```text
$MEM_MIGRATE_DEPLOY_REPO/
```

It contains runnable artifacts, release metadata, checksums, helper scripts, and the portable release bundle. It is not canonical source.

### 2.3 Target and source roles differ

The Control Plane development host needs the conversion worker at:

```text
/opt/mem/migrate/dev/mem-migrate
```

The MEM v0.1.0 source server needs the complete Source Assistant product at:

```text
/opt/mem/migrate/current
```

The target does not normally run `mem-migrate-web`, but the complete published payload is installed so the CLI retains every tested runtime companion file.

### 2.4 Docker policy

Neither installer installs, upgrades, restarts, or reconfigures Docker. Docker and its daemon must already be available.

### 2.5 `age` policy

The source bootstrap installs and validates `age` when missing on supported Ubuntu 24.04 source servers.

The Control Plane development worker installer validates the existing target-side `age` command but does not modify the development host’s package repositories.

### 2.6 Runtime state is never published

Do not copy or commit:

```text
mem-migrate.sqlite
SQLite WAL or SHM files
assessment or capture journals
migration archives or encrypted packages
age identities
Matrix signing keys
database dumps
credentials or environment files
logs containing sensitive material
```

Normal source-side state remains beneath:

```text
/var/lib/mem-migrate/work
/var/lib/mem-migrate/artifacts
```

## 3. Required development tools

The development machine requires:

```text
Git
.NET 8 SDK
Node.js and npm
Python 3
curl
sha256sum
tar
standard GNU/Linux shell tools
Docker
age
```

Verify:

```bash
git --version
dotnet --version
node --version
npm --version
python3 --version
curl --version
sha256sum --version
tar --version
docker version
age --version
```

## 4. Validate source changes

Run the narrowest relevant tests first.

Typical backend and Source Assistant gates:

```bash
cd "$MEM_REPO_ROOT/migrate"

dotnet test tests/Mem.Migrate.Web.Tests/Mem.Migrate.Web.Tests.csproj
```

```bash
cd src/Mem.Migrate.Web/ClientApp
npm run test
npm run build
```

Bootstrap and deployment gates:

```bash
cd "$MEM_REPO_ROOT/migrate"

bash -n bootstrap/install.sh
bash -n scripts/create-release-bundle.sh
bash -n scripts/publish-to-deploy-repo.sh
bash -n scripts/install-dev-target-worker.sh

./bootstrap/tests/bootstrap-tests.sh
./bootstrap/tests/release-bundle-tests.sh
./scripts/tests/install-dev-target-worker-tests.sh
```

Use the full solution gate when focused tests expose shared problems or at programme close-out:

```bash
dotnet restore Mem.Migrate.sln
dotnet build Mem.Migrate.sln --no-restore
dotnet test Mem.Migrate.sln --no-build
```

Do not publish while a relevant gate is failing.

## 5. Publish one release

The authoritative publisher is:

```text
migrate/scripts/publish-to-deploy-repo.sh
```

Run it as the normal development user, not with `sudo`:

```bash
cd "$MEM_REPO_ROOT"

./migrate/scripts/publish-to-deploy-repo.sh
```

Optional arguments:

```bash
./migrate/scripts/publish-to-deploy-repo.sh \
  --deploy-repo "$MEM_MIGRATE_DEPLOY_REPO" \
  --runtime linux-x64 \
  --configuration Release
```

The publisher:

- requires a clean deployment repository;
- records the canonical source commit and branch;
- marks dirty source builds explicitly;
- builds the React client;
- publishes the CLI and Source Assistant self-contained for `linux-x64`;
- validates both executables;
- validates `libe_sqlite3.so`;
- validates conversion worker request schema version 2;
- validates compiled static assets;
- performs a loopback Source Assistant health check;
- creates `VERSION`, `BUILD-INFO.json`, and `SHA256SUMS`;
- creates the Git deployment helpers;
- creates the portable verified `release-bundle/`;
- prints the required target-worker synchronisation command.

It does not:

```text
install the target worker
git add
git commit
git push
```

## 6. Generated deployment content

After publication:

```text
mem-migrate/
├── payload/
│   ├── mem-migrate
│   ├── mem-migrate-web
│   ├── libe_sqlite3.so
│   ├── wwwroot/
│   └── self-contained runtime files
├── release-bundle/
│   ├── install-mem-migrate.sh
│   ├── release.json
│   ├── mem-migrate-<version>-linux-x64.tar.gz
│   └── mem-migrate-<version>-linux-x64.tar.gz.sha256
├── scripts/
├── VERSION
├── BUILD-INFO.json
├── SHA256SUMS
├── install.sh
├── run-web.sh
├── stop-web.sh
├── status.sh
└── README.md
```

`BUILD-INFO.json` records the release version, product versions, runtime, configuration, publication time, canonical source commit, source branch, dirty state, release channel, and release eligibility. Public/installable metadata deliberately omits the producer checkout path and source remote so local filesystem layout and private SCM endpoints are never shipped in the release archive.

## 7. Review the publication

```bash
cd "$MEM_MIGRATE_DEPLOY_REPO"

git status --short
git diff --stat
git diff

cat VERSION
cat BUILD-INFO.json
sha256sum -c SHA256SUMS
cat release-bundle/release.json

(
  cd release-bundle
  sha256sum -c ./*.tar.gz.sha256
  tar -tzf ./*.tar.gz | sed -n '1,80p'
)
```

The archive must contain only the fixed root:

```text
mem-migrate-release/
```

## 8. Install the matching Control Plane development worker

Dry-run:

```bash
cd "$MEM_REPO_ROOT"

sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload" \
  --dry-run
```

Apply:

```bash
sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload"
```

The helper:

- validates x86-64 architecture;
- validates target `age` and Docker daemon availability;
- validates `mem-migrate`, `libe_sqlite3.so`, release metadata, and worker schema version 2;
- installs the full payload beneath `/opt/mem/migrate/dev-releases/<version>`;
- writes `TARGET-WORKER-INFO.json`;
- atomically activates the release through `/opt/mem/migrate/dev`;
- preserves the old unversioned `/opt/mem/migrate/dev` directory on first conversion to the versioned model;
- retains older versioned releases for deliberate rollback;
- validates the active worker after activation.

Verify:

```bash
readlink -f /opt/mem/migrate/dev
sudo /opt/mem/migrate/dev/mem-migrate version
sudo /opt/mem/migrate/dev/mem-migrate worker convert --help
cat /opt/mem/migrate/dev/VERSION
python3 -m json.tool /opt/mem/migrate/dev/BUILD-INFO.json
python3 -m json.tool /opt/mem/migrate/dev/TARGET-WORKER-INFO.json
```

Restart the Control Plane development API after activation. The active API should then validate and log the new target worker.

## 9. Deliver the same publication to the source server

### 9.1 Direct SCP development path

```bash
scp -r \
  "$MEM_MIGRATE_DEPLOY_REPO/release-bundle" \
  <operator>@<source-server>:/tmp/mem-migrate-release-bundle
```

This path requires no Git push or pull.

On the source server:

```bash
ssh <operator>@<source-server>

cd /tmp/mem-migrate-release-bundle
ARCHIVE="$(find . -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
test -n "$ARCHIVE"

sudo ./install-mem-migrate.sh \
  --bundle "$ARCHIVE" \
  --channel dev \
  --dry-run

sudo ./install-mem-migrate.sh \
  --bundle "$ARCHIVE" \
  --channel dev
```

### 9.2 Git deployment path

The deployment repository may still be committed and pushed for durable history:

```bash
cd "$MEM_MIGRATE_DEPLOY_REPO"

git add .
git commit -m "Publish MEM Migrate $(cat VERSION)"
git push
```

On the source server:

```bash
cd ~/mem-migrate
sudo ./stop-web.sh
git status --short
git pull --ff-only
sha256sum -c SHA256SUMS
sudo ./install.sh
sudo ./run-web.sh
```

Do not update while a capture, package operation, or another source workflow is running.

## 10. Verify same-build compatibility

Control Plane development host:

```bash
sudo /opt/mem/migrate/dev/mem-migrate version
cat /opt/mem/migrate/dev/VERSION
python3 -c 'import json; print(json.load(open("/opt/mem/migrate/dev/BUILD-INFO.json"))["sourceCommit"])'
```

Source server:

```bash
sudo /opt/mem/migrate/current/mem-migrate version
cat /opt/mem/migrate/current/VERSION
python3 -c 'import json; print(json.load(open("/opt/mem/migrate/current/BUILD-INFO.json"))["sourceCommit"])'
```

The product version, release version, and source commit must match before migration testing.

This check prevents an updated source package producer from being tested against a stale target conversion worker.

## 11. Run the Source Assistant

On the source server:

```bash
sudo install -d -m 0700 \
  /var/lib/mem-migrate \
  /var/lib/mem-migrate/work \
  /var/lib/mem-migrate/artifacts

sudo /opt/mem/migrate/current/mem-migrate-web \
  --listen-address 127.0.0.1 \
  --port 7391 \
  --workspace /var/lib/mem-migrate/work \
  --artifacts /var/lib/mem-migrate/artifacts
```

Keep the terminal open and note the access code.

From the operator workstation:

```bash
ssh -N \
  -L 7391:127.0.0.1:7391 \
  <operator>@<source-server>
```

Open:

```text
http://localhost:7391/
```

Do not expose port `7391` directly to the public internet.

## 12. Smoke and migration proof

Source-server smoke checks:

```bash
curl -fsS http://127.0.0.1:7391/api/health | python3 -m json.tool
curl -fsSI http://127.0.0.1:7391/ | sed -n '1,10p'
```

Expected:

```text
/api/health  → ready
/            → HTTP 200 with HTML
```

Before a full migration proof, confirm:

- target and source build identities match;
- the Control Plane API was restarted after target-worker activation;
- the target worker advertises request schema version 2;
- the source server has working `age` and Docker;
- the Source Assistant is loopback-only;
- for multi-stack sources, a selected source stack is passed or explicitly selected before conversion.

## 13. Updating later publications

Every later development publication repeats both installation paths:

```text
publish once
→ install matching /opt/mem/migrate/dev target worker
→ restart Control Plane API
→ install the same release on /opt/mem/migrate/current source server
→ compare version and source commit
→ run migration tests
```

Installing only the source-server bundle does not synchronise the Control Plane development worker.

## 14. Troubleshooting

### Deployment repository is dirty

```bash
cd "$MEM_MIGRATE_DEPLOY_REPO"
git status --short
```

Commit, discard, or move unexpected changes before publishing.

### Target worker is stale

Inspect:

```bash
readlink -f /opt/mem/migrate/dev
sudo /opt/mem/migrate/dev/mem-migrate version
cat /opt/mem/migrate/dev/VERSION
python3 -m json.tool /opt/mem/migrate/dev/BUILD-INFO.json
```

Reinstall from the current deployment payload:

```bash
cd "$MEM_REPO_ROOT"

sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload"
```

Restart the Control Plane API afterwards.

### Target worker contract is rejected

The installer requires:

```text
mem-migrate worker convert --request <absolute-request.json>
mem-conversion-worker-request version 2
```

Republish from current canonical source. Do not weaken the contract check.

### Target `age` is unavailable

The development worker helper validates but does not install `age`. Repair the development host package through its normal OS administration process, then rerun the helper.

### Docker daemon is unavailable

The helper does not modify Docker. Restore Docker daemon access, then rerun the dry-run.

### Source-server checksum verification fails

Do not install. Recopy the release bundle or restore the deployment checkout to its committed state and verify checksums again.

### Source Assistant is not reachable

On the source server, verify the process and loopback health. On the workstation, verify the SSH tunnel remains open.

## 15. Quick reference

```bash
# Development host: publish
cd "$MEM_REPO_ROOT"
./migrate/scripts/publish-to-deploy-repo.sh

# Development host: synchronise target conversion worker
sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload"

# Restart the Control Plane development API using the normal development command.

# Development host: copy the same source bundle
scp -r \
  "$MEM_MIGRATE_DEPLOY_REPO/release-bundle" \
  <operator>@<source-server>:/tmp/mem-migrate-release-bundle

# Source server: install
cd /tmp/mem-migrate-release-bundle
ARCHIVE="$(find . -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
sudo ./install-mem-migrate.sh --bundle "$ARCHIVE" --channel dev --dry-run
sudo ./install-mem-migrate.sh --bundle "$ARCHIVE" --channel dev

# Source server: run
sudo /opt/mem/migrate/current/mem-migrate-web \
  --listen-address 127.0.0.1 \
  --port 7391 \
  --workspace /var/lib/mem-migrate/work \
  --artifacts /var/lib/mem-migrate/artifacts

# Workstation: tunnel
ssh -N -L 7391:127.0.0.1:7391 <operator>@<source-server>
```

## 16. Retired development assumptions

The following assumptions are retired:

- publishing only a CLI directory into a local `publish/` folder;
- updating only the MEM v0.1.0 source server;
- manually copying an unversioned worker into `/opt/mem/migrate/dev`;
- assuming source and target workers are compatible without comparing build identity;
- beginning migration tests before restarting the Control Plane API after a worker update.

The authoritative workflow is now a single publication with two verified installations.
