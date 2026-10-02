#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
TEMPLATE="$RELEASE_DIR/templates/github-release-prepare.yml"
MATERIALIZER="$RELEASE_DIR/materialize-github-release-workflow.sh"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-github-release-tests.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT

pass() { printf 'PASS: %s\n' "$1"; PASSED=$((PASSED + 1)); }
fail_test() {
  printf 'FAIL: %s\n' "$1" >&2
  [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2
  FAILED=$((FAILED + 1))
}
expect_reject() {
  local name="$1" needle="$2"
  shift 2
  local output status
  set +e
  output="$("$@" 2>&1)"
  status=$?
  set -e
  if [[ $status -ne 0 && "$output" == *"$needle"* ]]; then
    pass "$name"
  else
    fail_test "$name" "$output"
  fi
}

[[ -f "$TEMPLATE" ]] || { echo "Missing workflow template: $TEMPLATE" >&2; exit 2; }
[[ -x "$MATERIALIZER" ]] || { echo "Missing workflow materializer: $MATERIALIZER" >&2; exit 2; }

REPO="$TMP_ROOT/repo"
mkdir -p "$REPO"
if "$MATERIALIZER" --write --repo-root "$REPO" >/dev/null &&
   cmp -s "$TEMPLATE" "$REPO/.github/workflows/release-prepare.yml" &&
   "$MATERIALIZER" --check --repo-root "$REPO" >/dev/null; then
  pass "materializer writes exactly the reviewed workflow and check verifies it"
else
  fail_test "materializer writes exactly the reviewed workflow and check verifies it"
fi

if "$MATERIALIZER" --write --repo-root "$REPO" 2>&1 | grep -Fq 'Already current:'; then
  pass "materializer is idempotent for an identical reviewed workflow"
else
  fail_test "materializer is idempotent for an identical reviewed workflow"
fi

printf '# local drift\n' >> "$REPO/.github/workflows/release-prepare.yml"
expect_reject \
  "materializer refuses to overwrite a different GitHub workflow" \
  "Refusing to overwrite" \
  "$MATERIALIZER" --write --repo-root "$REPO"
expect_reject \
  "materializer check fails closed on workflow drift" \
  "differs from reviewed template" \
  "$MATERIALIZER" --check --repo-root "$REPO"

if grep -Fq 'workflow_dispatch:' "$TEMPLATE" &&
   grep -Fq 'default: 0.2.0-rc.1' "$TEMPLATE" &&
   ! grep -Eq '^  (push|pull_request|release):' "$TEMPLATE"; then
  pass "workflow is manual-only and defaults the first provider proof to 0.2.0-rc.1"
else
  fail_test "workflow is manual-only and defaults the first provider proof to 0.2.0-rc.1"
fi

if grep -Fq 'SOURCE_REPOSITORY: message-easy-mode/mem' "$TEMPLATE" &&
   grep -Fq 'RELEASE_REPOSITORY: message-easy-mode/mem-releases' "$TEMPLATE" &&
   grep -Fq 'IMAGE_REPOSITORY: ghcr.io/message-easy-mode/mem-control-plane' "$TEMPLATE"; then
  pass "workflow separates source, public release, and GHCR image identities"
else
  fail_test "workflow separates source, public release, and GHCR image identities"
fi

if grep -Fq 'persist-credentials: false' "$TEMPLATE" &&
   grep -Fq '[[ "$GITHUB_REPOSITORY" == "$SOURCE_REPOSITORY" ]]' "$TEMPLATE" &&
   grep -Fq 'MEM_RELEASES_TOKEN' "$TEMPLATE" &&
   grep -Fq 'refs/heads/${DEFAULT_BRANCH}' "$TEMPLATE" &&
   grep -Fq 'repos/${SOURCE_REPOSITORY}/commits/${DEFAULT_BRANCH}' "$TEMPLATE" &&
   grep -Fq 'repos/${RELEASE_REPOSITORY}/git/ref/tags/${TAG}' "$TEMPLATE" &&
   grep -Fq 'gh release view' "$TEMPLATE" &&
   grep -Fq 'git status --porcelain --untracked-files=all' "$TEMPLATE"; then
  pass "workflow binds source preparation to mem and release publication checks to mem-releases"
else
  fail_test "workflow binds source preparation to mem and release publication checks to mem-releases"
fi

if grep -Fq 'runs-on: ubuntu-24.04' "$TEMPLATE" &&
   grep -Fq 'contents: read' "$TEMPLATE" &&
   grep -Fq 'packages: write' "$TEMPLATE" &&
   grep -Fq 'id-token: write' "$TEMPLATE" &&
   grep -Fq 'attestations: write' "$TEMPLATE" &&
   grep -Fq 'artifact-metadata: write' "$TEMPLATE"; then
  pass "workflow declares the explicit Ubuntu runner and least source-repository publication/attestation permissions"
else
  fail_test "workflow declares the explicit Ubuntu runner and least source-repository publication/attestation permissions"
fi

EXPECTED_ACTIONS="$(cat <<'ACTIONS'
actions/attest@1e69f48acb82d1966a394da916b4c1698aa569d6
actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1
actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68
actions/setup-node@820762786026740c76f36085b0efc47a31fe5020
actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a
anchore/sbom-action@e22c389904149dbc22b58101806040fa8d37a610
aquasecurity/setup-trivy@81e514348e19b6112ce2a7e3ecbafe19c1e1f567
docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a
docker/login-action@dbcb813823bdd20940b903addbd779551569679f
docker/setup-buildx-action@bb05f3f5519dd87d3ba754cc423b652a5edd6d2c
ACTIONS
)"
ACTUAL_ACTIONS="$(awk '/^[[:space:]]+uses: / {print $2}' "$TEMPLATE" | LC_ALL=C sort -u)"
if [[ "$ACTUAL_ACTIONS" == "$EXPECTED_ACTIONS" ]] &&
   ! grep -Eq '^[[:space:]]+uses:[[:space:]]+[^#[:space:]]+@(v|main|master|latest)([^0-9a-f]|$)' "$TEMPLATE"; then
  pass "every external GitHub Action is pinned to the reviewed full commit SHA"
else
  fail_test "every external GitHub Action is pinned to the reviewed full commit SHA" "$ACTUAL_ACTIONS"
fi

if grep -Fq 'NODE_VERSION: 22.23.2' "$TEMPLATE" &&
   grep -Fq 'DOTNET_SDK_VERSION: 8.0.424' "$TEMPLATE" &&
   grep -Fq 'TRIVY_VERSION: v0.74.0' "$TEMPLATE"; then
  pass "workflow pins the hosted Node, .NET SDK and Trivy tool versions"
else
  fail_test "workflow pins the hosted Node, .NET SDK and Trivy tool versions"
fi

if grep -Fq "resolve_image 'node:22-alpine'" "$TEMPLATE" &&
   grep -Fq "resolve_image 'mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim'" "$TEMPLATE" &&
   grep -Fq "resolve_image 'mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim'" "$TEMPLATE" &&
   grep -Fq "resolve_image 'docker:29-cli'" "$TEMPLATE" &&
   grep -Fq "printf '%s@%s\\n' \"\$image\" \"\$digest\"" "$TEMPLATE"; then
  pass "workflow resolves mutable Dockerfile base defaults to immutable registry digests before the release build"
else
  fail_test "workflow resolves mutable Dockerfile base defaults to immutable registry digests before the release build"
fi

if grep -Fq 'platforms: linux/amd64' "$TEMPLATE" &&
   grep -Fq 'migrate-source=./migrate' "$TEMPLATE" &&
   grep -Fq 'DOCKER_CLI_IMAGE=${{ steps.bases.outputs.docker_cli_image }}' "$TEMPLATE" &&
   grep -Fq 'MEM_PRODUCT_VERSION=${{ steps.preflight.outputs.version }}' "$TEMPLATE" &&
   grep -Fq 'MEM_COMMIT_SHA=${{ steps.preflight.outputs.source_commit }}' "$TEMPLATE" &&
   grep -Fq 'provenance: false' "$TEMPLATE" &&
   grep -Fq 'sbom: false' "$TEMPLATE"; then
  pass "workflow builds the existing Control Plane Docker boundary for exact linux/amd64 release identity"
else
  fail_test "workflow builds the existing Control Plane Docker boundary for exact linux/amd64 release identity"
fi

if grep -Fq './scripts/release/verify-control-plane-image.sh' "$TEMPLATE" &&
   grep -Fq 'subject-name: ghcr.io/message-easy-mode/mem-control-plane' "$TEMPLATE" &&
   grep -Fq 'subject-digest: ${{ steps.push.outputs.digest }}' "$TEMPLATE" &&
   grep -Fq 'push-to-registry: true' "$TEMPLATE"; then
  pass "workflow verifies and attests the exact pushed GHCR image digest"
else
  fail_test "workflow verifies and attests the exact pushed GHCR image digest"
fi

if grep -Fq 'format: spdx-json' "$TEMPLATE" &&
   grep -Fq 'upload-artifact: false' "$TEMPLATE" &&
   grep -Fq 'upload-release-assets: false' "$TEMPLATE" &&
   grep -Fq 'sbom-path: ${{ steps.preflight.outputs.release_root }}/sbom/mem-${{ steps.preflight.outputs.version }}-sbom.spdx.json' "$TEMPLATE"; then
  pass "workflow generates one controlled SPDX JSON SBOM and attests it to the image without surprise release assets"
else
  fail_test "workflow generates one controlled SPDX JSON SBOM and attests it to the image without surprise release assets"
fi

if grep -Fq -- '--ignore-unfixed' "$TEMPLATE" &&
   grep -Fq -- '--severity CRITICAL' "$TEMPLATE" &&
   grep -Fq -- '--exit-code 1' "$TEMPLATE" &&
   grep -Fq 'trivy-vulnerabilities.json' "$TEMPLATE" &&
   grep -Fq 'trivy-licenses.json' "$TEMPLATE"; then
  pass "workflow gates fixable CRITICAL vulnerabilities and retains full vulnerability/license evidence"
else
  fail_test "workflow gates fixable CRITICAL vulnerabilities and retains full vulnerability/license evidence"
fi

if grep -Fq './scripts/release/build-cli-release.sh' "$TEMPLATE" &&
   grep -Fq './scripts/release/build-migrate-release.sh' "$TEMPLATE" &&
   grep -Fq './scripts/release/assemble-product-release.sh' "$TEMPLATE" &&
   grep -Fq -- '--assets-dir "$RELEASE_ROOT/public"' "$TEMPLATE" &&
   grep -Fq 'subject-path: ${{ steps.preflight.outputs.release_root }}/public/*' "$TEMPLATE"; then
  pass "workflow reuses 03C/03D/03E-A producers and attests the validated exact public asset set"
else
  fail_test "workflow reuses 03C/03D/03E-A producers and attests the validated exact public asset set"
fi

if grep -Fq 'GH_TOKEN: ${{ secrets.MEM_RELEASES_TOKEN }}' "$TEMPLATE" &&
   grep -Fq 'gh release create "$TAG"' "$TEMPLATE" &&
   grep -Fq -- '--repo "$RELEASE_REPOSITORY"' "$TEMPLATE" &&
   grep -Fq -- '--target "$RELEASE_DEFAULT_BRANCH"' "$TEMPLATE" &&
   grep -Fq -- '--draft' "$TEMPLATE" &&
   grep -Fq 'STOP: this workflow does not publish the draft.' "$TEMPLATE" &&
   ! grep -Eq -- '--draft[= ]false|gh[[:space:]]+release[[:space:]]+edit.*--draft=false|gh[[:space:]]+release[[:space:]]+edit.*--latest' "$TEMPLATE"; then
  pass "workflow stops at a GitHub Release draft and contains no publication step"
else
  fail_test "workflow stops at a GitHub Release draft and contains no publication step"
fi

printf '\nGitHub release workflow tests: %d passed, %d failed\n' "$PASSED" "$FAILED"
[[ "$FAILED" -eq 0 ]]
