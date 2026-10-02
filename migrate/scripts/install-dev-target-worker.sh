#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

DEFAULT_INSTALL_ROOT="/opt/mem/migrate"
INSTALL_ROOT="$DEFAULT_INSTALL_ROOT"
PAYLOAD_DIR=""
VERSION_FILE=""
BUILD_INFO_FILE=""
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  sudo ./migrate/scripts/install-dev-target-worker.sh \
    --payload <mem-migrate-deploy-repo>/payload

Options:
  --payload <path>       Published MEM Migrate payload directory. Required.
  --version-file <path>  Release VERSION file. Defaults to ../VERSION beside payload.
  --build-info <path>    BUILD-INFO.json. Defaults to ../BUILD-INFO.json beside payload.
  --install-root <path>  Installation root. Default: /opt/mem/migrate.
  --dry-run              Validate and print the installation plan without modifying files.
  -h, --help             Show this help.

Installs the just-published MEM Migrate payload as the Control Plane development
conversion worker. Releases are stored under:

  /opt/mem/migrate/dev-releases/<version>

and activated atomically through:

  /opt/mem/migrate/dev

The Control Plane continues to use /opt/mem/migrate/dev/mem-migrate.
The full self-contained payload is installed so the CLI keeps every runtime
companion file produced by the authoritative publisher.
USAGE
}

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

info() {
  echo "INFO: $*"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --payload)
      [[ $# -ge 2 ]] || fail "--payload requires a path."
      PAYLOAD_DIR="$2"
      shift 2
      ;;
    --version-file)
      [[ $# -ge 2 ]] || fail "--version-file requires a path."
      VERSION_FILE="$2"
      shift 2
      ;;
    --build-info)
      [[ $# -ge 2 ]] || fail "--build-info requires a path."
      BUILD_INFO_FILE="$2"
      shift 2
      ;;
    --install-root)
      [[ $# -ge 2 ]] || fail "--install-root requires a path."
      INSTALL_ROOT="$2"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=true
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

[[ -n "$PAYLOAD_DIR" ]] || fail "--payload is required."

for command_name in \
  realpath \
  python3 \
  mktemp \
  cp \
  mv \
  rm \
  ln \
  grep \
  find \
  date \
  uname \
  age \
  docker; do
  require_command "$command_name"
done

PAYLOAD_DIR="$(realpath -m -- "$PAYLOAD_DIR")"
INSTALL_ROOT="$(realpath -m -- "$INSTALL_ROOT")"

[[ -d "$PAYLOAD_DIR" ]] || fail "Published payload directory does not exist: $PAYLOAD_DIR"
[[ "$INSTALL_ROOT" != "/" ]] || fail "Refusing to use the filesystem root as the installation root."

if [[ -z "$VERSION_FILE" ]]; then
  VERSION_FILE="$(dirname -- "$PAYLOAD_DIR")/VERSION"
fi
if [[ -z "$BUILD_INFO_FILE" ]]; then
  BUILD_INFO_FILE="$(dirname -- "$PAYLOAD_DIR")/BUILD-INFO.json"
fi

VERSION_FILE="$(realpath -m -- "$VERSION_FILE")"
BUILD_INFO_FILE="$(realpath -m -- "$BUILD_INFO_FILE")"

[[ -f "$VERSION_FILE" ]] || fail "Release VERSION file does not exist: $VERSION_FILE"
[[ -f "$BUILD_INFO_FILE" ]] || fail "Release BUILD-INFO.json does not exist: $BUILD_INFO_FILE"

if [[ "$INSTALL_ROOT" == "$DEFAULT_INSTALL_ROOT" && ${EUID:-$(id -u)} -ne 0 ]]; then
  fail "Run this installer with sudo when installing beneath $DEFAULT_INSTALL_ROOT."
fi

ARCHITECTURE="$(uname -m)"
[[ "$ARCHITECTURE" == "x86_64" ]] || fail "Unsupported architecture: $ARCHITECTURE. The development worker payload is linux-x64."

if ! AGE_OUTPUT="$(age --version 2>&1)"; then
  fail "age is installed but unusable: ${AGE_OUTPUT:-no version output}"
fi
AGE_VERSION="$(head -n 1 <<<"$AGE_OUTPUT" | tr -d '\r')"
[[ -n "$AGE_VERSION" ]] || fail "age is installed but did not report a version."

if ! DOCKER_OUTPUT="$(docker version --format '{{.Server.Version}}' 2>&1)"; then
  fail "Docker is installed but the daemon is not reachable: ${DOCKER_OUTPUT:-no version output}"
fi
DOCKER_VERSION="$(head -n 1 <<<"$DOCKER_OUTPUT" | tr -d '\r')"
[[ -n "$DOCKER_VERSION" ]] || fail "Docker is installed but the daemon is not reachable."

[[ -x "$PAYLOAD_DIR/mem-migrate" ]] || fail "Payload is missing executable mem-migrate."
[[ -f "$PAYLOAD_DIR/libe_sqlite3.so" ]] || fail "Payload is missing libe_sqlite3.so."

if find "$PAYLOAD_DIR" -mindepth 1 -type l -print -quit | grep -q .; then
  fail "Payload contains symbolic links. Publish a clean regular-file payload."
fi

RELEASE_VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
[[ "$RELEASE_VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] || fail "VERSION contains unsupported characters."

readarray -t BUILD_FIELDS < <(python3 - "$BUILD_INFO_FILE" "$RELEASE_VERSION" <<'PY'
import json
import sys

path, expected_release = sys.argv[1:]
with open(path, "r", encoding="utf-8") as handle:
    document = json.load(handle)

release = document.get("releaseVersion")
runtime = document.get("runtime")
source_commit = document.get("sourceCommit")
source_dirty = bool(document.get("sourceTreeDirty", False))

if release != expected_release:
    raise SystemExit("BUILD-INFO.json releaseVersion does not match VERSION")
if runtime != "linux-x64":
    raise SystemExit(f"BUILD-INFO.json runtime is {runtime!r}, expected 'linux-x64'")
if not isinstance(source_commit, str) or not source_commit.strip():
    raise SystemExit("BUILD-INFO.json sourceCommit is missing")

print(source_commit.strip())
print("true" if source_dirty else "false")
PY
) || fail "BUILD-INFO.json is invalid or does not match this release."

SOURCE_COMMIT="${BUILD_FIELDS[0]:-}"
SOURCE_DIRTY="${BUILD_FIELDS[1]:-false}"

PRODUCT_VERSION="$($PAYLOAD_DIR/mem-migrate version 2>&1 | head -n 1 | tr -d '\r')"
[[ -n "$PRODUCT_VERSION" ]] || fail "Published mem-migrate did not report a version."

WORKER_HELP="$($PAYLOAD_DIR/mem-migrate worker convert --help 2>&1)" || fail "Published mem-migrate conversion-worker command failed validation."
grep -q "mem-migrate worker convert --request" <<<"$WORKER_HELP" || fail "Published worker help does not expose the request-file command."
grep -q "mem-conversion-worker-request version 2" <<<"$WORKER_HELP" || fail "Published worker does not advertise conversion request schema version 2."

RELEASES_ROOT="$INSTALL_ROOT/dev-releases"
RELEASE_DIR="$RELEASES_ROOT/$RELEASE_VERSION"
ACTIVE_PATH="$INSTALL_ROOT/dev"
ACTIVE_TARGET=""
CURRENT_RELEASE=""

if [[ -L "$ACTIVE_PATH" ]]; then
  ACTIVE_TARGET="$(readlink -f -- "$ACTIVE_PATH" 2>/dev/null || true)"
  if [[ -n "$ACTIVE_TARGET" && -f "$ACTIVE_TARGET/VERSION" ]]; then
    CURRENT_RELEASE="$(tr -d '[:space:]' < "$ACTIVE_TARGET/VERSION")"
  fi
elif [[ -d "$ACTIVE_PATH" ]]; then
  CURRENT_RELEASE="legacy-unversioned"
fi

cat <<SUMMARY

MEM Migrate Control Plane development worker
  Mode:                   $([[ "$DRY_RUN" == true ]] && echo dry-run || echo apply)
  Payload:                $PAYLOAD_DIR
  Release:                $RELEASE_VERSION
  Product version:        $PRODUCT_VERSION
  Source commit:          $SOURCE_COMMIT
  Source tree dirty:      $SOURCE_DIRTY
  age version:            $AGE_VERSION
  Docker server:          $DOCKER_VERSION
  Install root:           $INSTALL_ROOT
  Active worker path:     $ACTIVE_PATH/mem-migrate
  Current release:        ${CURRENT_RELEASE:-not installed}
SUMMARY

if [[ "$DRY_RUN" == true ]]; then
  echo
  echo "Dry-run complete. No release directories or active worker links were modified."
  echo "Would install the full verified payload into:"
  echo "  $RELEASE_DIR"
  echo "and atomically activate it through:"
  echo "  $ACTIVE_PATH"
  exit 0
fi

mkdir -p -- "$INSTALL_ROOT" "$RELEASES_ROOT"

if [[ -d "$RELEASE_DIR" ]]; then
  info "Release directory already exists; validating it before activation."
  [[ -x "$RELEASE_DIR/mem-migrate" ]] || fail "Existing release is missing mem-migrate: $RELEASE_DIR"
  [[ -f "$RELEASE_DIR/libe_sqlite3.so" ]] || fail "Existing release is missing libe_sqlite3.so: $RELEASE_DIR"
  [[ "$(tr -d '[:space:]' < "$RELEASE_DIR/VERSION")" == "$RELEASE_VERSION" ]] || fail "Existing release VERSION is inconsistent."
  "$RELEASE_DIR/mem-migrate" worker convert --help 2>&1 | grep -q "mem-conversion-worker-request version 2" || fail "Existing release worker contract is invalid."
else
  STAGE_DIR="$(mktemp -d "$INSTALL_ROOT/.dev-stage.XXXXXX")"
  cleanup_stage() {
    rm -rf -- "${STAGE_DIR:-}"
  }
  trap cleanup_stage EXIT INT TERM

  cp -a -- "$PAYLOAD_DIR/." "$STAGE_DIR/"
  cp -a -- "$VERSION_FILE" "$STAGE_DIR/VERSION"
  cp -a -- "$BUILD_INFO_FILE" "$STAGE_DIR/BUILD-INFO.json"

  python3 - \
    "$STAGE_DIR/TARGET-WORKER-INFO.json" \
    "$RELEASE_VERSION" \
    "$PRODUCT_VERSION" \
    "$SOURCE_COMMIT" \
    "$SOURCE_DIRTY" \
    "$AGE_VERSION" \
    "$DOCKER_VERSION" <<'PY'
import datetime
import json
import sys

(
    output_path,
    release_version,
    product_version,
    source_commit,
    source_dirty,
    age_version,
    docker_version,
) = sys.argv[1:]

document = {
    "schemaVersion": 1,
    "role": "control-plane-development-conversion-worker",
    "releaseVersion": release_version,
    "productVersion": product_version,
    "sourceCommit": source_commit,
    "sourceTreeDirty": source_dirty == "true",
    "workerRequestSchemaVersion": 2,
    "ageVersionAtInstall": age_version,
    "dockerServerVersionAtInstall": docker_version,
    "installedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2)
    handle.write("\n")
PY

  chmod 0755 "$STAGE_DIR/mem-migrate"
  [[ -x "$STAGE_DIR/mem-migrate" ]] || fail "Staged release is missing executable mem-migrate."
  [[ -f "$STAGE_DIR/libe_sqlite3.so" ]] || fail "Staged release is missing libe_sqlite3.so."
  "$STAGE_DIR/mem-migrate" worker convert --help 2>&1 | grep -q "mem-conversion-worker-request version 2" || fail "Staged worker contract validation failed."

  if [[ ${EUID:-$(id -u)} -eq 0 ]]; then
    chown -R root:root "$STAGE_DIR"
  fi

  mv -- "$STAGE_DIR" "$RELEASE_DIR"
  STAGE_DIR=""
  trap - EXIT INT TERM
fi

CURRENT_LINK_TARGET=""
LEGACY_BACKUP=""
if [[ -L "$ACTIVE_PATH" ]]; then
  CURRENT_LINK_TARGET="$(readlink -- "$ACTIVE_PATH")"
elif [[ -e "$ACTIVE_PATH" ]]; then
  LEGACY_BACKUP="$INSTALL_ROOT/dev-legacy-$(date -u +%Y%m%dT%H%M%SZ)-$$"
  info "Preserving the legacy unversioned development worker at $LEGACY_BACKUP"
  mv -- "$ACTIVE_PATH" "$LEGACY_BACKUP"
fi

TEMP_LINK="$INSTALL_ROOT/.dev-link.$$"
rm -f -- "$TEMP_LINK"
ln -s -- "dev-releases/$RELEASE_VERSION" "$TEMP_LINK"

restore_active() {
  rm -f -- "$TEMP_LINK" "$ACTIVE_PATH"
  if [[ -n "$CURRENT_LINK_TARGET" ]]; then
    ln -s -- "$CURRENT_LINK_TARGET" "$ACTIVE_PATH"
  elif [[ -n "$LEGACY_BACKUP" && -e "$LEGACY_BACKUP" ]]; then
    mv -- "$LEGACY_BACKUP" "$ACTIVE_PATH"
  fi
}

if ! mv -Tf -- "$TEMP_LINK" "$ACTIVE_PATH"; then
  restore_active
  fail "Could not activate the development worker."
fi

if ! "$ACTIVE_PATH/mem-migrate" worker convert --help 2>&1 | grep -q "mem-conversion-worker-request version 2"; then
  restore_active
  fail "Activated worker failed its conversion-contract validation; the previous worker was restored."
fi

ACTIVE_PRODUCT_VERSION="$($ACTIVE_PATH/mem-migrate version 2>&1 | head -n 1 | tr -d '\r')"
[[ "$ACTIVE_PRODUCT_VERSION" == "$PRODUCT_VERSION" ]] || {
  restore_active
  fail "Activated worker version does not match the published payload; the previous worker was restored."
}

cat <<SUCCESS

Control Plane development worker installed successfully.
  Release:                $RELEASE_VERSION
  Product version:        $ACTIVE_PRODUCT_VERSION
  Source commit:          $SOURCE_COMMIT
  Active worker:          $ACTIVE_PATH/mem-migrate
  Worker request schema:  2
SUCCESS

if [[ -n "$LEGACY_BACKUP" ]]; then
  echo "  Previous legacy worker: $LEGACY_BACKUP"
fi

echo
echo "Restart the Control Plane development API before migration testing."
echo "Then verify:"
echo "  sudo $ACTIVE_PATH/mem-migrate version"
echo "  sudo $ACTIVE_PATH/mem-migrate worker convert --help"
