#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd -P)"

VERSION=""
OUTPUT_DIR="${REPO_ROOT}/artifacts/release"
CLI_PROJECT="${REPO_ROOT}/cli/src/Mem.Cli/Mem.Cli.csproj"
SOURCE_COMMIT=""
SOURCE_DATE_EPOCH_VALUE="${SOURCE_DATE_EPOCH:-}"
ALLOW_DIRTY=false

usage() {
    cat <<'EOF_USAGE'
Build the standalone MEM CLI release artifact.

Usage:
  ./scripts/release/build-cli-release.sh --version <semver> [options]

Required:
  --version <semver>
      Exact stable/prerelease MEM version, for example 0.2.0 or 0.2.0-rc.1.

Options:
  --output-dir <path>
      Output directory. Default: artifacts/release under the repository.

  --project <path>
      MEM CLI project. Default: cli/src/Mem.Cli/Mem.Cli.csproj.

  --source-commit <hex>
      Exact source commit. Defaults to git rev-parse HEAD.

  --source-date-epoch <seconds>
      Reproducible build timestamp. Defaults to SOURCE_DATE_EPOCH or the
      selected git commit timestamp.

  --allow-dirty
      Permit a build from a dirty Git worktree for development proof only.
      The generated build-info records releaseEligible=false.

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
        --output-dir)
            [[ $# -ge 2 ]] || fail "--output-dir requires a path"
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --project)
            [[ $# -ge 2 ]] || fail "--project requires a path"
            CLI_PROJECT="$2"
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
        --allow-dirty)
            ALLOW_DIRTY=true
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

for command_name in dotnet python3 sha256sum install mkdir rm mktemp git; do
    require_command "${command_name}"
done

[[ -n "${VERSION}" ]] || fail "--version is required"
[[ "${VERSION}" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]] || \
    fail "Invalid release version '${VERSION}'. Expected stable/prerelease SemVer without build metadata."

case "${VERSION}" in
    *latest*|*stable*|*current*|*dev*)
        fail "Public CLI release version may not contain a mutable channel/development label: ${VERSION}"
        ;;
esac

[[ -f "${CLI_PROJECT}" ]] || fail "MEM CLI project not found: ${CLI_PROJECT}"
[[ -f "${SCRIPT_DIR}/RELEASE-CONTRACT.md" ]] || fail "Unified release contract is missing. Apply RELEASE-020-CONTRACT-03A first."
[[ -f "${SCRIPT_DIR}/build-installer-bundle.sh" ]] || fail "Installer release builder is missing. Apply RELEASE-020-INSTALLER-03B first."

if [[ -z "${SOURCE_COMMIT}" ]]; then
    SOURCE_COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD 2>/dev/null)" || \
        fail "Could not determine source commit. Supply --source-commit."
fi
[[ "${SOURCE_COMMIT}" =~ ^[0-9a-f]{40,64}$ ]] || fail "Invalid source commit '${SOURCE_COMMIT}'."

HEAD_COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD 2>/dev/null)" || \
    fail "Repository root is not a readable Git worktree: ${REPO_ROOT}"
[[ "${HEAD_COMMIT}" == "${SOURCE_COMMIT}" ]] || \
    fail "Selected source commit ${SOURCE_COMMIT} does not match repository HEAD ${HEAD_COMMIT}."

WORKTREE_DIRTY=false
if [[ -n "$(git -C "${REPO_ROOT}" status --porcelain --untracked-files=all)" ]]; then
    WORKTREE_DIRTY=true
fi

if [[ "${WORKTREE_DIRTY}" == true && "${ALLOW_DIRTY}" != true ]]; then
    fail "Repository worktree is dirty. Commit/review changes before a release-eligible build, or use --allow-dirty for development proof only."
fi

if [[ -z "${SOURCE_DATE_EPOCH_VALUE}" ]]; then
    SOURCE_DATE_EPOCH_VALUE="$(git -C "${REPO_ROOT}" show -s --format=%ct "${SOURCE_COMMIT}" 2>/dev/null)" || \
        fail "Could not determine source timestamp. Supply --source-date-epoch."
fi
[[ "${SOURCE_DATE_EPOCH_VALUE}" =~ ^[0-9]{1,12}$ ]] || fail "Invalid source date epoch '${SOURCE_DATE_EPOCH_VALUE}'."

RELEASE_ELIGIBLE=true
if [[ "${WORKTREE_DIRTY}" == true ]]; then
    RELEASE_ELIGIBLE=false
    log "WARNING: dirty-worktree development proof; generated build-info will record releaseEligible=false."
fi

RUNTIME="linux-x64"
CONFIGURATION="Release"
FILE_NAME="mem-cli-${VERSION}-${RUNTIME}"
mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd -- "${OUTPUT_DIR}" && pwd -P)"
OUTPUT_PATH="${OUTPUT_DIR}/${FILE_NAME}"
BUILD_INFO_PATH="${OUTPUT_DIR}/${FILE_NAME}.build-info.json"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-cli-release.XXXXXX")"
PUBLISH_DIR="${TMP_ROOT}/publish"
mkdir -p "${PUBLISH_DIR}"
trap 'rm -rf -- "${TMP_ROOT}"' EXIT

VERSION_CORE="${VERSION%%-*}"
ASSEMBLY_VERSION="${VERSION_CORE}.0"

log "Publishing Message Easy Mode CLI ${VERSION}"
SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH_VALUE}" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
dotnet publish "${CLI_PROJECT}" \
    -c "${CONFIGURATION}" \
    -r "${RUNTIME}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${VERSION}" \
    -p:AssemblyVersion="${ASSEMBLY_VERSION}" \
    -p:FileVersion="${ASSEMBLY_VERSION}" \
    -p:InformationalVersion="${VERSION}" \
    -p:IncludeSourceRevisionInInformationalVersion=false \
    -p:Product="Message Easy Mode CLI" \
    -p:Company="Message Easy Mode" \
    -p:ContinuousIntegrationBuild=true \
    -p:Deterministic=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "${PUBLISH_DIR}"

PUBLISHED_BINARY="${PUBLISH_DIR}/mem"
[[ -f "${PUBLISHED_BINARY}" ]] || fail "dotnet publish did not produce ${PUBLISHED_BINARY}"
chmod 0755 "${PUBLISHED_BINARY}"

VERSION_OUTPUT="$("${PUBLISHED_BINARY}" --version 2>&1)" || \
    fail "Published MEM CLI --version failed."

EXPECTED_OUTPUT="$(cat <<EOF_EXPECTED
Message Easy Mode CLI
Version: ${VERSION}
Command: mem
EOF_EXPECTED
)"

[[ "${VERSION_OUTPUT}" == "${EXPECTED_OUTPUT}" ]] || \
    fail "Published MEM CLI identity/version output is not exact. Got: ${VERSION_OUTPUT}"

if grep -Fq "Matrix Easy Mode CLI" <<< "${VERSION_OUTPUT}"; then
    fail "Published CLI still exposes legacy Matrix Easy Mode product identity."
fi

install -m 0755 "${PUBLISHED_BINARY}" "${OUTPUT_PATH}"

ARTIFACT_SHA256="$(sha256sum "${OUTPUT_PATH}" | awk '{print $1}')"
ARTIFACT_SIZE="$(python3 - "${OUTPUT_PATH}" <<'PY_SIZE'
from pathlib import Path
import sys
print(Path(sys.argv[1]).stat().st_size)
PY_SIZE
)"
GENERATED_AT_UTC="$(python3 - "${SOURCE_DATE_EPOCH_VALUE}" <<'PY_TIME'
from datetime import datetime, timezone
import sys
print(datetime.fromtimestamp(int(sys.argv[1]), timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'))
PY_TIME
)"

python3 - \
    "${BUILD_INFO_PATH}" \
    "${VERSION}" \
    "${SOURCE_COMMIT}" \
    "${SOURCE_DATE_EPOCH_VALUE}" \
    "${GENERATED_AT_UTC}" \
    "${FILE_NAME}" \
    "${ARTIFACT_SHA256}" \
    "${ARTIFACT_SIZE}" \
    "${WORKTREE_DIRTY}" \
    "${RELEASE_ELIGIBLE}" <<'PY_BUILD_INFO'
import json
import sys

(
    output,
    version,
    commit,
    source_date_epoch,
    generated_at,
    file_name,
    sha256,
    size_bytes,
    worktree_dirty,
    release_eligible,
) = sys.argv[1:]

doc = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "version": version,
    },
    "artifact": {
        "id": "cli",
        "kind": "cli-binary",
        "fileName": file_name,
        "runtime": "linux-x64",
        "sha256": sha256,
        "sizeBytes": int(size_bytes),
    },
    "source": {
        "commit": commit,
        "sourceDateEpoch": int(source_date_epoch),
        "workingTreeDirty": worktree_dirty == "true",
    },
    "build": {
        "configuration": "Release",
        "selfContained": True,
        "singleFile": True,
        "generatedAtUtc": generated_at,
        "releaseEligible": release_eligible == "true",
    },
}
with open(output, "w", encoding="utf-8") as handle:
    json.dump(doc, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY_BUILD_INFO

# Re-read the installed artifact, not the temporary publish output.
VERIFY_OUTPUT="$("${OUTPUT_PATH}" --version 2>&1)" || fail "Final CLI artifact --version failed."
[[ "${VERIFY_OUTPUT}" == "${EXPECTED_OUTPUT}" ]] || fail "Final CLI artifact version output changed after installation."

log "MEM CLI release artifact complete"
echo "Artifact:         ${OUTPUT_PATH}"
echo "SHA-256:         ${ARTIFACT_SHA256}"
echo "Size:            ${ARTIFACT_SIZE}"
echo "Build info:      ${BUILD_INFO_PATH}"
echo "Source commit:   ${SOURCE_COMMIT}"
echo "Release eligible:${RELEASE_ELIGIBLE}"
