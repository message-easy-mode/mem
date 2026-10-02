# MEM Migrate

`mem-migrate` is the separate, version-specific migration product for moving a
supported Matrix Easy Mode v0.1.0 installation to a clean MEM 0.2.0 target.

Legacy PostgreSQL, Docker, and Synapse SQLite knowledge stays here. It is not
added to the permanent MEM control plane.

## Current implementation

### MM-01 — read-only source assessment

```bash
dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- assess \
  --workspace .workspace \
  --output artifacts/assessment
```

Assessment inspects the exact supported v0.1.0 PostgreSQL schema, Docker
inventory, stack files, Synapse SQLite databases, signing keys, Element
configuration, and source topology without changing the source.


### MM-WEB-01C — browser-guided source request, capture and packaging

Start the temporary Source Assistant:

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

After access-code authentication, the browser can now:

- run the existing read-only MEM 0.1.0 assessment;
- display source classification, capture eligibility, fingerprint, warnings,
  and blockers;
- list and select the intended source stack;
- import and validate a versioned target migration request;
- recalculate and confirm the target age-recipient fingerprint;
- record the operator's encrypted-message recovery acknowledgement;
- list eligible retained plaintext captures;
- create a fresh read-only rehearsal capture;
- select a capture and create the encrypted target package;
- recover the durable source workflow after browser refresh or process restart.

The current archive contract remains installation-wide. Selecting a stack does
not remove other captured stacks from the encrypted package; target conversion
uses the selected source stack identity. Browser package download is delivered
in MM-WEB-01D. See
`docs/MM-WEB-01C-Target-Request-and-Package-Orchestration.md`.

### MM-03 — immutable live rehearsal capture

```bash
dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- source capture \
  --workspace /var/lib/mem-migrate/work \
  --output /var/lib/mem-migrate/artifacts \
  --expected-fingerprint <reviewed-assessment-sha256>
```

The capture command:

- performs a fresh supported-source assessment before copying data;
- writes only to the selected private workspace and output directory;
- creates a PostgreSQL custom-format dump through read-only `pg_dump`;
- creates consistent Synapse SQLite snapshots through SQLite online backup;
- copies homeserver configuration, signing identity, media, Element config,
  and typed/redacted runtime evidence;
- creates deterministic, ordinally ordered ZIP64 entries;
- enforces canonical paths, duplicate and case-collision rejection, entry and
  total-size limits, entry-count limits, and staging-disk headroom;
- writes SHA-256 metadata and independently verifies the completed ZIP;
- records source fingerprints before and after capture;
- always marks a live capture as rehearsal-only;
- never stops or restarts source containers.

The local output is named like:

```text
mem-v010-<capture-id>.memmigration.zip
```

A plaintext ZIP is local-only and receives owner-only permissions. It is not a
supported cross-server transfer artifact.

## Encrypted cross-server capture

Install the standard `age` CLI, obtain the intended target's recipient/public
key, and run:

```bash
dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- source capture \
  --workspace /var/lib/mem-migrate/work \
  --output /var/lib/mem-migrate/artifacts \
  --expected-fingerprint <reviewed-assessment-sha256> \
  --age-recipient age1...
```

The verified ZIP is wrapped as:

```text
mem-v010-<capture-id>.memmigration.zip.age
```

After encryption succeeds, the plaintext ZIP is removed. The source receipt
retains the plaintext and encrypted SHA-256 values without retaining secret
key material.

`mem-migrate` invokes `age` directly without a shell. It never accepts an age
private key during source capture.

## Verify and inspect

Plain local archive:

```bash
dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- source verify artifacts/capture.memmigration.zip

dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- source inspect artifacts/capture.memmigration.zip
```

Encrypted archive on the intended target:

```bash
dotnet run \
  --project src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
  -- source verify artifacts/capture.memmigration.zip.age \
  --age-identity /private/path/target-age-identity.txt
```

Verification checks the archive envelope, fixed top-level root, paths, entry
types, limits, checksum index, manifest, and every captured file hash.
Inspection reads the safe manifest summary without extracting the archive.
Encrypted inputs are decrypted only into a private temporary workspace and are
removed afterward.

## Live capture consistency

MM-03 is a preview/rehearsal capture. Source services remain running.

- PostgreSQL uses a consistent `pg_dump` snapshot.
- Each SQLite database uses the SQLite online backup API.
- Media is copied while the source remains live.
- Source assessment fingerprints are recorded before and after capture.
- Drift is reported.
- The archive is always marked `rehearsalOnly: true` and `sourceFrozen: false`.

The final source freeze and final authoritative capture belong to MM-06.

Development-only coexistence ports, including `:18443`, are transport evidence
only. They are excluded from canonical migration URLs and must not become
target migration behaviour. Matrix `server_name`, signing identity, and public
host identity remain authoritative.

## Archive structure

```text
mem-migration/
  migration-manifest.json
  source-assessment.json
  legacy-mem/
    canonical-export.json
    postgres.dump
    schema-fingerprint.json
  stacks/
    <source-stack-id>/
      stack-manifest.json
      synapse/
        homeserver.db
        homeserver.yaml
        signing.key
        additional-config/
      media/
      element/
        config.json
      runtime/
        docker-inspect.json
        image-identity.json
        routes.json
  checksums/
    sha256.json
  evidence/
    capture-report.json
```

The checksum index does not checksum itself. The external archive SHA-256
receipt covers the complete ZIP, including the checksum index.

## Supported default envelope

| Limit | Default |
|---|---:|
| Maximum individual entry | 10 GiB |
| Maximum expanded capture | 25 GiB |
| Maximum entries | 250,000 |
| Verification compression ratio | 100:1 |
| Required staging headroom | 2.25 × plaintext estimate; at least 3.25 × for encrypted capture; plus 256 MiB |

The CLI accepts explicit local overrides, but exceeding the configured envelope
fails before a supported archive is produced.

## Resume semantics

A capture ID is durably journalled in the existing private
`mem-migrate.sqlite` database.

```bash
mem-migrate source capture \
  --capture-id <id> \
  --resume \
  ...
```

A completed capture is returned idempotently. An incomplete capture restarts
from a clean private staging directory under the same capture ID and performs a
fresh assessment. MM-03 does not claim file-level media-copy continuation.

## Prerequisites

- Linux host running the supported MEM v0.1.0 installation
- .NET 8 SDK when building from source
- Docker CLI and permission to inspect Docker
- running legacy PostgreSQL container for full capture
- local read access to assessed stack paths
- `age` only for encrypted transfer or encrypted verify/inspect

The tool invokes `psql` and `pg_dump` inside the assessed PostgreSQL container.
It never requests or prints the PostgreSQL password.

## Build and test

```bash
dotnet restore Mem.Migrate.sln
dotnet build Mem.Migrate.sln --no-restore
dotnet test Mem.Migrate.sln --no-build
```

Focused MM-03 tests:

```bash
dotnet test \
  tests/Mem.Migrate.ArchiveTests/Mem.Migrate.ArchiveTests.csproj

dotnet test \
  tests/Mem.Migrate.IntegrationTests/Mem.Migrate.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SqliteSnapshotterTests|FullyQualifiedName~SqliteCaptureJournalTests"
```

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Successful operation |
| `2` | Successful supported operation with warnings |
| `10` | Blocked |
| `11` | Unsupported source |
| `12` | Ambiguous source |
| `13` | Current target already present |
| `14` | Invalid or unverifiable migration archive |
| `64` | Invalid command line |
| `70` | Execution failure |

## Security notes

- Docker and age commands are executed without a shell.
- PostgreSQL queries and dump arguments are typed and predefined.
- No public network listener is introduced.
- Archive paths are canonical and fixed beneath `mem-migration/`.
- ZIP extraction is not required for inspect or verify.
- Symlink/special archive entries and source symlinks are rejected.
- Reports contain no password, access token, signing-key contents, or database
  dump contents.
- The archive itself contains security-sensitive Matrix and database material;
  use age encryption for any cross-host transfer.
- Source configuration, database, containers, routes, and restart policies are
  not modified by MM-03.

## Known MM-03 boundaries

- Live media copy is not atomic; every MM-03 capture is rehearsal-only.
- MM-03 does not convert Synapse SQLite to PostgreSQL. MM-04 owns that proof.
- MM-03 does not upload to or call the MEM v0.1.1 control plane.
- MM-03 does not stop, freeze, restart, or retire source services.
- Final cutover capture and rollback orchestration are later slices.
- The first release remains English-only.

## MEM Migrate Source Assistant foundation

`mem-migrate-web` is the temporary browser surface for the source-side
migration journey. MM-WEB-01A provides the security and hosting foundation;
source assessment, capture, packaging, and download are added in later slices.

Build the React client and run the Web host from source:

```bash
cd src/Mem.Migrate.Web/ClientApp
npm ci
npm run test
npm run build

cd ../../..
dotnet run \
  --project src/Mem.Migrate.Web/Mem.Migrate.Web.csproj
```

The safe default listens only on:

```text
http://127.0.0.1:7391
```

For access from another computer, keep the application on loopback and create
an SSH tunnel:

```bash
ssh -L 7391:127.0.0.1:7391 <operator>@<source-host>
```

Then open `http://localhost:7391` and enter the access code printed by the
`mem-migrate-web` process. Stopping the process removes the temporary
management surface.

A direct trusted-LAN or VPN bind requires both an explicit address and the
`--allow-remote` acknowledgement. Wildcard listener addresses and public
internet exposure are unsupported.

See `docs/MM-WEB-01A-Source-Assistant-Foundation.md` for the current security,
build, publish, and installation contract.

## Secure browser package download

MM-WEB-01D completes the Source Assistant browser handoff after package
encryption.

A package-ready workflow provides:

```text
Download encrypted package
Download package report
Delete local encrypted package
```

The `.memmigration.zip.age` file is streamed from the private package root with
range processing and no-cache headers. React does not buffer the package in
application memory.

Deletion is blocked while a browser download is active and removes only the
local encrypted package and its package reports. The verified plaintext source
capture, source journal and old MEM installation remain available.

See `docs/MM-WEB-01D-Secure-Package-Download-and-Local-Lifecycle.md`.

