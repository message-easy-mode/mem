#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd -P)"

VERSION=""
CONTROL_PLANE_IMAGE=""
SOURCE_COMMIT=""
SOURCE_DATE_EPOCH_VALUE="${SOURCE_DATE_EPOCH:-}"
OUTPUT_DIR="${REPO_ROOT}/artifacts/release"
CLI_BINARY=""
CLI_PROJECT="${REPO_ROOT}/cli/src/Mem.Cli/Mem.Cli.csproj"
CLI_INSTALL_SCRIPT=""
KEEP_STAGING=false

usage() {
    cat <<'EOF_USAGE'
Build a versioned MEM installer release bundle.

Usage:
  ./scripts/release/build-installer-bundle.sh \
    --version <semver> \
    --control-plane-image <version+digest-reference> \
    [options]

Required:
  --version <semver>
      Exact MEM release version, for example 0.2.0 or 0.2.0-rc.1.

  --control-plane-image <reference>
      Exact digest-bearing Control Plane image reference:
      ghcr.io/message-easy-mode/mem-control-plane:<version>@sha256:<64-hex>

Options:
  --source-commit <hex>
      Exact source commit identity. Defaults to git rev-parse HEAD.

  --source-date-epoch <seconds>
      Reproducible package timestamp. Defaults to SOURCE_DATE_EPOCH or the
      selected git commit timestamp.

  --output-dir <path>
      Output directory. Default: artifacts/release under the repository.

  --cli-binary <path>
      Use this prebuilt MEM CLI binary. Final release orchestration should pass
      the canonical CLI release binary produced by the CLI release workflow.
      When omitted, this script publishes cli/src/Mem.Cli from source.

  --cli-project <path>
      CLI project used when --cli-binary is omitted.

  --cli-install-script <path>
      Host CLI installation script to embed. Defaults to the canonical CLI
      source script, then falls back to bootstrap/cli/install-host-command.sh.

  --keep-staging
      Keep the temporary bundle root after a successful build for inspection.

  -h, --help
      Show this help.
EOF_USAGE
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

log() {
    echo "[release] $*"
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            [[ $# -ge 2 ]] || fail "--version requires a value"
            VERSION="$2"
            shift 2
            ;;
        --control-plane-image)
            [[ $# -ge 2 ]] || fail "--control-plane-image requires a value"
            CONTROL_PLANE_IMAGE="$2"
            shift 2
            ;;
        --source-commit)
            [[ $# -ge 2 ]] || fail "--source-commit requires a value"
            SOURCE_COMMIT="$2"
            shift 2
            ;;
        --source-date-epoch)
            [[ $# -ge 2 ]] || fail "--source-date-epoch requires a value"
            SOURCE_DATE_EPOCH_VALUE="$2"
            shift 2
            ;;
        --output-dir)
            [[ $# -ge 2 ]] || fail "--output-dir requires a path"
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --cli-binary)
            [[ $# -ge 2 ]] || fail "--cli-binary requires a path"
            CLI_BINARY="$2"
            shift 2
            ;;
        --cli-project)
            [[ $# -ge 2 ]] || fail "--cli-project requires a path"
            CLI_PROJECT="$2"
            shift 2
            ;;
        --cli-install-script)
            [[ $# -ge 2 ]] || fail "--cli-install-script requires a path"
            CLI_INSTALL_SCRIPT="$2"
            shift 2
            ;;
        --keep-staging)
            KEEP_STAGING=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown option: $1"
            ;;
    esac
done

for command_name in python3 sha256sum tar gzip find sort mktemp cp install chmod touch awk xargs mkdir rm; do
    require_command "${command_name}"
done

[[ -n "${VERSION}" ]] || fail "--version is required"
[[ "${VERSION}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]] || \
    fail "Invalid release version '${VERSION}'. Expected stable/prerelease SemVer without build metadata."

[[ -n "${CONTROL_PLANE_IMAGE}" ]] || fail "--control-plane-image is required"
EXPECTED_IMAGE_PREFIX="ghcr.io/message-easy-mode/mem-control-plane:${VERSION}@sha256:"
[[ "${CONTROL_PLANE_IMAGE}" == "${EXPECTED_IMAGE_PREFIX}"* ]] || \
    fail "Control Plane image must use exact version ${VERSION} and sha256 digest."
IMAGE_DIGEST="${CONTROL_PLANE_IMAGE#*@}"
[[ "${IMAGE_DIGEST}" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Control Plane image digest is invalid: ${IMAGE_DIGEST}"

[[ -f "${REPO_ROOT}/install.sh" ]] || fail "Root install.sh not found at ${REPO_ROOT}/install.sh"
[[ -d "${REPO_ROOT}/bootstrap" ]] || fail "bootstrap/ source not found at ${REPO_ROOT}/bootstrap"
[[ -f "${SCRIPT_DIR}/RELEASE-CONTRACT.md" ]] || fail "Unified release contract is missing. Apply RELEASE-020-CONTRACT-03A first."

if [[ -z "${SOURCE_COMMIT}" ]]; then
    require_command git
    SOURCE_COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD 2>/dev/null)" || fail "Could not determine source commit. Supply --source-commit."
fi
[[ "${SOURCE_COMMIT}" =~ ^[0-9a-f]{40,64}$ ]] || fail "Invalid source commit '${SOURCE_COMMIT}'."

if [[ -z "${SOURCE_DATE_EPOCH_VALUE}" ]]; then
    require_command git
    SOURCE_DATE_EPOCH_VALUE="$(git -C "${REPO_ROOT}" show -s --format=%ct "${SOURCE_COMMIT}" 2>/dev/null)" || \
        fail "Could not determine source timestamp. Supply --source-date-epoch."
fi
[[ "${SOURCE_DATE_EPOCH_VALUE}" =~ ^[0-9]{1,12}$ ]] || fail "Invalid source date epoch '${SOURCE_DATE_EPOCH_VALUE}'."

if [[ -z "${CLI_INSTALL_SCRIPT}" ]]; then
    if [[ -f "${REPO_ROOT}/cli/src/Mem.Cli/Scripts/install-host-command.sh" ]]; then
        CLI_INSTALL_SCRIPT="${REPO_ROOT}/cli/src/Mem.Cli/Scripts/install-host-command.sh"
    else
        CLI_INSTALL_SCRIPT="${REPO_ROOT}/bootstrap/cli/install-host-command.sh"
    fi
fi
[[ -f "${CLI_INSTALL_SCRIPT}" ]] || fail "MEM CLI host install script not found: ${CLI_INSTALL_SCRIPT}"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-installer-release.XXXXXX")"
STAGING_PARENT="${TMP_ROOT}/staging"
CLI_PUBLISH_DIR="${TMP_ROOT}/cli-publish"
mkdir -p "${STAGING_PARENT}" "${CLI_PUBLISH_DIR}"

cleanup() {
    if [[ "${KEEP_STAGING}" == true && -d "${STAGING_PARENT}" ]]; then
        echo "[release] Staging retained at: ${STAGING_PARENT}" >&2
        return 0
    fi
    rm -rf -- "${TMP_ROOT}"
}
trap cleanup EXIT

if [[ -z "${CLI_BINARY}" ]]; then
    require_command dotnet
    [[ -f "${CLI_PROJECT}" ]] || fail "MEM CLI project not found: ${CLI_PROJECT}"

    VERSION_CORE="${VERSION%%-*}"
    ASSEMBLY_VERSION="${VERSION_CORE}.0"

    log "Publishing MEM CLI ${VERSION} from ${CLI_PROJECT}"
    dotnet publish "${CLI_PROJECT}" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:Version="${VERSION}" \
        -p:AssemblyVersion="${ASSEMBLY_VERSION}" \
        -p:FileVersion="${ASSEMBLY_VERSION}" \
        -p:InformationalVersion="${VERSION}" \
        -p:IncludeSourceRevisionInInformationalVersion=false \
        -o "${CLI_PUBLISH_DIR}"
    CLI_BINARY="${CLI_PUBLISH_DIR}/mem"
fi

[[ -f "${CLI_BINARY}" ]] || fail "MEM CLI binary not found: ${CLI_BINARY}"
[[ -x "${CLI_BINARY}" ]] || chmod +x "${CLI_BINARY}"

CLI_VERSION_OUTPUT="$(${CLI_BINARY} --version 2>&1)" || fail "MEM CLI --version failed for ${CLI_BINARY}"
CLI_REPORTED_VERSION=""
while IFS= read -r line; do
    case "${line}" in
        "Version: "*) CLI_REPORTED_VERSION="${line#Version: }" ;;
    esac
done <<< "${CLI_VERSION_OUTPUT}"
[[ "${CLI_REPORTED_VERSION}" == "${VERSION}" ]] || \
    fail "MEM CLI binary does not report exact release version ${VERSION}. Output: ${CLI_VERSION_OUTPUT}"

ROOT_NAME="mem-installer-${VERSION}-ubuntu-24.04-amd64"
BUNDLE_ROOT="${STAGING_PARENT}/${ROOT_NAME}"
mkdir -p "${BUNDLE_ROOT}"

log "Staging existing MEM bootstrap"
cp -a "${REPO_ROOT}/install.sh" "${BUNDLE_ROOT}/install.sh"
cp -a "${REPO_ROOT}/bootstrap" "${BUNDLE_ROOT}/bootstrap"

# Source/test files are not runtime release content.
rm -rf -- "${BUNDLE_ROOT}/bootstrap/tests"

install -m 0755 "${CLI_INSTALL_SCRIPT}" "${BUNDLE_ROOT}/bootstrap/cli/install-host-command.sh"
install -m 0755 "${CLI_BINARY}" "${BUNDLE_ROOT}/bootstrap/cli/mem"

CHANNEL="stable"
if [[ "${VERSION}" == *-* ]]; then
    CHANNEL="prerelease"
fi

cat > "${BUNDLE_ROOT}/bootstrap/release.env" <<EOF_RELEASE_ENV
# Generated by scripts/release/build-installer-bundle.sh.
# Contains public immutable release identity only; no secrets.
MEM_RELEASE_MODE='release'
MEM_RELEASE_VERSION='${VERSION}'
MEM_RELEASE_CHANNEL='${CHANNEL}'
MEM_RELEASE_CONTROL_PLANE_IMAGE='${CONTROL_PLANE_IMAGE}'
MEM_RELEASE_PLATFORM_OS='ubuntu'
MEM_RELEASE_PLATFORM_VERSION='24.04'
MEM_RELEASE_PLATFORM_ARCH='amd64'
EOF_RELEASE_ENV
chmod 0644 "${BUNDLE_ROOT}/bootstrap/release.env"

printf '%s\n' "${VERSION}" > "${BUNDLE_ROOT}/VERSION"

CLI_SHA256="$(sha256sum "${BUNDLE_ROOT}/bootstrap/cli/mem" | awk '{print $1}')"
GENERATED_AT_UTC="$(python3 - "${SOURCE_DATE_EPOCH_VALUE}" <<'PY_TIME'
from datetime import datetime, timezone
import sys
print(datetime.fromtimestamp(int(sys.argv[1]), timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'))
PY_TIME
)"

python3 - \
    "${BUNDLE_ROOT}/BUILD-INFO.json" \
    "${VERSION}" \
    "${CHANNEL}" \
    "${SOURCE_COMMIT}" \
    "${SOURCE_DATE_EPOCH_VALUE}" \
    "${GENERATED_AT_UTC}" \
    "${CONTROL_PLANE_IMAGE}" \
    "${CLI_SHA256}" <<'PY_BUILD_INFO'
import json
import sys
(
    output,
    version,
    channel,
    commit,
    source_date_epoch,
    generated_at,
    image,
    cli_sha,
) = sys.argv[1:]

doc = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "version": version,
        "channel": channel,
    },
    "source": {
        "commit": commit,
        "sourceDateEpoch": int(source_date_epoch),
    },
    "generatedAtUtc": generated_at,
    "platform": {
        "os": "ubuntu",
        "version": "24.04",
        "architecture": "amd64",
    },
    "controlPlane": {
        "image": image,
    },
    "cli": {
        "runtime": "linux-x64",
        "sha256": cli_sha,
    },
}
with open(output, "w", encoding="utf-8") as handle:
    json.dump(doc, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY_BUILD_INFO

chmod 0755 "${BUNDLE_ROOT}/install.sh" "${BUNDLE_ROOT}/bootstrap/install.sh"
find "${BUNDLE_ROOT}/bootstrap/lib" -type f -name '*.sh' -exec chmod 0755 {} +
find "${BUNDLE_ROOT}/bootstrap/cli" -type f -name '*.sh' -exec chmod 0755 {} +

(
    cd "${BUNDLE_ROOT}"
    find . -type f ! -name SHA256SUMS -print0 \
        | sort -z \
        | xargs -0 sha256sum > SHA256SUMS
    sha256sum -c SHA256SUMS >/dev/null
)

# Normalize timestamps after every generated file is complete. Packaging owner,
# group, ordering and gzip headers are also normalized below.
find "${BUNDLE_ROOT}" -exec touch -h -d "@${SOURCE_DATE_EPOCH_VALUE}" {} +

mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd -- "${OUTPUT_DIR}" && pwd -P)"
ARCHIVE_PATH="${OUTPUT_DIR}/${ROOT_NAME}.tar.gz"
CHECKSUM_PATH="${ARCHIVE_PATH}.sha256"

log "Writing deterministic installer archive ${ARCHIVE_PATH}"
(
    cd "${STAGING_PARENT}"
    tar \
        --sort=name \
        --mtime="@${SOURCE_DATE_EPOCH_VALUE}" \
        --owner=0 \
        --group=0 \
        --numeric-owner \
        --format=gnu \
        -cf - "${ROOT_NAME}" \
        | gzip -n -9 > "${ARCHIVE_PATH}"
)

(
    cd "${OUTPUT_DIR}"
    sha256sum "$(basename "${ARCHIVE_PATH}")" > "$(basename "${CHECKSUM_PATH}")"
    sha256sum -c "$(basename "${CHECKSUM_PATH}")" >/dev/null
)

VERIFY_ROOT="${TMP_ROOT}/verify"
mkdir -p "${VERIFY_ROOT}"
tar -xzf "${ARCHIVE_PATH}" -C "${VERIFY_ROOT}"
[[ -d "${VERIFY_ROOT}/${ROOT_NAME}" ]] || fail "Archive verification did not produce fixed root ${ROOT_NAME}"
[[ "$(cat "${VERIFY_ROOT}/${ROOT_NAME}/VERSION")" == "${VERSION}" ]] || fail "Archive VERSION mismatch"
(
    cd "${VERIFY_ROOT}/${ROOT_NAME}"
    sha256sum -c SHA256SUMS >/dev/null
)
VERIFY_CLI_OUTPUT="$(${VERIFY_ROOT}/${ROOT_NAME}/bootstrap/cli/mem --version 2>&1)" || fail "Archived MEM CLI --version failed"
VERIFY_CLI_VERSION=""
while IFS= read -r line; do
    case "${line}" in
        "Version: "*) VERIFY_CLI_VERSION="${line#Version: }" ;;
    esac
done <<< "${VERIFY_CLI_OUTPUT}"
[[ "${VERIFY_CLI_VERSION}" == "${VERSION}" ]] || fail "Archived MEM CLI version mismatch: ${VERIFY_CLI_OUTPUT}"

ARCHIVE_SHA256="$(sha256sum "${ARCHIVE_PATH}" | awk '{print $1}')"
log "Installer bundle complete"
echo "Artifact:       ${ARCHIVE_PATH}"
echo "SHA-256:       ${ARCHIVE_SHA256}"
echo "Source commit: ${SOURCE_COMMIT}"
echo "MEM version:   ${VERSION}"
echo "Control Plane: ${CONTROL_PLANE_IMAGE}"
echo "CLI SHA-256:   ${CLI_SHA256}"
