#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/release/build-migrate-release.sh \
    --version <semver> \
    --output-dir <path> \
    [--migrate-root <path>] \
    [--allow-dirty] \
    [--component-release-dir <path>]

Builds the public MEM Migrate assets required by the unified MEM release:

  install-mem-migrate-<version>.sh
  mem-migrate-<version>-release.json
  mem-migrate-<version>-linux-x64.tar.gz
  mem-migrate-<version>-linux-x64.tar.gz.sha256

Normal mode creates a temporary deployment repository and invokes MEM Migrate's
existing source publication/release machinery with an exact product version.

--component-release-dir is a test/development override that consumes an already
prepared component release directory. It never produces releaseEligible=true.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

VERSION=""
OUTPUT_DIR=""
MIGRATE_ROOT=""
ALLOW_DIRTY=false
COMPONENT_RELEASE_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      [[ $# -ge 2 ]] || fail "--version requires a value."
      VERSION="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --migrate-root)
      [[ $# -ge 2 ]] || fail "--migrate-root requires a path."
      MIGRATE_ROOT="$2"
      shift 2
      ;;
    --allow-dirty)
      ALLOW_DIRTY=true
      shift
      ;;
    --component-release-dir)
      [[ $# -ge 2 ]] || fail "--component-release-dir requires a path."
      COMPONENT_RELEASE_DIR="$2"
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

[[ -n "$VERSION" ]] || fail "--version is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."
[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "--version must be an ordinary semantic version or prerelease version."

if [[ "$VERSION" == *-* ]]; then
  CHANNEL="prerelease"
else
  CHANNEL="stable"
fi

for command_name in git realpath mktemp rm mkdir cp chmod sha256sum awk python3 cmp; do
  require_command "$command_name"
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
[[ -n "$MIGRATE_ROOT" ]] || MIGRATE_ROOT="$REPO_ROOT/migrate"
MIGRATE_ROOT="$(realpath -m -- "$MIGRATE_ROOT")"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"

[[ -d "$MIGRATE_ROOT" ]] || fail "MEM Migrate source root does not exist: $MIGRATE_ROOT"
[[ "$OUTPUT_DIR" != "/" ]] || fail "Refusing to use filesystem root as output."

PUBLISH_SCRIPT="$MIGRATE_ROOT/scripts/publish-to-deploy-repo.sh"
[[ -x "$PUBLISH_SCRIPT" ]] || fail "Migrate publication script is missing or not executable: $PUBLISH_SCRIPT"

SOURCE_REPO_ROOT="$(git -C "$MIGRATE_ROOT" rev-parse --show-toplevel 2>/dev/null)" ||
  fail "MEM Migrate source must be inside a Git repository."
SOURCE_COMMIT="$(git -C "$SOURCE_REPO_ROOT" rev-parse HEAD)"
SOURCE_DIRTY=false
if [[ -n "$(git -C "$SOURCE_REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  SOURCE_DIRTY=true
fi

RELEASE_ELIGIBLE=true
if [[ "$SOURCE_DIRTY" == true || "$ALLOW_DIRTY" == true || -n "$COMPONENT_RELEASE_DIR" ]]; then
  RELEASE_ELIGIBLE=false
fi

if [[ "$SOURCE_DIRTY" == true && "$ALLOW_DIRTY" != true && -z "$COMPONENT_RELEASE_DIR" ]]; then
  fail "Repository worktree is dirty. Commit/review changes before a release-eligible Migrate build, or use --allow-dirty for development proof only."
fi

SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-$(git -C "$SOURCE_REPO_ROOT" show -s --format=%ct HEAD)}"
[[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "SOURCE_DATE_EPOCH must be an integer Unix timestamp."

TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-public-release.XXXXXX")"
cleanup() {
  local status=$?
  trap - EXIT INT TERM
  rm -rf -- "$TEMP_ROOT"
  exit "$status"
}
trap cleanup EXIT INT TERM

if [[ -n "$COMPONENT_RELEASE_DIR" ]]; then
  COMPONENT_DIR="$(realpath -m -- "$COMPONENT_RELEASE_DIR")"
  [[ -d "$COMPONENT_DIR" ]] || fail "Component release directory does not exist: $COMPONENT_DIR"
else
  COMPONENT_DEPLOY_REPO="$TEMP_ROOT/deploy"
  mkdir -p -- "$COMPONENT_DEPLOY_REPO"
  git -C "$COMPONENT_DEPLOY_REPO" init -q
  git -C "$COMPONENT_DEPLOY_REPO" branch -M main

  DIRTY_ARGS=()
  if [[ "$ALLOW_DIRTY" == true ]]; then
    DIRTY_ARGS=(--allow-dirty-release)
  fi

  SOURCE_DATE_EPOCH="$SOURCE_DATE_EPOCH" "$PUBLISH_SCRIPT" \
    --deploy-repo "$COMPONENT_DEPLOY_REPO" \
    --runtime linux-x64 \
    --configuration Release \
    --release-version "$VERSION" \
    --release-channel "$CHANNEL" \
    "${DIRTY_ARGS[@]}"

  COMPONENT_DIR="$COMPONENT_DEPLOY_REPO/release-bundle"
fi

COMPONENT_MANIFEST="$COMPONENT_DIR/release.json"
COMPONENT_BOOTSTRAP="$COMPONENT_DIR/install-mem-migrate.sh"
ARCHIVE_NAME="mem-migrate-${VERSION}-linux-x64.tar.gz"
COMPONENT_ARCHIVE="$COMPONENT_DIR/$ARCHIVE_NAME"
COMPONENT_CHECKSUM="$COMPONENT_ARCHIVE.sha256"

for required in \
  "$COMPONENT_MANIFEST" \
  "$COMPONENT_BOOTSTRAP"; do
  [[ -f "$required" ]] || fail "Prepared Migrate component release is missing: $required"
done

python3 - "$COMPONENT_MANIFEST" "$VERSION" "$CHANNEL" "$ARCHIVE_NAME" <<'PY'
import json
import re
import sys

manifest_path, version, channel, archive_name = sys.argv[1:]
with open(manifest_path, "r", encoding="utf-8") as handle:
    document = json.load(handle)

expected = {
    "schemaVersion": 1,
    "channel": channel,
    "releaseVersion": version,
    "runtime": "linux-x64",
    "archiveFileName": archive_name,
}
for key, value in expected.items():
    if document.get(key) != value:
        raise SystemExit(f"ERROR: component release manifest mismatch for {key}: expected {value!r}, got {document.get(key)!r}")

sha = document.get("archiveSha256")
if not isinstance(sha, str) or re.fullmatch(r"[0-9a-f]{64}", sha) is None:
    raise SystemExit("ERROR: component release manifest has invalid archiveSha256")
PY

for required in \
  "$COMPONENT_ARCHIVE" \
  "$COMPONENT_CHECKSUM"; do
  [[ -f "$required" ]] || fail "Prepared Migrate component release is missing: $required"
done

ARCHIVE_SHA="$(sha256sum "$COMPONENT_ARCHIVE" | awk '{print $1}')"
SIDECAR_SHA="$(awk 'NR == 1 { print $1 }' "$COMPONENT_CHECKSUM")"
SIDECAR_NAME="$(awk 'NR == 1 { print $2 }' "$COMPONENT_CHECKSUM")"
SIDECAR_NAME="${SIDECAR_NAME#\*}"

[[ "$SIDECAR_NAME" == "$ARCHIVE_NAME" ]] || fail "Migrate archive checksum sidecar names a different file."
[[ "$SIDECAR_SHA" == "$ARCHIVE_SHA" ]] || fail "Migrate archive checksum sidecar does not match the archive."

MANIFEST_ARCHIVE_SHA="$(python3 - "$COMPONENT_MANIFEST" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    print(json.load(handle)["archiveSha256"])
PY
)"
[[ "$MANIFEST_ARCHIVE_SHA" == "$ARCHIVE_SHA" ]] || fail "Migrate component manifest does not match archive SHA-256."

FINAL_BOOTSTRAP="install-mem-migrate-${VERSION}.sh"
FINAL_MANIFEST="mem-migrate-${VERSION}-release.json"
FINAL_ARCHIVE="$ARCHIVE_NAME"
FINAL_CHECKSUM="${ARCHIVE_NAME}.sha256"
BUILD_INFO="mem-migrate-${VERSION}-linux-x64.build-info.json"

STAGING="$TEMP_ROOT/output"
mkdir -p -- "$STAGING"

cp -a -- "$COMPONENT_BOOTSTRAP" "$STAGING/$FINAL_BOOTSTRAP"
cp -a -- "$COMPONENT_MANIFEST" "$STAGING/$FINAL_MANIFEST"
cp -a -- "$COMPONENT_ARCHIVE" "$STAGING/$FINAL_ARCHIVE"
cp -a -- "$COMPONENT_CHECKSUM" "$STAGING/$FINAL_CHECKSUM"
chmod 0755 "$STAGING/$FINAL_BOOTSTRAP"

python3 - \
  "$STAGING/$BUILD_INFO" \
  "$VERSION" \
  "$CHANNEL" \
  "$SOURCE_COMMIT" \
  "$SOURCE_DATE_EPOCH" \
  "$SOURCE_DIRTY" \
  "$RELEASE_ELIGIBLE" \
  "$STAGING/$FINAL_BOOTSTRAP" \
  "$STAGING/$FINAL_MANIFEST" \
  "$STAGING/$FINAL_ARCHIVE" \
  "$STAGING/$FINAL_CHECKSUM" <<'PY'
import hashlib
import json
import os
import sys
from datetime import datetime, timezone

(
    output_path,
    version,
    channel,
    source_commit,
    source_date_epoch,
    source_dirty,
    release_eligible,
    bootstrap_path,
    manifest_path,
    archive_path,
    checksum_path,
) = sys.argv[1:]

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def file_record(path, artifact_id, kind):
    return {
        "id": artifact_id,
        "kind": kind,
        "fileName": os.path.basename(path),
        "sha256": digest(path),
        "sizeBytes": os.path.getsize(path),
    }

document = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "component": "MEM Migrate",
        "version": version,
        "channel": channel,
    },
    "source": {
        "commit": source_commit,
        "sourceDateEpoch": int(source_date_epoch),
        "workingTreeDirty": source_dirty.lower() == "true",
    },
    "build": {
        "generatedAtUtc": datetime.fromtimestamp(int(source_date_epoch), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "releaseEligible": release_eligible.lower() == "true",
        "runtime": "linux-x64",
    },
    "artifacts": [
        file_record(bootstrap_path, "migrate-bootstrap", "migrate-bootstrap-script"),
        file_record(manifest_path, "migrate-manifest", "component-release-manifest"),
        file_record(archive_path, "migrate", "migrate-bundle"),
        file_record(checksum_path, "migrate-checksum", "sha256-sidecar"),
    ],
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY

rm -rf -- "$OUTPUT_DIR"
mkdir -p -- "$OUTPUT_DIR"
cp -a -- "$STAGING/." "$OUTPUT_DIR/"

printf '[release] MEM Migrate public release artifacts complete\n'
printf 'Version:          %s\n' "$VERSION"
printf 'Channel:          %s\n' "$CHANNEL"
printf 'Source commit:    %s\n' "$SOURCE_COMMIT"
printf 'Release eligible:%s\n' "$RELEASE_ELIGIBLE"
printf 'Output:           %s\n' "$OUTPUT_DIR"
printf '  %s\n' "$FINAL_BOOTSTRAP"
printf '  %s\n' "$FINAL_MANIFEST"
printf '  %s\n' "$FINAL_ARCHIVE"
printf '  %s\n' "$FINAL_CHECKSUM"
printf '  %s\n' "$BUILD_INFO"
