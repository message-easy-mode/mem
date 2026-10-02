# MEM 0.2.0 Supply-Chain Release Boundary

**Slice:** `RELEASE-020-SUPPLYCHAIN-03E`  
**03E-A:** exact local product release assembly  
**03E-B:** hosted GitHub/GHCR build, SBOM and trusted publication

## Canonical public identities

```text
GitHub organization  message-easy-mode
Source repo          https://github.com/message-easy-mode/mem
Release repo         https://github.com/message-easy-mode/mem-releases
Source ID            control-plane
Control Plane image  ghcr.io/message-easy-mode/mem-control-plane
Release tag          v<version>
```

For 0.2.0, `release.json` must use those exact identities. Public release
files are acquired from `mem-releases`; the source commit remains bound to `mem`,
while the Control Plane image remains `ghcr.io/message-easy-mode/mem-control-plane`.

## What 03E-A proves

`assemble-product-release.sh` is deliberately local and provider-independent
apart from the fixed public identities in the manifest/bootstrap. It proves that
one clean committed source tree can produce one internally consistent public
asset set.

The assembler refuses:

- a dirty source worktree;
- a selected source commit different from HEAD;
- a mutable or non-digest Control Plane image reference;
- CLI producer evidence that is dirty, non-release-eligible, from another
  commit, or inconsistent with the supplied binary;
- Migrate producer evidence that is dirty, non-release-eligible, from another
  commit, or inconsistent with its component files;
- a non-SPDX-2.3 JSON SBOM;
- an installer that does not contain the exact standalone CLI bytes; or
- a public asset directory containing undeclared extra files.

The installer archive is rebuilt during product assembly using the existing 03B
builder and the exact standalone CLI artifact. The assembler then re-verifies
the fixed archive root, inner checksums, source commit, platform, Control Plane
image identity and embedded CLI hash.

`release.generatedAtUtc` is derived from the source commit timestamp. It is not a
wall-clock assembly time. For identical release inputs this keeps the product
manifest reproducible.

## What 03E-A does not claim

03E-A does not:

- build or push the Control Plane container image;
- invent a container digest;
- generate the final SBOM;
- scan vulnerabilities;
- publish a GitHub Release;
- create GitHub artifact attestations; or
- prove that repository release immutability is enabled.

Those are 03E-B responsibilities because they require the real production
Docker build context and live GitHub/GHCR provider state.

The existing repository slice boundary intentionally does not permit arbitrary
`.github/` writes. 03E-A leaves that safety boundary unchanged. When 03E-B is
built from the real workflow/Docker baseline, workflow materialization must be
handled as an explicit reviewed step rather than broadening repository slices to
arbitrary GitHub configuration.

## Hosted publication policy for 03E-B

The hosted workflow should:

1. check out the exact release source commit;
2. run the appropriate release/test gates;
3. build and push `ghcr.io/message-easy-mode/mem-control-plane:<version>`;
4. consume the registry-backed SHA-256 digest returned by the image build;
5. generate the SPDX-2.3 JSON SBOM and required vulnerability/dependency evidence;
6. build release-eligible CLI and Migrate outputs from the same commit;
7. run `assemble-product-release.sh` with the real image digest and SBOM;
8. attest the container image and executable/downloadable release artifacts;
9. create the GitHub Release as a draft;
10. upload the complete validated public asset set;
11. publish the draft only after repository/organization release immutability is
    enabled; and
12. verify the immutable release and local assets after publication.

The workflow should use GitHub's current artifact-attestation mechanism rather
than a home-grown signature format. A MEM-owned detached signature can be added
later as an additional trust layer, not as a replacement for checksums,
provenance and immutable release verification.

## Consumer verification boundary

`SHA256SUMS` and the hashes in `release.json` establish byte consistency. They
do not independently authenticate the publisher when all files are downloaded
from the same compromised origin.

The versioned public bootstrap therefore explains the stronger GitHub
verification path:

```text
gh release verify v0.2.0 -R message-easy-mode/mem-releases
gh release verify-asset v0.2.0 <artifact> -R message-easy-mode/mem-releases
gh attestation verify <artifact> -R message-easy-mode/mem
```

The convenience installer itself remains usable on a clean Ubuntu host without
requiring GitHub CLI; it verifies the exact versioned release manifest,
`SHA256SUMS`, installer archive, fixed-root extraction contract and inner
checksums before handing control to the mature bundled installer.


## 03E-B release-image build contract

The production `installer/Dockerfile` remains the single Control Plane image
build definition. 03E-B does not create a second image path. The hosted workflow
passes release identity into the existing stages so one image carries:

- the exact MEM product version in the Control Plane API assembly;
- the same exact version in embedded `mem-migrate` and `mem-migrate-web`;
- the exact release version in the compiled Migrate Source Assistant UI;
- source commit and release version in runtime environment values; and
- OCI `source`, `version`, `revision` and `created` labels; and
- the native migration runtime dependencies `age`, `age-keygen`, and Docker CLI.

The Dockerfile keeps mutable base-image defaults for ordinary development. The
release workflow resolves those defaults, including the Docker CLI source image,
to immutable registry digests first and passes the resulting
`tag@sha256:digest` references as build arguments. The resolved base identities
are retained as internal workflow evidence. The final runtime copies only the
Docker client binary from the Docker CLI source image; it does not ship or run a
second Docker daemon.

`verify-control-plane-image.sh` then pulls the final exact
`repository:version@sha256:digest` reference and fails closed if the registry
identity, platform, OCI labels, runtime MEM identity, native migration tools,
embedded Migrate versions, or compiled Source Assistant identity disagree.

### Image reproducibility boundary

`CONTROL-PLANE-IMAGE-REPRODUCIBILITY-OBS-01` remains open as a claim boundary.
Base images are digest-resolved and source/version inputs are explicit, but the
runtime image still performs Debian `apt-get update` plus package installation
without snapshot/pinned package versions. 0.2.0 therefore records and attests
the produced image and its SBOM; it does **not** claim that a later rebuild is
bit-for-bit identical merely from the same Git commit.

## 03E-B hosted preparation workflow

The reviewed workflow template is:

```text
scripts/release/templates/github-release-prepare.yml
```

and the only materialized GitHub Actions destination is:

```text
.github/workflows/release-prepare.yml
```

The repository slice boundary is intentionally not broadened to `.github/**`.
Use the narrow materializer, review the resulting diff, and commit that exact
file:

```bash
./scripts/release/materialize-github-release-workflow.sh --write
./scripts/release/materialize-github-release-workflow.sh --check
git diff -- .github/workflows/release-prepare.yml
```

The workflow is manual-only and must run from the current default-branch tip. It
refuses an existing Git tag, GitHub Release, or exact GHCR version tag. The
first provider-level proof should be `0.2.0-rc.1`; stable `0.2.0` is reserved for
the final run after the outstanding product QA gates are complete.

The workflow performs, in order:

1. exact source/default-branch/clean-tree preflight;
2. exact Node, .NET SDK and Trivy tool setup with all Actions pinned to full
   commit SHAs;
3. GHCR authentication and existing-tag refusal;
4. immutable base-image digest resolution and evidence capture;
5. one `linux/amd64` build/push through `installer/Dockerfile` using the existing
   `migrate-source=./migrate` BuildKit context;
6. exact registry image verification;
7. GitHub image provenance attestation;
8. SPDX-2.3 JSON SBOM generation for the final runtime image and SBOM attestation
   bound to that image digest;
9. vulnerability and license evidence capture, with an automated fail gate for
   fixable CRITICAL image vulnerabilities;
10. release-eligible CLI and Migrate builds from the same source commit;
11. exact 03E-A product release assembly and checksum/schema validation;
12. GitHub attestation of the ten public release files;
13. upload of internal base-image/build/security evidence as a workflow artifact;
   and
14. creation of a GitHub Release **draft** containing exactly the ten validated
   public files.

The workflow stops there. It does not publish the draft and does not alter the
`latest` release marker.

If a hosted run fails after pushing its GHCR version tag or creating a draft,
the workflow deliberately refuses to overwrite those identities on retry. A
maintainer must inspect the partial provider state and either remove the failed
candidate deliberately or use the next prerelease version (for example
`0.2.0-rc.2`). This is safer than silently replacing an already-observed release
identity.

## Security scan boundary

The automated 0.2.0 image gate fails when Trivy finds a fixable CRITICAL
vulnerability (`--ignore-unfixed --severity CRITICAL --exit-code 1`). A complete
JSON vulnerability report is retained regardless, and license findings are
retained for review rather than converted into a simplistic automatic
allow/deny policy.

This is a release-engineering baseline, not a claim that every dependency has
received an independent security audit or penetration test.

## SBOM scope

`SUPPLYCHAIN-SBOM-SCOPE-OBS-01` remains an explicit scope note. The public
`mem-<version>-sbom.spdx.json` is generated from the final Control Plane runtime
image. It therefore covers the runtime OS/packages, Control Plane payload and
embedded Migrate runtime as seen in that image.

It does **not** by itself prove complete composition coverage for the separately
published standalone MEM CLI binary or the shell/bootstrap artifacts. The file
must be described publicly as the **Control Plane runtime image SBOM** unless a
later slice adds/merges component SBOMs and proves the broader product-SBOM
claim.

## Draft-to-immutable publication boundary

Before dispatching the stable 0.2.0 preparation run, enable GitHub release
immutability at the repository or organization level. The intended operator
sequence is:

```text
complete functional QA -> enable release immutability -> prepare stable draft
-> review exact assets/evidence -> publish draft -> verify release/assets/attestations
```

After publication, verify the final release in `mem-releases`. File/image
attestations produced by the trusted source workflow remain bound to the
`mem` source repository identity:

```bash
gh release verify v0.2.0 -R message-easy-mode/mem-releases
gh release verify-asset v0.2.0 ./mem-cli-0.2.0-linux-x64 \
  -R message-easy-mode/mem-releases
gh attestation verify ./mem-cli-0.2.0-linux-x64 \
  -R message-easy-mode/mem
gh attestation verify \
  oci://ghcr.io/message-easy-mode/mem-control-plane:0.2.0 \
  -R message-easy-mode/mem
```

Do not describe `SHA256SUMS` as publisher authentication. Checksums prove byte
consistency; GitHub release/artifact attestations and immutable provider state
supply the stronger provenance/integrity evidence.
