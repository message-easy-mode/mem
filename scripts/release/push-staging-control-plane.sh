#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"

VERSION=""
IMAGE_REPOSITORY="registry.vs4.one/message-easy-mode/mem-control-plane"
OUTPUT_ENV=""
REPLACE_EXISTING=false
CANONICAL_REPOSITORY="ghcr.io/message-easy-mode/mem-control-plane"
REGISTRY_UI_URL="https://registry-ui.vs4.one/#!/taglist/message-easy-mode/mem-control-plane"

usage() {
  cat <<'USAGE'
Build, push and verify a private MEM Control Plane QA image.

Usage:
  ./scripts/release/push-staging-control-plane.sh \
    --version 0.2.0-rc.1 \
    [--image-repository registry.vs4.one/message-easy-mode/mem-control-plane] \
    [--output-env /tmp/mem-staging-image.env] \
    [--replace-existing]

The script mirrors the release workflow's image build inputs:
  * clean committed source tree;
  * linux/amd64 only;
  * base images resolved to immutable digests;
  * exact source commit/time metadata;
  * installer/Dockerfile with migrate-source build context;
  * push to the selected private registry repository; and
  * full post-push Control Plane image verification.

It prints two digest-bearing references with the same digest:

  MEM_STAGING_CONTROL_PLANE_IMAGE
      The private registry reference used by QA hosts.

  MEM_CANONICAL_CONTROL_PLANE_IMAGE
      The canonical GHCR-shaped identity supplied to the normal public release
      assembler so the public release contract itself is not rewritten for QA.

The selected Docker registry must already be authenticated when authentication
is required. For the default registry, run `docker login registry.vs4.one`
separately; this script never accepts or stores registry credentials.

By default an existing exact version tag is rejected. --replace-existing is a
staging-only escape hatch for an intentionally disposable QA tag and must never
be used for a public release/provider proof.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

validate_repository() {
  local value="$1"
  [[ -n "$value" ]] || fail "Image repository may not be empty."
  [[ "$value" != *"://"* ]] || fail "Image repository must not contain a URL scheme: $value"
  [[ "$value" != *"@"* ]] || fail "Image repository must not contain a digest: $value"
  [[ "$value" != *[[:space:]]* ]] || fail "Image repository must not contain whitespace: $value"
  [[ "$value" == */* ]] || fail "Image repository must include registry and repository path: $value"
  [[ "$value" != */ ]] || fail "Image repository must not end with '/': $value"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      [[ $# -ge 2 ]] || fail "--version requires a value."
      VERSION="$2"
      shift 2
      ;;
    --image-repository)
      [[ $# -ge 2 ]] || fail "--image-repository requires a value."
      IMAGE_REPOSITORY="$2"
      shift 2
      ;;
    --output-env)
      [[ $# -ge 2 ]] || fail "--output-env requires a path."
      OUTPUT_ENV="$2"
      shift 2
      ;;
    --replace-existing)
      REPLACE_EXISTING=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

[[ -n "$VERSION" ]] || fail "--version is required."
[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] || \
  fail "--version must be an ordinary stable/prerelease semantic version."
case "$VERSION" in
  *latest*|*stable*|*current*|*dev*)
    fail "Version may not contain a mutable channel/development label: $VERSION"
    ;;
esac
validate_repository "$IMAGE_REPOSITORY"
validate_repository "$CANONICAL_REPOSITORY"

for command_name in docker git python3 mktemp rm date dirname mkdir chmod; do
  require_command "$command_name"
done

[[ -f "$REPO_ROOT/installer/Dockerfile" ]] || fail "installer/Dockerfile is missing from repository root."
[[ -d "$REPO_ROOT/migrate" ]] || fail "migrate/ source is missing from repository root."
[[ -x "$SCRIPT_DIR/verify-control-plane-image.sh" ]] || fail "Control Plane image verifier is missing or not executable."
docker buildx version >/dev/null 2>&1 || fail "Docker Buildx is required for the staging image build."

git -C "$REPO_ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1 || \
  fail "Repository root is not a Git worktree: $REPO_ROOT"
SOURCE_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD)"
[[ "$SOURCE_COMMIT" =~ ^[0-9a-f]{40,64}$ ]] || fail "Could not determine exact source commit."
if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  fail "Staging image build requires a clean committed source tree."
fi
SOURCE_DATE_EPOCH="$(git -C "$REPO_ROOT" show -s --format=%ct "$SOURCE_COMMIT")"
[[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "Could not determine source commit timestamp."
BUILD_DATE="$(date -u -d "@$SOURCE_DATE_EPOCH" '+%Y-%m-%dT%H:%M:%SZ')"

resolve_image() {
  local image="$1" manifest digest
  manifest="$(docker buildx imagetools inspect "$image" --format '{{json .Manifest}}')" || \
    fail "Could not inspect base image: $image"
  digest="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("digest", ""))' <<< "$manifest")"
  [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Could not resolve immutable digest for base image: $image"
  printf '%s@%s\n' "$image" "$digest"
}

IMAGE_TAG="${IMAGE_REPOSITORY}:${VERSION}"
if docker buildx imagetools inspect "$IMAGE_TAG" >/dev/null 2>&1; then
  if [[ "$REPLACE_EXISTING" != true ]]; then
    fail "Staging image tag already exists: $IMAGE_TAG. Use a new version or explicit --replace-existing for a disposable QA tag."
  fi
  printf '[staging] WARNING: replacing existing disposable QA tag: %s\n' "$IMAGE_TAG" >&2
fi

printf '[staging] Resolving release base images...\n'
NODE_IMAGE="$(resolve_image 'node:22-alpine')"
DOTNET_SDK_IMAGE="$(resolve_image 'mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim')"
DOTNET_RUNTIME_IMAGE="$(resolve_image 'mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim')"
DOCKER_CLI_IMAGE="$(resolve_image 'docker:29-cli')"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-staging-image.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT INT TERM
METADATA_FILE="$TMP_ROOT/buildx-metadata.json"

printf '[staging] Building and pushing %s from commit %s...\n' "$IMAGE_TAG" "$SOURCE_COMMIT"
docker buildx build \
  --platform linux/amd64 \
  --file "$REPO_ROOT/installer/Dockerfile" \
  --build-context "migrate-source=$REPO_ROOT/migrate" \
  --build-arg "NODE_IMAGE=$NODE_IMAGE" \
  --build-arg "DOTNET_SDK_IMAGE=$DOTNET_SDK_IMAGE" \
  --build-arg "DOTNET_RUNTIME_IMAGE=$DOTNET_RUNTIME_IMAGE" \
  --build-arg "DOCKER_CLI_IMAGE=$DOCKER_CLI_IMAGE" \
  --build-arg "MEM_PRODUCT_VERSION=$VERSION" \
  --build-arg "MEM_COMMIT_SHA=$SOURCE_COMMIT" \
  --build-arg "BUILD_DATE=$BUILD_DATE" \
  --build-arg "SOURCE_DATE_EPOCH=$SOURCE_DATE_EPOCH" \
  --tag "$IMAGE_TAG" \
  --push \
  --provenance=false \
  --sbom=false \
  --metadata-file "$METADATA_FILE" \
  "$REPO_ROOT/installer"

[[ -s "$METADATA_FILE" ]] || fail "Docker Buildx did not emit image metadata."
IMAGE_DIGEST="$(python3 - "$METADATA_FILE" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    doc = json.load(handle)
digest = doc.get("containerimage.digest")
if not digest and isinstance(doc.get("containerimage.descriptor"), dict):
    digest = doc["containerimage.descriptor"].get("digest")
print(digest or "")
PY
)"
[[ "$IMAGE_DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || \
  fail "Docker Buildx metadata did not contain an immutable image digest."

STAGING_IMAGE="${IMAGE_TAG}@${IMAGE_DIGEST}"
CANONICAL_IMAGE="${CANONICAL_REPOSITORY}:${VERSION}@${IMAGE_DIGEST}"

printf '[staging] Verifying exact pushed image...\n'
"$SCRIPT_DIR/verify-control-plane-image.sh" \
  --image "$STAGING_IMAGE" \
  --expected-repository "$IMAGE_REPOSITORY" \
  --version "$VERSION" \
  --source-commit "$SOURCE_COMMIT" \
  --created "$BUILD_DATE"

if [[ -n "$OUTPUT_ENV" ]]; then
  mkdir -p -- "$(dirname -- "$OUTPUT_ENV")"
  {
    printf 'MEM_RELEASE_VERSION=%q\n' "$VERSION"
    printf 'MEM_SOURCE_COMMIT=%q\n' "$SOURCE_COMMIT"
    printf 'MEM_STAGING_CONTROL_PLANE_IMAGE=%q\n' "$STAGING_IMAGE"
    printf 'MEM_CANONICAL_CONTROL_PLANE_IMAGE=%q\n' "$CANONICAL_IMAGE"
  } > "$OUTPUT_ENV"
  chmod 0600 "$OUTPUT_ENV"
  printf '[staging] Wrote image evidence: %s\n' "$OUTPUT_ENV"
fi

printf '\nMEM staging image ready\n'
printf 'Version:          %s\n' "$VERSION"
printf 'Source commit:    %s\n' "$SOURCE_COMMIT"
printf 'Staging image:    %s\n' "$STAGING_IMAGE"
printf 'Canonical image:  %s\n' "$CANONICAL_IMAGE"
if [[ "$IMAGE_REPOSITORY" == "registry.vs4.one/message-easy-mode/mem-control-plane" ]]; then
  printf 'Registry UI:      %s\n' "$REGISTRY_UI_URL"
fi
