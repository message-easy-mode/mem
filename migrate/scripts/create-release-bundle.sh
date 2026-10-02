#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/create-release-bundle.sh \
    --deployment-root <path> \
    --output-dir <path> \
    [--channel dev|stable|prerelease] \
    [--runtime linux-x64] \
    [--installer-source <path>]

Creates a transportable MEM Migrate release bundle containing:
  install-mem-migrate.sh
  release.json
  mem-migrate-<version>-linux-x64.tar.gz
  mem-migrate-<version>-linux-x64.tar.gz.sha256

The archive contains one fixed root named mem-migrate-release and reuses the
validated deployment-repository payload and atomic installation helpers.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

validate_public_build_info() {
  local build_info_path="$1"

  python3 - "$build_info_path" <<'PY'
import json
import re
import sys
from urllib.parse import urlsplit

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as handle:
    document = json.load(handle)

if not isinstance(document, dict):
    raise SystemExit("public BUILD-INFO.json must contain a JSON object")

allowed_fields = {
    "schemaVersion",
    "product",
    "productVersion",
    "sourceAssistantVersion",
    "releaseVersion",
    "publishedAtUtc",
    "runtime",
    "configuration",
    "selfContained",
    "sourceCommit",
    "sourceBranch",
    "sourceTreeDirty",
    "releaseChannel",
    "releaseEligible",
}
unexpected_fields = sorted(set(document) - allowed_fields)
if unexpected_fields:
    raise SystemExit(
        "public BUILD-INFO.json contains unsupported field(s): "
        + ", ".join(unexpected_fields)
    )

for required_field in (
    "releaseVersion",
    "publishedAtUtc",
    "runtime",
    "configuration",
    "sourceCommit",
    "sourceTreeDirty",
    "releaseChannel",
    "releaseEligible",
):
    if required_field not in document:
        raise SystemExit(
            f"public BUILD-INFO.json is missing required field: {required_field}"
        )

for key in ("releaseVersion", "publishedAtUtc", "runtime", "configuration",
            "sourceCommit", "releaseChannel"):
    value = document.get(key)
    if not isinstance(value, str) or not value:
        raise SystemExit(
            f"public BUILD-INFO.json field {key} must be a non-empty string"
        )

for key in ("sourceTreeDirty", "releaseEligible"):
    if not isinstance(document.get(key), bool):
        raise SystemExit(
            f"public BUILD-INFO.json field {key} must be a boolean"
        )

def iter_strings(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for nested in value.values():
            yield from iter_strings(nested)
    elif isinstance(value, list):
        for nested in value:
            yield from iter_strings(nested)

for value in iter_strings(document):
    lowered = value.lower()

    if "/home/" in lowered or "/users/" in lowered:
        raise SystemExit(
            "public BUILD-INFO.json contains a producer-local filesystem path"
        )

    if "scm.private.invalid" in lowered:
        raise SystemExit(
            "public BUILD-INFO.json contains a private/internal SCM endpoint"
        )

    if re.search(r"(?i)\b(?:ssh|git|file)://", value):
        raise SystemExit(
            "public BUILD-INFO.json contains an SCM endpoint"
        )

    if re.search(r"(?i)\bgit@[^:\s]+:", value):
        raise SystemExit(
            "public BUILD-INFO.json contains an SSH SCM endpoint"
        )

    parsed = urlsplit(value)
    if parsed.scheme in {"http", "https"} and (
        parsed.username is not None or parsed.password is not None
    ):
        raise SystemExit(
            "public BUILD-INFO.json contains credential-bearing URL material"
        )
PY
}

DEPLOYMENT_ROOT=""
OUTPUT_DIR=""
CHANNEL="dev"
RUNTIME="linux-x64"
INSTALLER_SOURCE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --deployment-root)
      [[ $# -ge 2 ]] || fail "--deployment-root requires a path."
      DEPLOYMENT_ROOT="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --channel)
      [[ $# -ge 2 ]] || fail "--channel requires dev, stable, or prerelease."
      CHANNEL="$2"
      shift 2
      ;;
    --runtime)
      [[ $# -ge 2 ]] || fail "--runtime requires a value."
      RUNTIME="$2"
      shift 2
      ;;
    --installer-source)
      [[ $# -ge 2 ]] || fail "--installer-source requires a path."
      INSTALLER_SOURCE="$2"
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

for command_name in realpath mktemp rm mkdir cp chmod tar sha256sum find sort awk grep python3; do
  require_command "$command_name"
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MIGRATE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
[[ -n "$INSTALLER_SOURCE" ]] || INSTALLER_SOURCE="$MIGRATE_ROOT/bootstrap/install.sh"

[[ -n "$DEPLOYMENT_ROOT" ]] || fail "--deployment-root is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."
[[ "$CHANNEL" == "dev" || "$CHANNEL" == "stable" || "$CHANNEL" == "prerelease" ]] || fail "Unsupported release channel: $CHANNEL"
[[ "$RUNTIME" == "linux-x64" ]] || fail "MM-BOOT-01B supports the linux-x64 release runtime only."

DEPLOYMENT_ROOT="$(realpath -m -- "$DEPLOYMENT_ROOT")"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
INSTALLER_SOURCE="$(realpath -m -- "$INSTALLER_SOURCE")"

[[ -d "$DEPLOYMENT_ROOT" ]] || fail "Deployment root does not exist: $DEPLOYMENT_ROOT"
[[ -f "$INSTALLER_SOURCE" ]] || fail "Bootstrap installer source does not exist: $INSTALLER_SOURCE"

case "$OUTPUT_DIR/" in
  "$DEPLOYMENT_ROOT/payload/"*|"$DEPLOYMENT_ROOT/scripts/"*)
    fail "Output directory must not be placed inside deployment payload or script content."
    ;;
esac

REQUIRED_PATHS=(
  VERSION
  BUILD-INFO.json
  SHA256SUMS
  payload
  scripts/install-current.sh
  install.sh
  run-web.sh
  stop-web.sh
  status.sh
)

for required_path in "${REQUIRED_PATHS[@]}"; do
  [[ -e "$DEPLOYMENT_ROOT/$required_path" ]] || fail "Deployment content is missing: $required_path"
done

VERSION="$(tr -d '[:space:]' < "$DEPLOYMENT_ROOT/VERSION")"
[[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] || fail "Deployment VERSION contains unsupported characters."

BUILD_RELEASE_VERSION="$(python3 - "$DEPLOYMENT_ROOT/BUILD-INFO.json" <<'PY'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    document = json.load(handle)

value = document.get("releaseVersion")
if not isinstance(value, str) or not value:
    raise SystemExit("BUILD-INFO.json does not contain a valid releaseVersion")
print(value)
PY
)" || fail "BUILD-INFO.json is invalid."

[[ "$BUILD_RELEASE_VERSION" == "$VERSION" ]] || fail "BUILD-INFO.json releaseVersion does not match VERSION."

PUBLISHED_AT_UTC="$(python3 - "$DEPLOYMENT_ROOT/BUILD-INFO.json" <<'PY'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    document = json.load(handle)

value = document.get("publishedAtUtc")
if not isinstance(value, str) or not value:
    raise SystemExit("BUILD-INFO.json does not contain a valid publishedAtUtc")
print(value)
PY
)" || fail "BUILD-INFO.json is invalid."

validate_public_build_info "$DEPLOYMENT_ROOT/BUILD-INFO.json" ||
  fail "BUILD-INFO.json violates the public metadata contract."

(
  cd "$DEPLOYMENT_ROOT"
  sha256sum -c SHA256SUMS >/dev/null
) || fail "Deployment content failed its inner SHA256SUMS verification."

TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-release-bundle.XXXXXX")"
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  rm -rf -- "$TEMP_ROOT"
  exit "$status"
}
trap cleanup EXIT INT TERM

FIXED_ROOT="$TEMP_ROOT/mem-migrate-release"
VERIFY_ROOT="$TEMP_ROOT/verify"
ARCHIVE_NAME="mem-migrate-${VERSION}-${RUNTIME}.tar.gz"
ARCHIVE_PATH="$TEMP_ROOT/$ARCHIVE_NAME"

mkdir -p -- "$FIXED_ROOT/scripts" "$VERIFY_ROOT"
cp -a -- "$DEPLOYMENT_ROOT/VERSION" "$FIXED_ROOT/VERSION"
cp -a -- "$DEPLOYMENT_ROOT/BUILD-INFO.json" "$FIXED_ROOT/BUILD-INFO.json"
cp -a -- "$DEPLOYMENT_ROOT/SHA256SUMS" "$FIXED_ROOT/SHA256SUMS"
cp -a -- "$DEPLOYMENT_ROOT/payload" "$FIXED_ROOT/payload"
cp -a -- "$DEPLOYMENT_ROOT/scripts/install-current.sh" "$FIXED_ROOT/scripts/install-current.sh"
cp -a -- "$DEPLOYMENT_ROOT/install.sh" "$FIXED_ROOT/install.sh"
cp -a -- "$DEPLOYMENT_ROOT/run-web.sh" "$FIXED_ROOT/run-web.sh"
cp -a -- "$DEPLOYMENT_ROOT/stop-web.sh" "$FIXED_ROOT/stop-web.sh"
cp -a -- "$DEPLOYMENT_ROOT/status.sh" "$FIXED_ROOT/status.sh"

chmod 0755 \
  "$FIXED_ROOT/install.sh" \
  "$FIXED_ROOT/run-web.sh" \
  "$FIXED_ROOT/stop-web.sh" \
  "$FIXED_ROOT/status.sh" \
  "$FIXED_ROOT/scripts/install-current.sh"

# Build a single-root archive. Numeric ownership prevents developer workstation
# identities from becoming part of the release transport contract.
if [[ -n "${SOURCE_DATE_EPOCH:-}" ]]; then
  [[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "SOURCE_DATE_EPOCH must be an integer Unix timestamp."
  tar \
    --sort=name \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    --mtime="@${SOURCE_DATE_EPOCH}" \
    --clamp-mtime \
    --use-compress-program="gzip -n" \
    -C "$TEMP_ROOT" \
    -cf "$ARCHIVE_PATH" \
    mem-migrate-release
else
  tar \
    --sort=name \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    -C "$TEMP_ROOT" \
    -czf "$ARCHIVE_PATH" \
    mem-migrate-release
fi

ARCHIVE_SHA256="$(sha256sum "$ARCHIVE_PATH" | awk '{print $1}')"
[[ "$ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]] || fail "Could not calculate the release archive SHA-256."

# Independently extract and verify the generated transport before publishing it.
tar -xzf "$ARCHIVE_PATH" -C "$VERIFY_ROOT" --no-same-owner --no-same-permissions
[[ -d "$VERIFY_ROOT/mem-migrate-release" ]] || fail "Generated archive does not contain the fixed release root."
validate_public_build_info "$VERIFY_ROOT/mem-migrate-release/BUILD-INFO.json" ||
  fail "Generated archive BUILD-INFO.json violates the public metadata contract."
(
  cd "$VERIFY_ROOT/mem-migrate-release"
  sha256sum -c SHA256SUMS >/dev/null
) || fail "Generated archive failed its inner SHA256SUMS verification."

rm -rf -- "$OUTPUT_DIR"
mkdir -p -- "$OUTPUT_DIR"
cp -a -- "$ARCHIVE_PATH" "$OUTPUT_DIR/$ARCHIVE_NAME"
printf '%s  %s\n' "$ARCHIVE_SHA256" "$ARCHIVE_NAME" > "$OUTPUT_DIR/$ARCHIVE_NAME.sha256"
cp -a -- "$INSTALLER_SOURCE" "$OUTPUT_DIR/install-mem-migrate.sh"
chmod 0755 "$OUTPUT_DIR/install-mem-migrate.sh"

python3 - \
  "$OUTPUT_DIR/release.json" \
  "$CHANNEL" \
  "$VERSION" \
  "$RUNTIME" \
  "$ARCHIVE_NAME" \
  "$ARCHIVE_SHA256" \
  "$PUBLISHED_AT_UTC" <<'PY'
import json
import sys

(
    output_path,
    channel,
    release_version,
    runtime,
    archive_file_name,
    archive_sha256,
    published_at_utc,
) = sys.argv[1:]

manifest = {
    "schemaVersion": 1,
    "channel": channel,
    "releaseVersion": release_version,
    "runtime": runtime,
    "archiveFileName": archive_file_name,
    "archiveSha256": archive_sha256,
    "publishedAtUtc": published_at_utc,
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
PY

python3 - "$OUTPUT_DIR/release.json" "$ARCHIVE_NAME" "$ARCHIVE_SHA256" "$VERSION" "$RUNTIME" "$CHANNEL" <<'PY'
import json
import sys

manifest_path, archive_name, archive_sha256, version, runtime, channel = sys.argv[1:]
with open(manifest_path, "r", encoding="utf-8") as handle:
    manifest = json.load(handle)

expected = {
    "schemaVersion": 1,
    "channel": channel,
    "releaseVersion": version,
    "runtime": runtime,
    "archiveFileName": archive_name,
    "archiveSha256": archive_sha256,
}
for key, value in expected.items():
    if manifest.get(key) != value:
        raise SystemExit(f"release.json mismatch for {key}")
PY

(
  cd "$OUTPUT_DIR"
  sha256sum -c "$ARCHIVE_NAME.sha256" >/dev/null
)

printf 'Release bundle created:\n'
printf '  Channel:          %s\n' "$CHANNEL"
printf '  Release:          %s\n' "$VERSION"
printf '  Runtime:          %s\n' "$RUNTIME"
printf '  Archive:          %s\n' "$OUTPUT_DIR/$ARCHIVE_NAME"
printf '  Archive SHA-256:  %s\n' "$ARCHIVE_SHA256"
printf '  Manifest:         %s\n' "$OUTPUT_DIR/release.json"
printf '  Bootstrap:        %s\n' "$OUTPUT_DIR/install-mem-migrate.sh"
