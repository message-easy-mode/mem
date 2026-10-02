# MEM Migrate Quick Two-Sided Development Deployment Guide

**Applies to:** `MM-BOOT-01B-C01` development publications  
**Purpose:** Publish MEM Migrate once, install the matching conversion worker on the Control Plane development host, copy the release bundle to a MEM v0.1.0 source server, install it without Git, and run the Source Assistant.

> Replace `<operator>` and `<source-server>` with the actual SSH user and source-server address.

## Portable development paths

From anywhere inside the MEM checkout, initialize these variables once in each shell used for the workflow:

```bash
export MEM_REPO_ROOT="$(git rev-parse --show-toplevel)"
export MEM_MIGRATE_DEPLOY_REPO="${MEM_MIGRATE_DEPLOY_REPO:-$(cd "$MEM_REPO_ROOT/.." && pwd -P)/mem-migrate}"
```

`MEM_MIGRATE_DEPLOY_REPO` matches the publisher's default sibling-repository layout but can be overridden explicitly. The examples below avoid developer-specific absolute paths.

## 1. Run the relevant tests

Run the focused tests for the current slice before publishing. At minimum for bootstrap or deployment changes:

```bash
cd "$MEM_REPO_ROOT/migrate"

./bootstrap/tests/bootstrap-tests.sh
./bootstrap/tests/release-bundle-tests.sh
./scripts/tests/install-dev-target-worker-tests.sh
```

Do not publish while a relevant focused gate is failing.

## 2. Publish on the development machine

```bash
cd "$MEM_REPO_ROOT"

./migrate/scripts/publish-to-deploy-repo.sh
```

The generated deployment content is written to:

```text
$MEM_MIGRATE_DEPLOY_REPO/
```

The portable bundle is written to:

```text
$MEM_MIGRATE_DEPLOY_REPO/release-bundle/
```

## 3. Review the publication

```bash
cd ~/Code/MatrixEasyMode/mem-migrate

git status --short
git diff --stat
cat VERSION
cat BUILD-INFO.json
sha256sum -c SHA256SUMS
cat release-bundle/release.json

(
  cd release-bundle
  sha256sum -c ./*.tar.gz.sha256
)
```

## 4. Synchronise the Control Plane development worker

The Control Plane uses the target-side conversion worker through:

```text
/opt/mem/migrate/dev/mem-migrate
```

Dry-run the local installation:

```bash
cd "$MEM_REPO_ROOT"

sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload" \
  --dry-run
```

Install the exact publication:

```bash
sudo ./migrate/scripts/install-dev-target-worker.sh \
  --payload "$MEM_MIGRATE_DEPLOY_REPO/payload"
```

The helper installs versioned releases beneath:

```text
/opt/mem/migrate/dev-releases/<version>
```

and atomically activates the selected release through:

```text
/opt/mem/migrate/dev
```

Verify:

```bash
sudo /opt/mem/migrate/dev/mem-migrate version

sudo /opt/mem/migrate/dev/mem-migrate \
  worker convert --help | tail -n 20

cat /opt/mem/migrate/dev/VERSION
python3 -m json.tool /opt/mem/migrate/dev/BUILD-INFO.json
python3 -m json.tool /opt/mem/migrate/dev/TARGET-WORKER-INFO.json
```

Restart the Control Plane development API before migration testing so startup validation and runtime logs reflect the new worker.

## 5. Copy the bundle to the source server

```bash
scp -r \
  ~/Code/MatrixEasyMode/mem-migrate/release-bundle \
  <operator>@<source-server>:/tmp/mem-migrate-release-bundle
```

No Git push or pull is required for this development test path.

## 6. Dry-run the source-server installer

```bash
ssh <operator>@<source-server>

cd /tmp/mem-migrate-release-bundle
ARCHIVE="$(find . -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"

test -n "$ARCHIVE"

sudo ./install-mem-migrate.sh \
  --bundle "$ARCHIVE" \
  --channel dev \
  --dry-run
```

The dry-run must not modify packages, releases, MEM Migrate state, or Docker.

## 7. Install MEM Migrate on the source server

```bash
sudo ./install-mem-migrate.sh \
  --bundle "$ARCHIVE" \
  --channel dev
```

The full source-side product is installed beneath:

```text
/opt/mem/migrate/releases/<version>
```

and activated through:

```text
/opt/mem/migrate/current
```

## 8. Verify both sides use the same build

On the Control Plane development host:

```bash
TARGET_PRODUCT_VERSION="$(sudo /opt/mem/migrate/dev/mem-migrate version)"
TARGET_RELEASE_VERSION="$(cat /opt/mem/migrate/dev/VERSION)"
TARGET_SOURCE_COMMIT="$(python3 -c 'import json; print(json.load(open("/opt/mem/migrate/dev/BUILD-INFO.json"))["sourceCommit"])')"

printf 'Target product: %s\n' "$TARGET_PRODUCT_VERSION"
printf 'Target release: %s\n' "$TARGET_RELEASE_VERSION"
printf 'Target commit:  %s\n' "$TARGET_SOURCE_COMMIT"
```

On the MEM v0.1.0 source server:

```bash
SOURCE_PRODUCT_VERSION="$(sudo /opt/mem/migrate/current/mem-migrate version)"
SOURCE_RELEASE_VERSION="$(cat /opt/mem/migrate/current/VERSION)"
SOURCE_SOURCE_COMMIT="$(python3 -c 'import json; print(json.load(open("/opt/mem/migrate/current/BUILD-INFO.json"))["sourceCommit"])')"

printf 'Source product: %s\n' "$SOURCE_PRODUCT_VERSION"
printf 'Source release: %s\n' "$SOURCE_RELEASE_VERSION"
printf 'Source commit:  %s\n' "$SOURCE_SOURCE_COMMIT"
```

Before migration testing, confirm that the product version, release version, and source commit agree across both machines.

## 9. Run the Source Assistant

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

Keep this terminal open and note the printed access code.

## 10. Open it from the operator workstation

```bash
ssh -N \
  -L 7391:127.0.0.1:7391 \
  <operator>@<source-server>
```

Open:

```text
http://localhost:7391/
```

Enter the access code printed on the source server.

## 11. Smoke test

From another source-server terminal:

```bash
curl -fsS http://127.0.0.1:7391/api/health | python3 -m json.tool
curl -fsSI http://127.0.0.1:7391/ | sed -n '1,10p'
```

Expected:

```text
/api/health  → ready
/            → HTTP 200 with HTML
```

Then exercise the intended migration workflow. For a multi-stack source, verify that the target Control Plane receives or explicitly requests the selected source stack before conversion.

Stop the foreground Source Assistant with `Ctrl+C` in its server terminal.

---

## Development loop

```text
change MEM Migrate source
→ run focused tests
→ publish one release
→ review checksums and build identity
→ install the matching Control Plane development worker
→ restart the Control Plane development API
→ SCP the same release bundle
→ dry-run and install on the source server
→ compare product version, release version, and source commit
→ run the Source Assistant through the SSH tunnel
→ test the complete two-sided migration path
```

Do not treat a source-server-only update as a complete MEM Migrate development deployment.
