# MEM CLI release packaging

**Programme slice:** `RELEASE-020-CLI-03C`  
**Artifact:** `mem-cli-<version>-linux-x64`

## Purpose

This release producer turns the current MEM CLI source into the canonical
standalone self-contained Linux x64 artifact required by the unified MEM release
contract.

It intentionally separates **packaging correctness** from **functional QA
acceptance**.

## Build a development-proof artifact from an uncommitted slice

During slice validation the Git worktree is intentionally dirty. Use:

```bash
cd "$(git rev-parse --show-toplevel)"

./scripts/release/build-cli-release.sh \
  --version 0.2.0 \
  --allow-dirty \
  --output-dir /tmp/mem-cli-release-proof
```

The companion build-info will record:

```json
"workingTreeDirty": true,
"releaseEligible": false
```

Do not publish such an artifact.

## Build a release-eligible artifact

After the release source is committed and the worktree is clean:

```bash
cd "$(git rev-parse --show-toplevel)"

./scripts/release/build-cli-release.sh \
  --version 0.2.0 \
  --output-dir ./artifacts/release
```

The builder derives the exact Git commit and commit timestamp and refuses a dirty
worktree.

Output:

```text
mem-cli-0.2.0-linux-x64
mem-cli-0.2.0-linux-x64.build-info.json
```

The `.build-info.json` file is producer evidence for release orchestration. It is
not one of the schema-v1 public release assets.

The public release asset is:

```text
mem-cli-0.2.0-linux-x64
```

Its SHA-256 and byte size are copied into the product-level `release.json` and
aggregate `SHA256SUMS` during release assembly.

## Version injection

The builder supplies exact release metadata to the .NET SDK:

```text
Version
AssemblyVersion
FileVersion
InformationalVersion
Product
Company
```

The produced CLI must print exactly:

```text
Message Easy Mode CLI
Version: 0.2.0
Command: mem
```

## Installer identity

The final 03B installer build should pass this standalone artifact through:

```bash
./scripts/release/build-installer-bundle.sh \
  ... \
  --cli-binary ./artifacts/release/mem-cli-0.2.0-linux-x64
```

The release regression proves that the CLI embedded in the installer is byte-for-
byte identical to the standalone artifact.

## QA boundary

The current CLI is not yet formally qualified through the same operator QA
campaign used for the Control Plane. It is also known to lag some final API and
Migrate functionality.

Therefore 03C acceptance means:

```text
source builds/tests
+ correct product identity
+ exact release version
+ deterministic producer contract
+ self-contained artifact
+ installer byte identity
```

It does **not** mean:

```text
all final 0.2.0 CLI commands have completed release QA
```

Before public release, perform a dedicated CLI QA cycle against the final
Control Plane API and explicitly close any command/contract gaps.
