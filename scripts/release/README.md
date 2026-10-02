# MEM Release Contract Tooling

This directory contains the schema-v1 product release contract for Message Easy Mode.

## Files

```text
RELEASE-CONTRACT.md
release-manifest.schema.json
validate-release-manifest.py
assemble-product-release.sh
verify-control-plane-image.sh
push-staging-control-plane.sh
package-local-qa-release.sh
scp-local-qa-release.sh
materialize-github-release-workflow.sh
templates/install-mem-release.sh.in
templates/github-release-prepare.yml
SUPPLY-CHAIN.md
examples/release-0.2.0.example.json
tests/release-contract-tests.sh
tests/product-release-assembly-tests.sh
tests/control-plane-image-release-tests.sh
tests/staging-local-qa-tests.sh
tests/github-release-workflow-tests.sh
```

## Validate the example

```bash
python3 scripts/release/validate-release-manifest.py \
  scripts/release/examples/release-0.2.0.example.json
```

## Run contract tests

```bash
bash scripts/release/tests/release-contract-tests.sh
```

## Validate a prepared release directory

The directory must contain:

```text
release.json
SHA256SUMS
<every artifact listed by release.json>
```

Run:

```bash
python3 scripts/release/validate-release-manifest.py \
  ./release-output/release.json \
  --assets-dir ./release-output
```

The validator is intentionally standard-library-only so release proof does not
depend on installing a Python package merely to validate the product manifest.

The JSON Schema exists for IDE/CI/third-party validation. The Python validator
also enforces MEM-specific cross-field rules that are awkward to express clearly
in a portable schema, including exact 0.2.0 host support, canonical
`message-easy-mode/mem-releases` publication identity, canonical
`message-easy-mode/mem` source identity, canonical `message-easy-mode/mem-control-plane` image identity, artifact filenames,
and an exact public asset directory with no undeclared files.

## Build the versioned installer bundle

After `RELEASE-020-INSTALLER-03B`, build a release installer from the current
repository with an exact digest-bearing Control Plane image identity:

```bash
./scripts/release/build-installer-bundle.sh \
  --version 0.2.0 \
  --control-plane-image \
    ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest>
```

The builder normally publishes the MEM CLI from `cli/src/Mem.Cli`. Final release
orchestration may instead pass the canonical standalone CLI release binary:

```bash
./scripts/release/build-installer-bundle.sh \
  --version 0.2.0 \
  --control-plane-image \
    ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest> \
  --cli-binary ./release-output/mem-cli-0.2.0-linux-x64
```

Output:

```text
mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz
mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz.sha256
```

The archive has a fixed root and includes:

```text
VERSION
BUILD-INFO.json
SHA256SUMS
install.sh
bootstrap/
bootstrap/release.env
bootstrap/cli/install-host-command.sh
bootstrap/cli/mem
```

`bootstrap/release.env` contains only public, immutable release identity. It is
not a secret store.

Run the installer-bundle regression with:

```bash
bash scripts/release/tests/installer-bundle-tests.sh
```

## MEM Migrate public producer

```bash
./scripts/release/build-migrate-release.sh \
  --version 0.2.0 \
  --output-dir ./artifacts/migrate
```

See `MIGRATE-RELEASE.md` for the component/publication boundary.

## Assemble the exact product release set

03E-A adds the local product assembler. It requires a clean committed worktree,
release-eligible CLI and Migrate producer outputs from that same commit, an exact
digest-bearing Control Plane image reference, and a prebuilt SPDX-2.3 JSON SBOM.

Example:

```bash
./scripts/release/assemble-product-release.sh \
  --version 0.2.0 \
  --control-plane-image \
    ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest> \
  --cli-dir ./artifacts/cli \
  --migrate-dir ./artifacts/migrate \
  --sbom ./artifacts/sbom/mem-0.2.0-sbom.spdx.json \
  --output-dir ./artifacts/product-release
```

The assembler invokes the existing 03B installer builder itself, passing the
canonical standalone CLI artifact. This means the installer cannot silently
contain a different CLI binary from the one published separately.

The public directory is exact:

```text
release.json
SHA256SUMS
install-mem-0.2.0.sh
mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz
mem-cli-0.2.0-linux-x64
install-mem-migrate-0.2.0.sh
mem-migrate-0.2.0-release.json
mem-migrate-0.2.0-linux-x64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz.sha256
mem-0.2.0-sbom.spdx.json
```

Component `*.build-info.json` files remain workflow evidence and are not copied
into this public schema-v1 asset set.

Canonical public acquisition for 0.2.0 is:

```text
GitHub Releases  https://github.com/message-easy-mode/mem-releases
Control Plane    ghcr.io/message-easy-mode/mem-control-plane:<version>@sha256:<digest>
Source identity  https://github.com/message-easy-mode/mem
```

The generated `install-mem-<version>.sh` must download `release.json`,
`SHA256SUMS` and the installer bundle from `mem-releases`, never from the source
repository and never from an internal/private staging registry.

Run:

```bash
bash scripts/release/tests/product-release-assembly-tests.sh
bash scripts/release/tests/release-contract-tests.sh
```

See `SUPPLY-CHAIN.md` for the boundary between local exact assembly (03E-A) and
the hosted GitHub/GHCR publication workflow (03E-B).



## Prepare the hosted GitHub/GHCR workflow

03E-B keeps the repository slice boundary narrow. `apply-slice.sh --target
repository` still does **not** accept arbitrary `.github/` files. The reviewed
workflow lives as source under:

```text
scripts/release/templates/github-release-prepare.yml
```

Materialize exactly one GitHub Actions path only after reviewing the repository
slice:

```bash
./scripts/release/materialize-github-release-workflow.sh --write
./scripts/release/materialize-github-release-workflow.sh --check

git diff -- .github/workflows/release-prepare.yml
```

The helper refuses to overwrite a different existing workflow. Commit the
materialized `.github/workflows/release-prepare.yml` together with the reviewed
03E-B source changes.

The hosted workflow is `workflow_dispatch` only. It runs from the canonical
`message-easy-mode/mem` source repository, builds/pushes the private
GHCR image there, and creates the draft public file release in the separate
`message-easy-mode/mem-releases` repository. Cross-repository draft creation
requires a repository secret named `MEM_RELEASES_TOKEN` with the minimum write
authority needed for release/tag creation in `mem-releases`; the normal source
repository `github.token` is not reused as cross-repository authority.

Its first provider proof should use:

```text
0.2.0-rc.1
```

It builds and pushes one `linux/amd64` Control Plane image, verifies the exact
registry digest, generates an SPDX JSON image SBOM, records vulnerability and
license evidence, builds the release-eligible CLI and Migrate artifacts, runs
`assemble-product-release.sh`, attests the image/SBOM/public files, and creates a
**draft** GitHub Release. It deliberately contains no release-publication step.

Before dispatching the stable `0.2.0` preparation run, the separate CLI and
Migrate functional QA gates must be complete and GitHub release immutability must
already be enabled.

## Verify a pushed Control Plane image

The hosted workflow invokes the same verifier directly:

```bash
./scripts/release/verify-control-plane-image.sh \
  --image \
    ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest> \
  --version 0.2.0 \
  --source-commit <commit> \
  --created <source-commit-utc-timestamp>
```

This verifies the digest-backed `RepoDigest`, `linux/amd64`, OCI source/version/
revision/created labels, MEM runtime version/commit environment, the native
migration dependencies (`age`, `age-keygen`, and Docker CLI), embedded Migrate
executable identities, embedded Migrate `VERSION`/`BUILD-INFO.json` provenance,
and the compiled Source Assistant release identity.

Run the local 03E-B contract tests without Docker/network access:

```bash
bash scripts/release/tests/control-plane-image-release-tests.sh
bash scripts/release/tests/github-release-workflow-tests.sh
bash scripts/release/tests/product-release-assembly-tests.sh
bash scripts/release/tests/release-contract-tests.sh
```

## Private local-registry / copied-asset QA

`RELEASE-020-STAGING-03F` adds a staging-only path for fast disposable Ubuntu
24.04 clean-room installs without logging each VM into GitHub/GHCR.

The public release contract remains unchanged:

```text
GitHub Releases  -> canonical public release files
GHCR             -> canonical public Control Plane image
```

The QA path changes only transport/runtime origin:

```text
Kubuntu/release workstation
    |
    +-- build+push --> registry.vs4.one/message-easy-mode/mem-control-plane
    |
    +-- package exact ten public release files
              |
              +-- SCP --> disposable Ubuntu 24.04 VM
```

### 1. Authenticate to the private registry

When the registry requires authentication, use ordinary Docker authentication
outside the release scripts:

```bash
docker login registry.vs4.one
```

No release script accepts or stores a registry password/token.

### 2. Build and push the QA Control Plane image

From a clean committed source tree:

```bash
./scripts/release/push-staging-control-plane.sh \
  --version 0.2.0-rc.1 \
  --output-env /tmp/mem-0.2.0-rc.1-staging-image.env

source /tmp/mem-0.2.0-rc.1-staging-image.env
```

Default staging repository:

```text
registry.vs4.one/message-easy-mode/mem-control-plane
```

Registry UI:

```text
https://registry-ui.vs4.one/#!/taglist/message-easy-mode/mem-control-plane
```

The script verifies the pushed digest-bearing private image and prints both:

```text
MEM_STAGING_CONTROL_PLANE_IMAGE
MEM_CANONICAL_CONTROL_PLANE_IMAGE
```

They carry the same immutable digest. Use the canonical GHCR-shaped reference
with `assemble-product-release.sh`; use the private-registry reference only for
explicit QA runtime override.

### 3. Assemble the normal exact release set

Continue to use the ordinary product assembler. For example:

```bash
./scripts/release/assemble-product-release.sh \
  --version "$MEM_RELEASE_VERSION" \
  --control-plane-image "$MEM_CANONICAL_CONTROL_PLANE_IMAGE" \
  --cli-dir ./artifacts/cli \
  --migrate-dir ./artifacts/migrate \
  --sbom "./artifacts/sbom/mem-${MEM_RELEASE_VERSION}-sbom.spdx.json" \
  --output-dir ./artifacts/product-release
```

The output remains the canonical exact ten-file schema-v1 public release set.

### 4. Package a local-QA transport kit

```bash
./scripts/release/package-local-qa-release.sh \
  --release-dir ./artifacts/product-release \
  --staging-image "$MEM_STAGING_CONTROL_PLANE_IMAGE" \
  --output-dir ./artifacts/local-qa
```

The resulting `mem-local-qa-<version>.tar.gz` wraps the exact unmodified public
release directory with a QA-only staging image reference and `install-local.sh`.
The packager refuses a staging image whose version or digest differs from
`release.json`.

### 5. Copy to a disposable Ubuntu VM

Dry-run first:

```bash
./scripts/release/scp-local-qa-release.sh \
  --bundle ./artifacts/local-qa/mem-local-qa-0.2.0-rc.1.tar.gz \
  --target ubuntu@mem-test-vm \
  --remote-root /tmp/mem-local-qa \
  --dry-run
```

Then copy:

```bash
./scripts/release/scp-local-qa-release.sh \
  --bundle ./artifacts/local-qa/mem-local-qa-0.2.0-rc.1.tar.gz \
  --target ubuntu@mem-test-vm \
  --remote-root /tmp/mem-local-qa
```

The helper verifies the transport SHA-256, QA wrapper checksums and the copied
public `SHA256SUMS` before publishing the remote directory.

On the VM:

```bash
cd /tmp/mem-local-qa/mem-local-qa-0.2.0-rc.1
sudo ./install-local.sh
```

`install-local.sh` invokes the normal versioned release bootstrap using
`--release-dir` and passes the exact private digest-bearing image through the
existing explicit `--control-plane-image` QA override. Public release metadata
is not rewritten.

Run focused staging tooling tests with:

```bash
bash scripts/release/tests/staging-local-qa-tests.sh
bash scripts/release/tests/control-plane-image-release-tests.sh
bash scripts/release/tests/product-release-assembly-tests.sh
bash scripts/release/tests/release-contract-tests.sh
bash scripts/release/tests/github-release-workflow-tests.sh
```
