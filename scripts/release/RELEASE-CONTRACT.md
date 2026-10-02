# MEM 0.2.0 Unified Release Artifact Contract

**Contract:** `RELEASE-020-CONTRACT-03A`  
**Schema version:** 1  
**Product:** Message Easy Mode (MEM)  
**Initial public release target:** MEM 0.2.0  
**Certified Control Plane host for this release:** Ubuntu 24.04 LTS amd64

## 1. Purpose

This document defines the product-level release contract shared by:

- the MEM public bootstrap;
- the main MEM installer bundle;
- the MEM Control Plane container image;
- the MEM CLI;
- MEM Migrate;
- the public Message Easy Mode website;
- GitHub release/publication automation.

The contract deliberately separates **release identity** from mutable channel aliases.

A public release is identified by an exact version such as:

```text
0.2.0
```

and an exact release tag such as:

```text
v0.2.0
```

A word such as `stable`, `latest`, `current`, or `dev` may be used for discovery
or development policy, but it is never the immutable identity of a published
release asset.

---

## 2. Release invariants

A MEM public release is valid only when all of these are true:

1. Every product artifact belongs to one exact MEM release version.
2. The release version is embedded or otherwise observable inside each executable/bundle where practical.
3. Every published file has a SHA-256 recorded in the product release manifest.
4. The aggregate `SHA256SUMS` covers `release.json` and every product artifact.
5. The Control Plane image is referenced by both exact version tag and immutable image digest.
6. Published filenames contain the exact release version.
7. Public release filenames do not use `latest`, `stable`, `current`, or `dev`.
8. The installer, CLI, Migrate and Control Plane image are tied to source commit identity.
9. MEM 0.2.0 does not claim a Control Plane host platform that has not been clean-room certified.
10. Publication must provide cryptographically verifiable provenance/authenticity in addition to ordinary checksums.
11. A convenience one-line installer may resolve the current release, but once a version is resolved it must use the exact release manifest and exact artifact identities.
12. Failure to resolve or verify release identity must fail closed rather than silently falling back to another version/channel.

---

## 3. Version and channel policy

### Stable

A stable version uses an ordinary semantic version:

```text
0.2.0
```

with:

```json
"channel": "stable"
```

and tag:

```text
v0.2.0
```

### Prerelease

A public release candidate uses semantic-version prerelease syntax, for example:

```text
0.2.0-rc.1
```

with:

```json
"channel": "prerelease"
```

and tag:

```text
v0.2.0-rc.1
```

### Development builds

`dev`, local timestamps, temporary build IDs and mutable development image tags
are not public release identities under schema v1.

They may exist in developer tooling, but they must not be substituted into a
published stable/prerelease manifest.

Build metadata such as source commit and generation time belongs in manifest
metadata instead of being hidden inside a mutable `stable` artifact name.

---

## 4. Product release manifest

The canonical product-level manifest is:

```text
release.json
```

The public website, bootstrap and release tooling should treat this as the
machine-readable release description.

The schema lives at:

```text
scripts/release/release-manifest.schema.json
```

The repository also provides a standard-library validator:

```bash
python3 scripts/release/validate-release-manifest.py release.json
```

For a prepared release directory:

```bash
python3 scripts/release/validate-release-manifest.py \
  release.json \
  --assets-dir ./release-output
```

The second form verifies artifact presence, byte size, SHA-256 values and the
aggregate `SHA256SUMS`.

---

## 5. Manifest model

The manifest records these boundaries.

### Product

```text
id
name
version
channel
```

For schema v1:

```text
id     mem
name   Message Easy Mode
```

### Release publication

Records:

- release generation timestamp;
- GitHub Releases as the publication model;
- the release repository;
- exact tag.

For MEM 0.2.0 the canonical public release repository is fixed as:

```text
https://github.com/message-easy-mode/mem-releases
```

This repository owns the versioned public release files and GitHub Release tag.
It is deliberately separate from the Control Plane source/image identity. The
JSON Schema remains structurally reusable, while the MEM validator enforces this
exact publication repository for the 0.2.0 release line.

### Sources

`sources[]` supports one or more source repositories.

Each source has:

```text
id
repository
commit
```

Artifacts and the Control Plane image refer to a `sourceId`.

MEM 0.2.0 uses exactly one source identity:

```text
id          control-plane
repository  https://github.com/message-easy-mode/mem
```

The source repository and release-publication repository are separate contract
boundaries. Public artifact acquisition must use `mem-releases`; source provenance
continues to bind the release to the exact MEM source commit.

The schema still supports multiple source records so a later repository split
does not require redesigning schema v1.

For product assembly, `release.generatedAtUtc` is reproducible evidence: it is
derived from the selected source commit timestamp rather than wall-clock time.
This keeps `release.json` deterministic for the same release inputs.

### Supported Control Plane host

The initial MEM 0.2.0 public release supports:

```text
Ubuntu 24.04 LTS
amd64
```

The product manifest must not advertise additional Control Plane host versions or
architectures until those targets are deliberately certified.

This is distinct from an artifact runtime. For example, a self-contained CLI may
be built as `linux-x64`, while the product's certified first-install host remains
Ubuntu 24.04 amd64.

### Control Plane image

The manifest records:

```text
repository
tag
digest
reference
sourceId
```

For a 0.2.0 stable release the intended shape is:

```text
ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest>
```

The release installer must ultimately consume the immutable digest-bearing
reference from the resolved release contract rather than independently choosing
a mutable `stable` tag.

---

## 6. Required public release artifacts

Schema v1 requires these artifact IDs.

### `bootstrap`

Versioned public bootstrap script:

```text
install-mem-<version>.sh
```

Its job is release acquisition and verification, not reimplementation of the
mature MEM bootstrap.

The stable website `/install.sh` convenience endpoint may be a small front door,
but the immutable release must also carry a versioned bootstrap asset.

### `installer`

Main MEM bootstrap/installer bundle:

```text
mem-installer-<version>-ubuntu-24.04-amd64.tar.gz
```

The inner bundle should converge on the strong MEM Migrate pattern:

```text
fixed archive root
VERSION
BUILD-INFO.json
inner SHA256SUMS
install.sh
bootstrap/
bootstrap/cli/install-host-command.sh
bootstrap/cli/mem
```

The inner manifest cannot contain the outer archive SHA-256 because that would
create a self-reference cycle.

The product-level `release.json` and aggregate `SHA256SUMS` own the outer
transport identity.

### `cli`

Standalone MEM CLI executable:

```text
mem-cli-<version>-linux-x64
```

The executable must report the same release version through `mem --version`.

The binary embedded in the installer bundle must be the same release binary as
the standalone CLI artifact; the installer producer slice must verify byte/hash
identity.

### `migrate-bootstrap`

Versioned MEM Migrate downloader/bootstrap:

```text
install-mem-migrate-<version>.sh
```

### `migrate-manifest`

Versioned MEM Migrate component manifest:

```text
mem-migrate-<version>-release.json
```

This keeps the already-tested standalone MEM Migrate bootstrap contract while avoiding a filename collision with the product-level `release.json`.

### `migrate`

MEM Migrate transport:

```text
mem-migrate-<version>-linux-x64.tar.gz
```

### `migrate-checksum`

MEM Migrate archive checksum sidecar:

```text
mem-migrate-<version>-linux-x64.tar.gz.sha256
```

The sidecar is retained because the standalone Migrate bootstrap already validates it against both the component manifest and the downloaded archive. The aggregate product `SHA256SUMS` additionally covers the sidecar itself.

MEM Migrate already has a validated inner release contract. That machinery should
be preserved.

Its component-level manifest is published under the versioned name
`mem-migrate-<version>-release.json`, avoiding any collision with the product-level
`release.json` while preserving the standalone Migrate bootstrap contract.

### `sbom`

Release SBOM:

```text
mem-<version>-sbom.spdx.json
```

The exact SBOM production tooling belongs to the supply-chain implementation
slice. Schema v1 reserves and requires the public release asset now so release
automation and website UX can rely on a stable contract.

---

## 7. Aggregate integrity file

Every release publishes:

```text
SHA256SUMS
```

It contains SHA-256 entries for:

```text
release.json
install-mem-<version>.sh
mem-installer-<version>-ubuntu-24.04-amd64.tar.gz
mem-cli-<version>-linux-x64
install-mem-migrate-<version>.sh
mem-migrate-<version>-release.json
mem-migrate-<version>-linux-x64.tar.gz
mem-migrate-<version>-linux-x64.tar.gz.sha256
mem-<version>-sbom.spdx.json
```

`SHA256SUMS` does not list itself.

The product manifest also records each artifact SHA-256 and byte size. This
intentional duplication permits local consistency checking and gives consumers a
machine-readable artifact inventory.

Checksums establish byte integrity. They do not, by themselves, establish
publisher authenticity when the artifact and checksum are obtained from the same
compromised origin.

---

## 8. Publication and authenticity policy

The initial public release model is GitHub Releases under the canonical
`message-easy-mode` organization.

Schema v1 requires the release policy to state:

```text
GitHub Releases
immutable release required
release attestation required
artifact provenance attestations required
Control Plane image provenance attestation required
```

A published stable/prerelease release is not considered complete until the
publication workflow proves those external conditions.

GitHub's release provider state is intentionally not represented as a mutable
post-publication boolean in `release.json`; the manifest states the required
policy and consumers/release automation verify the actual provider state.

A MEM-owned detached signature may be added as an additional trust layer later.
Schema v1 does not make a bespoke detached-signature format mandatory because the
required cryptographic authenticity mechanism is release/artifact provenance
attestation. If detached signatures are introduced, they must supplement rather
than replace checksums and provenance verification.

---

## 9. GitHub release rules

The release workflow should:

1. Build/test all release artifacts from reviewed source.
2. Resolve all final hashes, image digest and source commit identities.
3. Generate `release.json`.
4. Generate `SHA256SUMS`.
5. Generate provenance attestations for file artifacts and the container image.
6. Create the GitHub release as a draft.
7. Upload the complete asset set.
8. Publish only after the draft asset inventory is complete.
9. Require immutable releases for public stable/prerelease publication.
10. Verify the published release and assets after publication.

`latest` is a discovery pointer only.

An exact version download must always be able to resolve through:

```text
release tag + exact asset filename
```

without relying on the current value of `latest`.

---

## 10. Website and bootstrap consumption rules

### Convenience path

The website may offer a short one-line command.

That command can discover the latest stable release, but after discovery the
bootstrap must resolve:

```text
exact version
exact release tag
exact release.json
exact artifact filename
exact SHA-256
exact Control Plane digest
```

before installation.

### Inspect-first path

The website must also provide a path where a security-focused operator can:

1. select an exact version;
2. download the versioned bootstrap/manifest/assets;
3. inspect the script;
4. verify GitHub release immutability/release identity;
5. verify the local asset against the published release;
6. verify artifact provenance;
7. verify SHA-256;
8. run the installer only after verification.

The website must not describe checksum verification as equivalent to publisher
authentication.

---

## 11. Main installer convergence requirements for 03B

The main installer implementation slice must make the existing mature bootstrap
consume this release identity without rewriting its operator/lifecycle behaviour.

At minimum 03B must:

- stop release packaging from defaulting the CLI version to `dev`;
- package the real CLI binary;
- make the installer bundle report the exact MEM release version;
- produce an inner `VERSION`, `BUILD-INFO.json` and `SHA256SUMS` contract;
- make public release installation use the manifest's exact Control Plane
  digest-bearing image reference;
- align the public 0.2.0 host policy to Ubuntu 24.04 amd64;
- preserve explicit development overrides for local proof without allowing them
  to become public release defaults;
- add release-bundle corruption and version-mismatch tests.

---

## 12. CLI convergence requirements for 03C

The CLI release slice must:

- build one self-contained `linux-x64` release binary;
- inject 0.2.0 into assembly informational/file/product version metadata;
- make `mem --version` report the product release;
- publish the canonical versioned filename;
- generate/verify its SHA-256;
- make the installer bundle consume the same bytes as the standalone artifact;
- retain the existing root-owned versioned installation layout:

```text
/opt/mem/cli/<version>/mem
/usr/local/bin/mem
```

---

## 13. MEM Migrate convergence requirements for 03D

Preserve the existing Migrate strengths:

- Ubuntu 24.04 / linux-x64 fail-closed host/runtime policy;
- fixed-root archive;
- `VERSION`;
- `BUILD-INFO.json`;
- inner `SHA256SUMS`;
- outer archive SHA-256;
- archive verification after generation;
- atomic versioned install;
- release-bundle corruption tests.

03D should adapt publication naming/metadata to the unified product release, not
replace this machinery with a weaker generic packager.

---

## 14. Supply-chain implementation requirements for 03E

03E is split at a deliberate trust boundary.

### 03E-A — exact local product release assembly

The repository must be able to assemble, from one clean source commit:

- the exact standalone CLI;
- the versioned installer built with those exact CLI bytes;
- the exact digest-bearing Control Plane image reference;
- the release-eligible Migrate artifact set;
- an SPDX-2.3 JSON SBOM;
- the exact versioned public bootstrap;
- deterministic `release.json`; and
- aggregate `SHA256SUMS`.

The assembler emits only the schema-v1 public asset set. Component
`*.build-info.json` files are build evidence for the workflow, not additional
public release assets.

### 03E-B — trusted hosted publication

The hosted release workflow must add:

- GHCR image build/push using the reviewed production Dockerfile;
- SBOM production and vulnerability/dependency policy;
- GitHub artifact attestations for release files and the container image;
- draft GitHub Release creation and complete asset upload;
- publication only after release immutability is enabled;
- release/asset verification after publication; and
- website verification instructions.

The release workflow must not invent or predeclare the container digest; it must
consume the digest returned by the registry-backed image build.

---

## 15. Example manifest

See:

```text
scripts/release/examples/release-0.2.0.example.json
```

Digest, size and commit values in that example are fixtures.

The repository and source identity are not illustrative: MEM 0.2.0 publishes
from `message-easy-mode/mem` and uses source ID `control-plane`.

---

## 16. Acceptance for this contract slice

03A is complete when:

- the contract document exists;
- the JSON Schema exists and parses;
- the example manifest passes the validator;
- critical invalid versions, channels, mutable filenames, unsupported 0.2.0 host
  claims, bad image references and bad digests are rejected;
- an asset-directory verification mode can check real files when producers begin
  implementing the contract;
- no installer/CLI/Migrate production behaviour has been changed yet.

That leaves implementation cleanly separated into 03B, 03C, 03D and 03E.
