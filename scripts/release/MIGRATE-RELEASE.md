# MEM Migrate Public Release Alignment

`RELEASE-020-MIGRATE-03D` preserves MEM Migrate's existing component release
bundle and bootstrap while mapping it into the unified Message Easy Mode product
release.

## Public assets

For MEM 0.2.0:

```text
install-mem-migrate-0.2.0.sh
mem-migrate-0.2.0-release.json
mem-migrate-0.2.0-linux-x64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz.sha256
```

The product-level `release.json` lists all four artifacts.

The component manifest keeps its existing schema and is versioned specifically so
it can coexist with the product-level `release.json`.

## Why the component manifest remains

The existing MEM Migrate source-server bootstrap already uses a small component
manifest plus archive checksum sidecar. That path has dedicated tests and
fail-closed archive validation.

03D does not replace that mature bootstrap contract merely to make every component
look identical.

Instead, the unified product release records the component bootstrap, manifest,
archive and sidecar as four exact versioned assets.

## Build

On a clean repository:

```bash
cd "$(git rev-parse --show-toplevel)"

./scripts/release/build-migrate-release.sh \
  --version 0.2.0 \
  --output-dir ./artifacts/migrate
```

A dirty development proof is explicit:

```bash
./scripts/release/build-migrate-release.sh \
  --version 0.2.0 \
  --allow-dirty \
  --output-dir /tmp/mem-migrate-release-proof
```

The generated `*.build-info.json` records whether the result is release eligible.

## Exact source identity

Normal mode creates a temporary deployment repository and invokes:

```text
migrate/scripts/publish-to-deploy-repo.sh
```

with an exact release version.

That existing publisher still performs the substantive Migrate release work:

- builds the Source Assistant client;
- publishes CLI and Web self-contained payloads;
- verifies CLI/Source Assistant versions;
- verifies conversion-worker help;
- starts the Source Assistant on loopback and checks `/api/health`;
- builds the deployment payload;
- writes inner `SHA256SUMS`;
- creates and verifies the fixed-root release archive.

The root producer then maps that tested component release to the canonical unified
product filenames.

## Install shape

A versioned public install can use the existing Migrate bootstrap with the exact
component manifest, for example:

```bash
curl -fsSLO <release>/install-mem-migrate-0.2.0.sh

sudo bash install-mem-migrate-0.2.0.sh \
  --version 0.2.0 \
  --channel stable \
  --release-manifest-url <release>/mem-migrate-0.2.0-release.json
```

For an offline/local install using the exact public release files, the same bootstrap now resolves the sibling versioned manifest automatically:

```bash
sudo bash install-mem-migrate-0.2.0.sh \
  --bundle ./mem-migrate-0.2.0-linux-x64.tar.gz \
  --version 0.2.0 \
  --channel stable
```

The bootstrap prefers `mem-migrate-<version>-release.json` for this public layout while retaining `release.json` compatibility for component/development bundles.

The release archive's `VERSION` and `BUILD-INFO.json` are retained in the activated `/opt/mem/migrate/releases/<version>` root, and the Control Plane image carries equivalent embedded provenance under `/opt/mem/migrate/current`.

The final website will provide the concrete release URL only after publication
and staging proof.

## QA boundary

Release packaging acceptance does not certify every migration workflow.

MEM Migrate must still complete its dedicated final functional QA campaign
against the final 0.1.0 source and 0.2.0 target before public release acceptance.
