#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

PROGRAM_NAME="MEM Migrate bootstrap"
PROGRAM_VERSION="MM-BOOT-01B"
SUPPORTED_OS_ID="ubuntu"
SUPPORTED_OS_VERSION="24.04"
SUPPORTED_ARCH="amd64"
SUPPORTED_RUNTIME="linux-x64"
MIN_INSTALL_FREE_KIB="${MEM_MIGRATE_BOOTSTRAP_MIN_FREE_KIB:-524288}"
DEFAULT_RELEASE_BASE_URL="${MEM_MIGRATE_RELEASE_BASE_URL:-}"

DRY_RUN=false
ASSUME_YES=false
SKIP_AGE_INSTALL=false
TEMP_DIR=""
OS_ID=""
OS_VERSION=""
OS_PRETTY_NAME=""
ARCH=""
ARCH_RAW=""
DISK_AVAILABLE_KIB=""
DOCKER_PATH=""
DOCKER_VERSION=""
AGE_PATH=""
AGE_VERSION=""
CURRENT_RELEASE="not installed"
MISSING_PACKAGES=()

REQUESTED_CHANNEL=""
REQUESTED_VERSION=""
RELEASE_BASE_URL="$DEFAULT_RELEASE_BASE_URL"
RELEASE_MANIFEST_URL=""
LOCAL_BUNDLE=""

MANIFEST_PATH=""
CHECKSUM_PATH=""
ARCHIVE_PATH=""
RELEASE_CHANNEL=""
RELEASE_VERSION=""
RELEASE_RUNTIME=""
RELEASE_ARCHIVE_NAME=""
RELEASE_ARCHIVE_SHA256=""
RELEASE_PUBLISHED_AT_UTC=""
RELEASE_SOURCE=""
RELEASE_ALREADY_CURRENT=false

usage() {
  cat <<'USAGE'
MEM Migrate source-server bootstrap

Usage:
  curl -fsSL https://<release-host>/install-mem-migrate.sh \
    | sudo bash -s -- --release-base-url https://<release-host>

  sudo bash install.sh --bundle /path/to/mem-migrate-<version>-linux-x64.tar.gz

Options:
  --dry-run
      Inspect the host, resolve and validate the release manifest, and report
      planned changes without installing packages or downloading the archive.

  --yes
      Accept non-interactive package and release installation. MM-BOOT-01B does
      not currently prompt, but the option is retained for the one-line path.

  --channel <dev|stable|prerelease>
      Require the resolved release manifest to use the selected channel.

  --version <release-version>
      Require the exact release version. The installer never substitutes a
      different version when this option is supplied.

  --release-base-url <https-url>
      Resolve release.json, the release archive, and its .sha256 file from this
      HTTPS directory.

  --release-manifest-url <https-url>
      Resolve a manifest from an explicit HTTPS URL. Archive files are resolved
      relative to the manifest URL.

  --bundle <local-archive>
      Install an approved local development/offline archive. The published
      mem-migrate-<version>-release.json manifest and <archive>.sha256 must be
      present beside the archive. Legacy component bundles using release.json
      remain accepted.

  --skip-age-install
      Do not install age when it is missing. The bootstrap fails instead.

  -h, --help
      Show this help.

MM-BOOT-01B validates Ubuntu 24.04 amd64, validates Docker without modifying it,
installs or validates age, verifies a versioned MEM Migrate release bundle, and
reuses the existing atomic installer beneath /opt/mem/migrate.
USAGE
}

log_info() {
  printf 'INFO: %s\n' "$*"
}

log_warn() {
  printf 'WARNING: %s\n' "$*" >&2
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" ]]; then
    rm -rf -- "$TEMP_DIR"
  fi
  exit "$status"
}
trap cleanup EXIT INT TERM

is_test_mode() {
  [[ "${MEM_MIGRATE_BOOTSTRAP_TEST_MODE:-0}" == "1" ]]
}

effective_uid() {
  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_EUID_OVERRIDE:-}" ]]; then
    printf '%s\n' "$MEM_MIGRATE_BOOTSTRAP_EUID_OVERRIDE"
    return
  fi

  printf '%s\n' "${EUID:-$(id -u)}"
}

resolve_os_release_file() {
  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_OS_RELEASE_FILE:-}" ]]; then
    printf '%s\n' "$MEM_MIGRATE_BOOTSTRAP_OS_RELEASE_FILE"
    return
  fi

  printf '%s\n' "/etc/os-release"
}

resolve_current_link() {
  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_CURRENT_LINK:-}" ]]; then
    printf '%s\n' "$MEM_MIGRATE_BOOTSTRAP_CURRENT_LINK"
    return
  fi

  printf '%s\n' "/opt/mem/migrate/current"
}

resolve_ca_certificate_file() {
  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_CA_CERT_FILE:-}" ]]; then
    printf '%s\n' "$MEM_MIGRATE_BOOTSTRAP_CA_CERT_FILE"
    return
  fi

  printf '%s\n' "/etc/ssl/certs/ca-certificates.crt"
}

resolve_disk_probe_path() {
  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_DISK_PROBE_PATH:-}" ]]; then
    printf '%s\n' "$MEM_MIGRATE_BOOTSTRAP_DISK_PROBE_PATH"
    return
  fi

  if [[ -d /opt ]]; then
    printf '%s\n' "/opt"
  else
    printf '%s\n' "/"
  fi
}

read_os_release_value() {
  local file="$1"
  local key="$2"
  local value

  value="$(awk -F= -v wanted="$key" '$1 == wanted { sub(/^[^=]*=/, ""); print; exit }' "$file")"
  value="${value%\"}"
  value="${value#\"}"
  printf '%s\n' "$value"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required host command not found: $1"
}

append_unique_package() {
  local candidate="$1"
  local existing

  for existing in "${MISSING_PACKAGES[@]:-}"; do
    if [[ "$existing" == "$candidate" ]]; then
      return
    fi
  done

  MISSING_PACKAGES+=("$candidate")
}

parse_arguments() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --dry-run)
        DRY_RUN=true
        shift
        ;;
      --yes)
        ASSUME_YES=true
        shift
        ;;
      --channel)
        [[ $# -ge 2 ]] || fail "--channel requires dev, stable, or prerelease."
        REQUESTED_CHANNEL="$2"
        shift 2
        ;;
      --version)
        [[ $# -ge 2 ]] || fail "--version requires a release version."
        REQUESTED_VERSION="$2"
        shift 2
        ;;
      --release-base-url)
        [[ $# -ge 2 ]] || fail "--release-base-url requires a URL."
        RELEASE_BASE_URL="$2"
        shift 2
        ;;
      --release-manifest-url)
        [[ $# -ge 2 ]] || fail "--release-manifest-url requires a URL."
        RELEASE_MANIFEST_URL="$2"
        shift 2
        ;;
      --bundle)
        [[ $# -ge 2 ]] || fail "--bundle requires a local archive path."
        LOCAL_BUNDLE="$2"
        shift 2
        ;;
      --skip-age-install)
        SKIP_AGE_INSTALL=true
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

  [[ -z "$REQUESTED_CHANNEL" || "$REQUESTED_CHANNEL" == "dev" || "$REQUESTED_CHANNEL" == "stable" || "$REQUESTED_CHANNEL" == "prerelease" ]] ||
    fail "Unsupported release channel: $REQUESTED_CHANNEL"

  if [[ -n "$REQUESTED_VERSION" ]]; then
    [[ "$REQUESTED_VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] ||
      fail "Requested release version contains unsupported characters."
  fi

  if [[ -n "$LOCAL_BUNDLE" && ( -n "$RELEASE_BASE_URL" || -n "$RELEASE_MANIFEST_URL" ) ]]; then
    fail "--bundle cannot be combined with --release-base-url or --release-manifest-url."
  fi

  if [[ -n "$RELEASE_BASE_URL" && -n "$RELEASE_MANIFEST_URL" ]]; then
    fail "Use either --release-base-url or --release-manifest-url, not both."
  fi

  if [[ -z "$LOCAL_BUNDLE" && -z "$RELEASE_BASE_URL" && -z "$RELEASE_MANIFEST_URL" ]]; then
    fail "No release source is configured. Supply --bundle, --release-base-url, or --release-manifest-url."
  fi
}

create_private_temp_workspace() {
  TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-bootstrap.XXXXXX")"
  chmod 0700 "$TEMP_DIR"
  MANIFEST_PATH="$TEMP_DIR/release.json"
  CHECKSUM_PATH="$TEMP_DIR/archive.sha256"
  ARCHIVE_PATH="$TEMP_DIR/release.tar.gz"
}

validate_privilege_boundary() {
  local uid
  uid="$(effective_uid)"

  if [[ "$DRY_RUN" == true ]]; then
    if [[ "$uid" != "0" ]]; then
      log_warn "Dry-run is not running as root. Host inspection will continue, but a real install must use sudo."
    fi
    return
  fi

  [[ "$uid" == "0" ]] || fail "Run the bootstrap as root: curl ... | sudo bash"
}

validate_host_identity() {
  local kernel_name
  local os_release_file

  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_KERNEL_OVERRIDE:-}" ]]; then
    kernel_name="$MEM_MIGRATE_BOOTSTRAP_KERNEL_OVERRIDE"
  else
    kernel_name="$(uname -s)"
  fi
  [[ "$kernel_name" == "Linux" ]] || fail "Unsupported operating system kernel: $kernel_name. Linux is required."

  os_release_file="$(resolve_os_release_file)"
  [[ -f "$os_release_file" ]] || fail "Operating-system identity file not found: $os_release_file"

  OS_ID="$(read_os_release_value "$os_release_file" ID)"
  OS_VERSION="$(read_os_release_value "$os_release_file" VERSION_ID)"
  OS_PRETTY_NAME="$(read_os_release_value "$os_release_file" PRETTY_NAME)"

  [[ "$OS_ID" == "$SUPPORTED_OS_ID" ]] ||
    fail "Unsupported distribution: ${OS_PRETTY_NAME:-$OS_ID}. MM-BOOT-01 supports Ubuntu only."

  [[ "$OS_VERSION" == "$SUPPORTED_OS_VERSION" ]] ||
    fail "Unsupported Ubuntu version: ${OS_VERSION:-unknown}. MM-BOOT-01 currently supports Ubuntu 24.04 only."

  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_ARCH_OVERRIDE:-}" ]]; then
    ARCH_RAW="$MEM_MIGRATE_BOOTSTRAP_ARCH_OVERRIDE"
  else
    ARCH_RAW="$(uname -m)"
  fi
  case "$ARCH_RAW" in
    x86_64|amd64)
      ARCH="amd64"
      ;;
    *)
      fail "Unsupported architecture: $ARCH_RAW. MM-BOOT-01 currently supports amd64/x86_64 only."
      ;;
  esac
}

validate_install_capacity() {
  local probe_path
  probe_path="$(resolve_disk_probe_path)"

  DISK_AVAILABLE_KIB="$(df -Pk "$probe_path" | awk 'NR == 2 { print $4 }')"
  [[ "$DISK_AVAILABLE_KIB" =~ ^[0-9]+$ ]] ||
    fail "Could not determine available installation space for $probe_path."

  if (( DISK_AVAILABLE_KIB < MIN_INSTALL_FREE_KIB )); then
    fail "Insufficient installation space: ${DISK_AVAILABLE_KIB} KiB available; ${MIN_INSTALL_FREE_KIB} KiB required."
  fi
}

resolve_existing_release() {
  local current_link
  local target
  current_link="$(resolve_current_link)"

  CURRENT_RELEASE="not installed"
  if [[ -e "$current_link" || -L "$current_link" ]]; then
    target="$(readlink -f -- "$current_link" 2>/dev/null || true)"
    if [[ -n "$target" ]]; then
      CURRENT_RELEASE="$(basename -- "$target")"
    else
      CURRENT_RELEASE="present, but current link is unresolved"
    fi
  fi
}

resolve_docker_command() {
  if is_test_mode && [[ "${MEM_MIGRATE_BOOTSTRAP_FORCE_DOCKER_MISSING:-0}" == "1" ]]; then
    return 1
  fi

  command -v docker 2>/dev/null
}

validate_docker() {
  DOCKER_PATH="$(resolve_docker_command || true)"
  [[ -n "$DOCKER_PATH" ]] ||
    fail "Docker is not installed. MEM Migrate will not install or modify Docker on an existing source server."

  local docker_version_output
  if ! docker_version_output="$($DOCKER_PATH --version 2>&1)"; then
    fail "Docker is installed but did not report a usable version."
  fi
  DOCKER_VERSION="${docker_version_output%%$'\n'*}"
  [[ -n "$DOCKER_VERSION" ]] || fail "Docker is installed but did not report a version."

  if ! "$DOCKER_PATH" info >/dev/null 2>&1; then
    fail "Docker is installed but the daemon is not reachable. Resolve Docker access before installing MEM Migrate."
  fi
}

resolve_age_command() {
  local state_file

  if is_test_mode && [[ -n "${MEM_MIGRATE_BOOTSTRAP_TEST_AGE_STATE_FILE:-}" ]]; then
    state_file="$MEM_MIGRATE_BOOTSTRAP_TEST_AGE_STATE_FILE"
    if [[ ! -f "$state_file" || "$(cat "$state_file")" == "missing" ]]; then
      return 1
    fi
  fi

  command -v age 2>/dev/null
}

validate_existing_age() {
  AGE_PATH="$(resolve_age_command || true)"
  if [[ -z "$AGE_PATH" ]]; then
    return 1
  fi

  local age_version_output
  if ! age_version_output="$($AGE_PATH --version 2>&1)"; then
    fail "An age command exists at $AGE_PATH but is not usable. Repair or remove it, then rerun the bootstrap."
  fi
  AGE_VERSION="${age_version_output%%$'\n'*}"
  [[ -n "$AGE_VERSION" ]] ||
    fail "An age command exists at $AGE_PATH but did not report a version. Repair or remove it, then rerun the bootstrap."

  return 0
}

collect_missing_transport_packages() {
  local ca_certificate_file
  MISSING_PACKAGES=()

  command -v curl >/dev/null 2>&1 || append_unique_package "curl"
  command -v tar >/dev/null 2>&1 || append_unique_package "tar"
  command -v gzip >/dev/null 2>&1 || append_unique_package "gzip"
  command -v sha256sum >/dev/null 2>&1 || append_unique_package "coreutils"

  ca_certificate_file="$(resolve_ca_certificate_file)"
  [[ -s "$ca_certificate_file" ]] || append_unique_package "ca-certificates"
}

age_package_is_available() {
  command -v apt-cache >/dev/null 2>&1 || return 1
  apt-cache show age >/dev/null 2>&1
}

install_ubuntu_packages() {
  local needs_age="$1"
  local package

  if (( ${#MISSING_PACKAGES[@]} == 0 )) && [[ "$needs_age" != "true" ]]; then
    return
  fi

  if [[ "$needs_age" == "true" ]]; then
    append_unique_package "age"
  fi

  log_info "Required Ubuntu package plan:"
  for package in "${MISSING_PACKAGES[@]}"; do
    printf '  - %s\n' "$package"
  done

  if [[ "$DRY_RUN" == true ]]; then
    log_info "Dry-run: would refresh apt metadata if required."
    if [[ "$needs_age" == "true" ]] && ! age_package_is_available; then
      log_info "Dry-run: would enable the Ubuntu universe component if age is not available after metadata refresh."
      log_info "Dry-run: may install software-properties-common to perform that repository change."
    fi
    log_info "Dry-run: would install the listed packages non-interactively."
    return
  fi

  require_command apt-get
  require_command apt-cache

  log_info "Refreshing Ubuntu package metadata."
  DEBIAN_FRONTEND=noninteractive apt-get update

  if [[ "$needs_age" == "true" ]] && ! age_package_is_available; then
    log_info "The age package is not currently available; enabling the Ubuntu universe component."

    if ! command -v add-apt-repository >/dev/null 2>&1; then
      log_info "Installing software-properties-common so the Ubuntu universe component can be enabled."
      DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends software-properties-common
      hash -r 2>/dev/null || true
    fi

    require_command add-apt-repository
    add-apt-repository -y universe
    DEBIAN_FRONTEND=noninteractive apt-get update

    age_package_is_available ||
      fail "Ubuntu package metadata still does not provide age after enabling universe."
  fi

  log_info "Installing required Ubuntu packages."
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${MISSING_PACKAGES[@]}"
  hash -r 2>/dev/null || true
}

validate_transport_dependencies() {
  local ca_certificate_file

  command -v curl >/dev/null 2>&1 || fail "curl is unavailable after dependency installation."
  command -v tar >/dev/null 2>&1 || fail "tar is unavailable after dependency installation."
  command -v gzip >/dev/null 2>&1 || fail "gzip is unavailable after dependency installation."
  command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is unavailable after dependency installation."

  ca_certificate_file="$(resolve_ca_certificate_file)"
  [[ -s "$ca_certificate_file" ]] || fail "The system CA certificate bundle is unavailable after dependency installation."
}

ensure_age_and_transport_dependencies() {
  local needs_age=false

  collect_missing_transport_packages

  if validate_existing_age; then
    log_info "Existing age encryption command is usable; it will be preserved."
  else
    needs_age=true
    if [[ "$SKIP_AGE_INSTALL" == true ]]; then
      fail "age encryption is missing and --skip-age-install was supplied. Install age, then rerun the bootstrap."
    fi
  fi

  install_ubuntu_packages "$needs_age"

  if [[ "$DRY_RUN" == true ]]; then
    if [[ "$needs_age" == "true" ]]; then
      AGE_PATH="not installed during dry-run"
      AGE_VERSION="installation planned"
    fi
    return
  fi

  validate_transport_dependencies

  if ! validate_existing_age; then
    fail "age encryption is unavailable after package installation."
  fi
}

validate_https_url() {
  local url="$1"
  local label="$2"

  if [[ "$url" == https://* ]]; then
    return
  fi

  if is_test_mode && [[ "${MEM_MIGRATE_BOOTSTRAP_ALLOW_INSECURE_URL:-0}" == "1" ]] && [[ "$url" == http://* ]]; then
    return
  fi

  fail "$label must use HTTPS."
}

download_file() {
  local url="$1"
  local destination="$2"

  validate_https_url "$url" "Release URL"

  curl \
    --fail \
    --silent \
    --show-error \
    --location \
    --max-redirs 3 \
    --connect-timeout 15 \
    --max-time 300 \
    --proto '=https' \
    --tlsv1.2 \
    --output "$destination" \
    "$url"
}

manifest_field_lines() {
  local file="$1"
  local field="$2"
  grep -E "^[[:space:]]*\"${field}\"[[:space:]]*:" "$file" || true
}

read_manifest_string() {
  local file="$1"
  local field="$2"
  local lines
  local count
  local value

  lines="$(manifest_field_lines "$file" "$field")"
  count="$(printf '%s\n' "$lines" | sed '/^$/d' | wc -l | tr -d '[:space:]')"
  [[ "$count" == "1" ]] || fail "release.json must contain exactly one $field field."

  value="$(printf '%s\n' "$lines" | sed -nE 's/^[[:space:]]*"[^"]+"[[:space:]]*:[[:space:]]*"([^"]*)"[[:space:]]*,?[[:space:]]*$/\1/p')"
  [[ -n "$value" ]] || fail "release.json contains an invalid $field string."
  printf '%s\n' "$value"
}

read_manifest_number() {
  local file="$1"
  local field="$2"
  local lines
  local count
  local value

  lines="$(manifest_field_lines "$file" "$field")"
  count="$(printf '%s\n' "$lines" | sed '/^$/d' | wc -l | tr -d '[:space:]')"
  [[ "$count" == "1" ]] || fail "release.json must contain exactly one $field field."

  value="$(printf '%s\n' "$lines" | sed -nE 's/^[[:space:]]*"[^"]+"[[:space:]]*:[[:space:]]*([0-9]+)[[:space:]]*,?[[:space:]]*$/\1/p')"
  [[ -n "$value" ]] || fail "release.json contains an invalid $field number."
  printf '%s\n' "$value"
}

validate_release_manifest() {
  local schema_version
  local expected_archive_name

  [[ -s "$MANIFEST_PATH" ]] || fail "release.json is missing or empty."
  grep -Eq '^[[:space:]]*\{' "$MANIFEST_PATH" || fail "release.json is not a supported JSON object."
  grep -Eq '\}[[:space:]]*$' "$MANIFEST_PATH" || fail "release.json is incomplete."

  schema_version="$(read_manifest_number "$MANIFEST_PATH" schemaVersion)"
  RELEASE_CHANNEL="$(read_manifest_string "$MANIFEST_PATH" channel)"
  RELEASE_VERSION="$(read_manifest_string "$MANIFEST_PATH" releaseVersion)"
  RELEASE_RUNTIME="$(read_manifest_string "$MANIFEST_PATH" runtime)"
  RELEASE_ARCHIVE_NAME="$(read_manifest_string "$MANIFEST_PATH" archiveFileName)"
  RELEASE_ARCHIVE_SHA256="$(read_manifest_string "$MANIFEST_PATH" archiveSha256)"
  RELEASE_PUBLISHED_AT_UTC="$(read_manifest_string "$MANIFEST_PATH" publishedAtUtc)"

  [[ "$schema_version" == "1" ]] || fail "Unsupported release.json schemaVersion: $schema_version"
  [[ "$RELEASE_CHANNEL" == "dev" || "$RELEASE_CHANNEL" == "stable" || "$RELEASE_CHANNEL" == "prerelease" ]] ||
    fail "Unsupported release channel in release.json: $RELEASE_CHANNEL"
  [[ "$RELEASE_VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] ||
    fail "release.json contains an invalid releaseVersion."
  [[ "$RELEASE_RUNTIME" == "$SUPPORTED_RUNTIME" ]] ||
    fail "Unsupported release runtime: $RELEASE_RUNTIME. Expected $SUPPORTED_RUNTIME."
  [[ "$RELEASE_ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]] ||
    fail "release.json contains an invalid archiveSha256."
  [[ "$RELEASE_PUBLISHED_AT_UTC" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] ||
    fail "release.json contains an invalid publishedAtUtc timestamp."

  expected_archive_name="mem-migrate-${RELEASE_VERSION}-${SUPPORTED_RUNTIME}.tar.gz"
  [[ "$RELEASE_ARCHIVE_NAME" == "$expected_archive_name" ]] ||
    fail "release.json archiveFileName does not match the release version and runtime."
  [[ "$RELEASE_ARCHIVE_NAME" != */* && "$RELEASE_ARCHIVE_NAME" != *'..'* ]] ||
    fail "release.json archiveFileName must be a safe file name."

  if [[ -n "$REQUESTED_CHANNEL" && "$RELEASE_CHANNEL" != "$REQUESTED_CHANNEL" ]]; then
    fail "Resolved release channel is $RELEASE_CHANNEL, not requested channel $REQUESTED_CHANNEL."
  fi

  if [[ -n "$REQUESTED_VERSION" && "$RELEASE_VERSION" != "$REQUESTED_VERSION" ]]; then
    fail "Resolved release is $RELEASE_VERSION, not requested version $REQUESTED_VERSION."
  fi
}

resolve_release_manifest() {
  local bundle_dir
  local bundle_name
  local bundle_version
  local local_manifest
  local manifest_url

  if [[ -n "$LOCAL_BUNDLE" ]]; then
    LOCAL_BUNDLE="$(readlink -f -- "$LOCAL_BUNDLE" 2>/dev/null || true)"
    [[ -n "$LOCAL_BUNDLE" && -f "$LOCAL_BUNDLE" ]] || fail "Local release bundle does not exist."

    bundle_dir="$(dirname -- "$LOCAL_BUNDLE")"
    bundle_name="$(basename -- "$LOCAL_BUNDLE")"
    local_manifest=""

    if [[ "$bundle_name" == mem-migrate-*-$SUPPORTED_RUNTIME.tar.gz ]]; then
      bundle_version="${bundle_name#mem-migrate-}"
      bundle_version="${bundle_version%-$SUPPORTED_RUNTIME.tar.gz}"
      if [[ "$bundle_version" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] &&
         [[ -f "$bundle_dir/mem-migrate-${bundle_version}-release.json" ]]; then
        local_manifest="$bundle_dir/mem-migrate-${bundle_version}-release.json"
      fi
    fi

    if [[ -z "$local_manifest" && -f "$bundle_dir/release.json" ]]; then
      local_manifest="$bundle_dir/release.json"
    fi

    [[ -n "$local_manifest" ]] ||
      fail "Local bundle requires mem-migrate-<version>-release.json beside the archive (legacy release.json is also accepted)."

    cp -- "$local_manifest" "$MANIFEST_PATH"
    cp -- "$LOCAL_BUNDLE.sha256" "$CHECKSUM_PATH" 2>/dev/null ||
      fail "Local bundle requires ${bundle_name}.sha256 beside the archive."

    RELEASE_SOURCE="local bundle: $LOCAL_BUNDLE"
    validate_release_manifest
    [[ "$bundle_name" == "$RELEASE_ARCHIVE_NAME" ]] ||
      fail "Local archive name does not match release.json."
    return
  fi

  if [[ -n "$RELEASE_MANIFEST_URL" ]]; then
    validate_https_url "$RELEASE_MANIFEST_URL" "--release-manifest-url"
    manifest_url="$RELEASE_MANIFEST_URL"
    RELEASE_BASE_URL="${RELEASE_MANIFEST_URL%/*}"
  else
    RELEASE_BASE_URL="${RELEASE_BASE_URL%/}"
    validate_https_url "$RELEASE_BASE_URL" "--release-base-url"
    manifest_url="$RELEASE_BASE_URL/release.json"
  fi

  log_info "Resolving MEM Migrate release manifest."
  download_file "$manifest_url" "$MANIFEST_PATH"
  RELEASE_SOURCE="$manifest_url"
  validate_release_manifest
}

verify_checksum_contract() {
  local checksum_hash
  local checksum_name
  local calculated_hash

  [[ -s "$CHECKSUM_PATH" ]] || fail "Release archive checksum file is missing or empty."
  [[ "$(wc -l < "$CHECKSUM_PATH" | tr -d '[:space:]')" == "1" ]] ||
    fail "Release archive checksum file must contain exactly one entry."

  checksum_hash="$(awk 'NR == 1 { print $1 }' "$CHECKSUM_PATH")"
  checksum_name="$(awk 'NR == 1 { print $2 }' "$CHECKSUM_PATH")"
  checksum_name="${checksum_name#\*}"

  [[ "$checksum_hash" =~ ^[0-9a-f]{64}$ ]] || fail "Release archive checksum file contains an invalid SHA-256."
  [[ "$checksum_name" == "$RELEASE_ARCHIVE_NAME" ]] || fail "Release archive checksum file names a different archive."
  [[ "$checksum_hash" == "$RELEASE_ARCHIVE_SHA256" ]] || fail "release.json and the archive checksum file disagree."

  calculated_hash="$(sha256sum "$ARCHIVE_PATH" | awk '{print $1}')"
  [[ "$calculated_hash" == "$RELEASE_ARCHIVE_SHA256" ]] || fail "Release archive SHA-256 verification failed."
}

download_and_verify_release_archive() {
  if [[ -n "$LOCAL_BUNDLE" ]]; then
    cp -- "$LOCAL_BUNDLE" "$ARCHIVE_PATH"
  else
    log_info "Downloading MEM Migrate release archive."
    download_file "$RELEASE_BASE_URL/$RELEASE_ARCHIVE_NAME" "$ARCHIVE_PATH"
    download_file "$RELEASE_BASE_URL/$RELEASE_ARCHIVE_NAME.sha256" "$CHECKSUM_PATH"
  fi

  verify_checksum_contract
  log_info "Release archive SHA-256 verified."
}

validate_archive_safety() {
  local names_file="$TEMP_DIR/archive-names.txt"
  local verbose_file="$TEMP_DIR/archive-verbose.txt"
  local name
  local type_character

  tar -tzf "$ARCHIVE_PATH" > "$names_file" || fail "Release archive could not be listed."
  [[ -s "$names_file" ]] || fail "Release archive is empty."

  while IFS= read -r name; do
    [[ -n "$name" ]] || fail "Release archive contains an empty path."
    [[ "$name" != /* ]] || fail "Release archive contains an absolute path."
    [[ "$name" == "mem-migrate-release" || "$name" == "mem-migrate-release/" || "$name" == mem-migrate-release/* ]] ||
      fail "Release archive contains content outside mem-migrate-release/."
    case "/$name/" in
      */../*)
        fail "Release archive contains parent-directory traversal."
        ;;
    esac
  done < "$names_file"

  if [[ -n "$(sort "$names_file" | uniq -d)" ]]; then
    fail "Release archive contains duplicate paths."
  fi

  tar -tvzf "$ARCHIVE_PATH" > "$verbose_file" || fail "Release archive metadata could not be inspected."
  while IFS= read -r name; do
    type_character="${name:0:1}"
    case "$type_character" in
      -|d)
        ;;
      *)
        fail "Release archive contains an unsafe non-file entry type: $type_character"
        ;;
    esac
  done < "$verbose_file"
}

extract_and_verify_release() {
  local extraction_root="$TEMP_DIR/extracted"
  local release_root="$extraction_root/mem-migrate-release"
  local archive_version
  local build_release_version
  local checksum_files="$TEMP_DIR/checksum-files.txt"
  local actual_files="$TEMP_DIR/actual-files.txt"

  validate_archive_safety
  mkdir -p -- "$extraction_root"
  tar -xzf "$ARCHIVE_PATH" -C "$extraction_root" --no-same-owner --no-same-permissions

  [[ -d "$release_root" ]] || fail "Release archive did not extract the fixed mem-migrate-release root."
  [[ -f "$release_root/VERSION" ]] || fail "Release archive is missing VERSION."
  [[ -f "$release_root/BUILD-INFO.json" ]] || fail "Release archive is missing BUILD-INFO.json."
  [[ -f "$release_root/SHA256SUMS" ]] || fail "Release archive is missing SHA256SUMS."
  [[ -x "$release_root/install.sh" ]] || fail "Release archive is missing executable install.sh."
  [[ -x "$release_root/scripts/install-current.sh" ]] || fail "Release archive is missing executable scripts/install-current.sh."
  [[ -x "$release_root/payload/mem-migrate" ]] || fail "Release archive is missing executable payload/mem-migrate."
  [[ -x "$release_root/payload/mem-migrate-web" ]] || fail "Release archive is missing executable payload/mem-migrate-web."
  [[ -f "$release_root/payload/libe_sqlite3.so" ]] || fail "Release archive is missing payload/libe_sqlite3.so."
  [[ -f "$release_root/payload/wwwroot/index.html" ]] || fail "Release archive is missing Source Assistant static assets."

  archive_version="$(tr -d '[:space:]' < "$release_root/VERSION")"
  [[ "$archive_version" == "$RELEASE_VERSION" ]] || fail "Archive VERSION does not match release.json."

  build_release_version="$(read_manifest_string "$release_root/BUILD-INFO.json" releaseVersion)"
  [[ "$build_release_version" == "$RELEASE_VERSION" ]] || fail "Archive BUILD-INFO.json does not match release.json."

  (
    cd "$release_root"
    sha256sum -c SHA256SUMS >/dev/null
  ) || fail "Release archive failed its inner SHA256SUMS verification."

  awk '{ print $2 }' "$release_root/SHA256SUMS" | sed -e 's/^\*//' -e 's#^\./##' | sort > "$checksum_files"
  (
    cd "$release_root"
    find . -type f -printf '%P\n' | grep -v '^SHA256SUMS$' | sort
  ) > "$actual_files"

  if ! cmp -s "$checksum_files" "$actual_files"; then
    fail "Release archive contains unverified regular files or omits required inner checksum entries."
  fi

  printf '%s\n' "$release_root"
}

verify_installed_release_metadata() {
  local current_link
  local installed_version
  local build_release_version

  current_link="$(resolve_current_link)"
  [[ -f "$current_link/VERSION" ]] ||
    fail "Installed MEM Migrate release is missing VERSION."
  [[ -f "$current_link/BUILD-INFO.json" ]] ||
    fail "Installed MEM Migrate release is missing BUILD-INFO.json."

  installed_version="$(tr -d '[:space:]' < "$current_link/VERSION")"
  [[ "$installed_version" == "$RELEASE_VERSION" ]] ||
    fail "Installed VERSION does not match the resolved release."

  build_release_version="$(sed -nE 's/^[[:space:]]*"releaseVersion"[[:space:]]*:[[:space:]]*"([^"]+)"[[:space:]]*,?[[:space:]]*$/\1/p' "$current_link/BUILD-INFO.json" | sed -n '1p')"
  [[ "$build_release_version" == "$RELEASE_VERSION" ]] ||
    fail "Installed BUILD-INFO.json does not match the resolved release."
}

install_resolved_release() {
  local release_root

  if [[ "$CURRENT_RELEASE" == "$RELEASE_VERSION" ]]; then
    RELEASE_ALREADY_CURRENT=true
    if [[ "$DRY_RUN" != true ]]; then
      verify_installed_release_metadata
    fi
    log_info "MEM Migrate $RELEASE_VERSION is already the current release; no release files will be changed."
    return
  fi

  if [[ "$DRY_RUN" == true ]]; then
    return
  fi

  download_and_verify_release_archive
  release_root="$(extract_and_verify_release)"
  log_info "Release archive and inner payload checksums verified."
  log_info "Installing MEM Migrate $RELEASE_VERSION atomically."
  /bin/bash "$release_root/install.sh"

  resolve_existing_release
  [[ "$CURRENT_RELEASE" == "$RELEASE_VERSION" ]] ||
    fail "Atomic installer completed but /opt/mem/migrate/current does not resolve to $RELEASE_VERSION."
  verify_installed_release_metadata
}

print_plan() {
  printf '\n%s\n' "$PROGRAM_NAME"
  printf '  Programme slice:        %s\n' "$PROGRAM_VERSION"
  printf '  Mode:                   %s\n' "$([[ "$DRY_RUN" == true ]] && printf 'dry-run' || printf 'apply')"
  printf '  Supported host policy:  Ubuntu %s %s\n' "$SUPPORTED_OS_VERSION" "$SUPPORTED_ARCH"
  printf '  Docker policy:          validate only; never install or modify\n'
  printf '  age policy:             preserve working command; install Ubuntu package when missing\n'
  printf '  Release policy:         verified %s bundle; atomic install; no Git required\n' "$SUPPORTED_RUNTIME"
  printf '\n'
}

print_host_summary() {
  printf '\nHost dependency assurance\n'
  printf '  Operating system:       %s\n' "${OS_PRETTY_NAME:-Ubuntu $OS_VERSION}"
  printf '  Architecture:           %s (%s)\n' "$ARCH" "$ARCH_RAW"
  printf '  Installation space:     %s KiB available\n' "$DISK_AVAILABLE_KIB"
  printf '  Docker command:         %s\n' "$DOCKER_PATH"
  printf '  Docker version:         %s\n' "$DOCKER_VERSION"
  printf '  age command:            %s\n' "${AGE_PATH:-missing}"
  printf '  age version:            %s\n' "${AGE_VERSION:-missing}"
  printf '  Current MEM Migrate:    %s\n' "$CURRENT_RELEASE"
  printf '\nResolved release\n'
  printf '  Source:                 %s\n' "$RELEASE_SOURCE"
  printf '  Channel:                %s\n' "$RELEASE_CHANNEL"
  printf '  Release:                %s\n' "$RELEASE_VERSION"
  printf '  Runtime:                %s\n' "$RELEASE_RUNTIME"
  printf '  Published:              %s\n' "$RELEASE_PUBLISHED_AT_UTC"
  printf '  Archive SHA-256:        %s\n' "$RELEASE_ARCHIVE_SHA256"
  printf '\n'

  if [[ "$DRY_RUN" == true ]]; then
    if [[ "$CURRENT_RELEASE" == "$RELEASE_VERSION" ]]; then
      printf 'Dry-run complete. The requested release is already current.\n'
    else
      printf 'Dry-run complete. Would download, verify, and atomically install MEM Migrate %s.\n' "$RELEASE_VERSION"
    fi
    printf 'No release payload was downloaded and no packages, release files, stable commands, or MEM Migrate state were modified.\n'
    return
  fi

  if [[ "$RELEASE_ALREADY_CURRENT" == true ]]; then
    printf 'MEM Migrate %s is already installed and current.\n' "$RELEASE_VERSION"
  else
    printf 'MEM Migrate installed successfully.\n'
    printf '  Release:                %s\n' "$RELEASE_VERSION"
    printf '  Current path:           %s\n' "$(readlink -f -- "$(resolve_current_link)" 2>/dev/null || printf 'unresolved')"
  fi

  printf '\nMM-BOOT-01B installs the verified release without Git. Stable start, stop, and status commands are delivered by MM-BOOT-01C.\n'
}

main() {
  parse_arguments "$@"

  for command_name in uname awk sed grep wc tr mktemp rm chmod df basename dirname readlink cat cp cmp sort uniq find tar sha256sum curl; do
    require_command "$command_name"
  done

  [[ "$MIN_INSTALL_FREE_KIB" =~ ^[0-9]+$ ]] || fail "MEM_MIGRATE_BOOTSTRAP_MIN_FREE_KIB must be a positive integer."
  (( MIN_INSTALL_FREE_KIB > 0 )) || fail "MEM_MIGRATE_BOOTSTRAP_MIN_FREE_KIB must be greater than zero."

  print_plan
  validate_privilege_boundary
  create_private_temp_workspace
  validate_host_identity
  validate_install_capacity
  resolve_existing_release
  validate_docker
  ensure_age_and_transport_dependencies
  resolve_release_manifest
  install_resolved_release
  print_host_summary
}

main "$@"
