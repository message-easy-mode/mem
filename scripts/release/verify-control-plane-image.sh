#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

IMAGE_REF=""
VERSION=""
SOURCE_COMMIT=""
CREATED=""
EXPECTED_REPOSITORY="ghcr.io/message-easy-mode/mem-control-plane"
EXPECTED_SOURCE="https://github.com/message-easy-mode/mem"

usage() {
  cat <<'USAGE'
Verify the published MEM Control Plane release image before product assembly.

Usage:
  ./scripts/release/verify-control-plane-image.sh \
    --image <repository>:<version>@sha256:<digest> \
    [--expected-repository <repository>] \
    --version <semver> \
    --source-commit <40-64 hex> \
    --created <RFC3339 UTC timestamp>

The verifier pulls the exact digest-bearing image and requires:
  * the expected repository, exact version tag and sha256 digest;
  * linux/amd64 platform;
  * matching OCI source/version/revision/created labels;
  * matching MEM_PRODUCT_VERSION and MEM_COMMIT_SHA runtime environment;
  * native migration dependencies age, age-keygen and Docker CLI executing successfully;
  * embedded mem-migrate and mem-migrate-web reporting the exact release version;
  * embedded MEM Migrate VERSION and BUILD-INFO.json matching version/source commit;
  * compiled Source Assistant web assets carrying the requested version; and
  * the known development fallback 0.2.0-alpha.1 absent from a different release.

--expected-repository defaults to the canonical public repository:
  ghcr.io/message-easy-mode/mem-control-plane

Use a different repository only for explicit private staging/local-registry
verification. OCI source metadata remains canonical and is still verified.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image)
      [[ $# -ge 2 ]] || fail "--image requires a value."
      IMAGE_REF="$2"
      shift 2
      ;;
    --expected-repository)
      [[ $# -ge 2 ]] || fail "--expected-repository requires a value."
      EXPECTED_REPOSITORY="$2"
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || fail "--version requires a value."
      VERSION="$2"
      shift 2
      ;;
    --source-commit)
      [[ $# -ge 2 ]] || fail "--source-commit requires a value."
      SOURCE_COMMIT="$2"
      shift 2
      ;;
    --created)
      [[ $# -ge 2 ]] || fail "--created requires a value."
      CREATED="$2"
      shift 2
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

[[ -n "$IMAGE_REF" ]] || fail "--image is required."
[[ -n "$EXPECTED_REPOSITORY" ]] || fail "--expected-repository may not be empty."
[[ "$EXPECTED_REPOSITORY" != *"://"* ]] || fail "--expected-repository must not contain a URL scheme."
[[ "$EXPECTED_REPOSITORY" != *"@"* ]] || fail "--expected-repository must not contain a digest."
[[ "$EXPECTED_REPOSITORY" != *[[:space:]]* ]] || fail "--expected-repository must not contain whitespace."
[[ "$EXPECTED_REPOSITORY" == */* ]] || fail "--expected-repository must include registry and repository path."
[[ "$EXPECTED_REPOSITORY" != */ ]] || fail "--expected-repository must not end with /."
[[ -n "$VERSION" ]] || fail "--version is required."
[[ -n "$SOURCE_COMMIT" ]] || fail "--source-commit is required."
[[ -n "$CREATED" ]] || fail "--created is required."

[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] || \
  fail "Invalid release version: $VERSION"
case "$VERSION" in
  *latest*|*stable*|*current*|*dev*)
    fail "Release version may not contain a mutable channel/development label: $VERSION"
    ;;
esac
[[ "$SOURCE_COMMIT" =~ ^[0-9a-f]{40,64}$ ]] || fail "Invalid source commit: $SOURCE_COMMIT"
[[ "$CREATED" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] || \
  fail "--created must be an RFC3339 UTC timestamp such as 2026-09-05T00:00:00Z."

EXPECTED_PREFIX="${EXPECTED_REPOSITORY}:${VERSION}@sha256:"
[[ "$IMAGE_REF" == "$EXPECTED_PREFIX"* ]] || \
  fail "Image must use the expected repository with exact version tag and digest: ${EXPECTED_PREFIX}<64 hex>"
DIGEST="${IMAGE_REF##*@}"
[[ "$DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Image digest must be immutable SHA-256."

for command_name in docker python3 grep mktemp rm tr; do
  require_command "$command_name"
done

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-image-verify.XXXXXX")"
CONTAINER_ID=""
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ -n "$CONTAINER_ID" ]]; then
    docker rm -f "$CONTAINER_ID" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$TMP_ROOT"
  exit "$status"
}
trap cleanup EXIT INT TERM

echo "[release] Pulling exact Control Plane image: $IMAGE_REF"
docker pull "$IMAGE_REF" >/dev/null

docker image inspect "$IMAGE_REF" > "$TMP_ROOT/image-inspect.json"
python3 - \
  "$TMP_ROOT/image-inspect.json" \
  "$EXPECTED_REPOSITORY" \
  "$DIGEST" \
  "$VERSION" \
  "$SOURCE_COMMIT" \
  "$CREATED" \
  "$EXPECTED_SOURCE" <<'PY'
import json
import sys

path, repository, digest, version, commit, created, source = sys.argv[1:]
doc = json.load(open(path, "r", encoding="utf-8"))
if not isinstance(doc, list) or len(doc) != 1 or not isinstance(doc[0], dict):
    raise SystemExit("ERROR: docker image inspect did not return exactly one image object.")
image = doc[0]

expected_repo_digest = f"{repository}@{digest}"
repo_digests = image.get("RepoDigests") or []
if expected_repo_digest not in repo_digests:
    raise SystemExit(f"ERROR: image RepoDigests does not contain {expected_repo_digest}")

if image.get("Os") != "linux" or image.get("Architecture") != "amd64":
    raise SystemExit(
        f"ERROR: image platform must be linux/amd64, got {image.get('Os')}/{image.get('Architecture')}"
    )

config = image.get("Config") or {}
labels = config.get("Labels") or {}
expected_labels = {
    "org.opencontainers.image.source": source,
    "org.opencontainers.image.version": version,
    "org.opencontainers.image.revision": commit,
    "org.opencontainers.image.created": created,
}
for key, expected in expected_labels.items():
    actual = labels.get(key)
    if actual != expected:
        raise SystemExit(f"ERROR: image label {key} expected {expected!r}, got {actual!r}")

env = {}
for item in config.get("Env") or []:
    if "=" in item:
        key, value = item.split("=", 1)
        env[key] = value
expected_env = {
    "MEM_PRODUCT_VERSION": version,
    "MEM_COMMIT_SHA": commit,
}
for key, expected in expected_env.items():
    actual = env.get(key)
    if actual != expected:
        raise SystemExit(f"ERROR: image environment {key} expected {expected!r}, got {actual!r}")
PY

AGE_VERSION="$(docker run --rm --entrypoint age "$IMAGE_REF" --version 2>&1)" || \
  fail "Embedded age --version failed."
[[ -n "$AGE_VERSION" ]] || fail "Embedded age --version returned empty output."

AGE_KEYGEN_VERSION="$(docker run --rm --entrypoint age-keygen "$IMAGE_REF" --version 2>&1)" || \
  fail "Embedded age-keygen --version failed."
[[ -n "$AGE_KEYGEN_VERSION" ]] || fail "Embedded age-keygen --version returned empty output."

DOCKER_CLI_VERSION="$(docker run --rm --entrypoint docker "$IMAGE_REF" --version 2>&1)" || \
  fail "Embedded Docker CLI --version failed."
[[ "$DOCKER_CLI_VERSION" == Docker\ version\ * ]] || \
  fail "Embedded Docker CLI returned unexpected version output: $DOCKER_CLI_VERSION"

MIGRATE_VERSION="$(docker run --rm --entrypoint /opt/mem/migrate/current/mem-migrate "$IMAGE_REF" version 2>&1)" || \
  fail "Embedded mem-migrate version command failed."
[[ "$MIGRATE_VERSION" == "$VERSION" ]] || \
  fail "Embedded mem-migrate reports '$MIGRATE_VERSION', expected '$VERSION'."

MIGRATE_WEB_VERSION="$(docker run --rm --entrypoint /opt/mem/migrate/current/mem-migrate-web "$IMAGE_REF" --version 2>&1)" || \
  fail "Embedded mem-migrate-web --version failed."
[[ "$MIGRATE_WEB_VERSION" == "$VERSION" ]] || \
  fail "Embedded mem-migrate-web reports '$MIGRATE_WEB_VERSION', expected '$VERSION'."

CONTAINER_ID="$(docker create "$IMAGE_REF")"
[[ -n "$CONTAINER_ID" ]] || fail "Could not create inspection container."
docker cp "$CONTAINER_ID:/opt/mem/migrate/current/VERSION" "$TMP_ROOT/migrate-VERSION" >/dev/null || \
  fail "Embedded MEM Migrate VERSION is missing."
docker cp "$CONTAINER_ID:/opt/mem/migrate/current/BUILD-INFO.json" "$TMP_ROOT/migrate-BUILD-INFO.json" >/dev/null || \
  fail "Embedded MEM Migrate BUILD-INFO.json is missing."
docker cp "$CONTAINER_ID:/opt/mem/migrate/current/wwwroot" "$TMP_ROOT/wwwroot" >/dev/null

docker rm -f "$CONTAINER_ID" >/dev/null
CONTAINER_ID=""

EMBEDDED_MIGRATE_VERSION="$(tr -d '[:space:]' < "$TMP_ROOT/migrate-VERSION")"
[[ "$EMBEDDED_MIGRATE_VERSION" == "$VERSION" ]] || \
  fail "Embedded MEM Migrate VERSION reports '$EMBEDDED_MIGRATE_VERSION', expected '$VERSION'."

python3 - "$TMP_ROOT/migrate-BUILD-INFO.json" "$VERSION" "$SOURCE_COMMIT" <<'PY'
import json
import sys

path, version, commit = sys.argv[1:]
with open(path, "r", encoding="utf-8") as handle:
    document = json.load(handle)

if document.get("releaseVersion") != version:
    raise SystemExit(
        f"ERROR: embedded MEM Migrate BUILD-INFO.json releaseVersion expected {version!r}, got {document.get('releaseVersion')!r}"
    )
if document.get("sourceCommit") != commit:
    raise SystemExit(
        f"ERROR: embedded MEM Migrate BUILD-INFO.json sourceCommit expected {commit!r}, got {document.get('sourceCommit')!r}"
    )
PY

[[ -f "$TMP_ROOT/wwwroot/index.html" ]] || fail "Embedded Source Assistant wwwroot is incomplete."
grep -RIql -- "$VERSION" "$TMP_ROOT/wwwroot" || \
  fail "Compiled Source Assistant assets do not contain release identity '$VERSION'."

if [[ "$VERSION" != "0.2.0-alpha.1" ]] && \
   grep -RIql -- '0.2.0-alpha.1' "$TMP_ROOT/wwwroot"; then
  fail "Compiled Source Assistant assets still expose development identity 0.2.0-alpha.1."
fi

printf 'PASS: verified Control Plane image %s\n' "$IMAGE_REF"
