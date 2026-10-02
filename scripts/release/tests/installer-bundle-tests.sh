#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"
REPO_ROOT="$(cd -- "${RELEASE_DIR}/../.." && pwd -P)"
BUILDER="${RELEASE_DIR}/build-installer-bundle.sh"
VERSION="0.2.0"
DIGEST="sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
IMAGE="ghcr.io/message-easy-mode/mem-control-plane:${VERSION}@${DIGEST}"
SOURCE_COMMIT="0123456789abcdef0123456789abcdef01234567"
SOURCE_DATE_EPOCH_VALUE="1788393600"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-installer-bundle-tests.XXXXXX")"
trap 'rm -rf -- "${TMP_ROOT}"' EXIT

PASSED=0
FAILED=0

pass() { echo "PASS: $1"; PASSED=$((PASSED + 1)); }
fail_test() { echo "FAIL: $1" >&2; [[ $# -lt 2 ]] || echo "  $2" >&2; FAILED=$((FAILED + 1)); }

assert_eq() {
    local expected="$1" actual="$2" name="$3"
    if [[ "${actual}" == "${expected}" ]]; then
        pass "${name}"
    else
        fail_test "${name}" "expected '${expected}', got '${actual}'"
    fi
}

assert_file() {
    local path="$1" name="$2"
    [[ -f "${path}" ]] && pass "${name}" || fail_test "${name}" "missing ${path}"
}

assert_not_path() {
    local path="$1" name="$2"
    [[ ! -e "${path}" ]] && pass "${name}" || fail_test "${name}" "unexpected ${path}"
}

FAKE_CLI="${TMP_ROOT}/mem"
cat > "${FAKE_CLI}" <<'EOF_CLI'
#!/usr/bin/env bash
if [[ "${1:-}" == "--version" || "${1:-}" == "version" ]]; then
    echo "Message Easy Mode CLI"
    echo "Version: 0.2.0"
    echo "Command: mem"
    exit 0
fi
echo "fake mem cli"
EOF_CLI
chmod +x "${FAKE_CLI}"
FAKE_CLI_SHA="$(sha256sum "${FAKE_CLI}" | awk '{print $1}')"

OUT1="${TMP_ROOT}/out1"
OUT2="${TMP_ROOT}/out2"
mkdir -p "${OUT1}" "${OUT2}"

build_into() {
    local out="$1"
    "${BUILDER}" \
        --version "${VERSION}" \
        --control-plane-image "${IMAGE}" \
        --source-commit "${SOURCE_COMMIT}" \
        --source-date-epoch "${SOURCE_DATE_EPOCH_VALUE}" \
        --cli-binary "${FAKE_CLI}" \
        --output-dir "${out}" >/dev/null
}

build_into "${OUT1}"
build_into "${OUT2}"

ROOT_NAME="mem-installer-${VERSION}-ubuntu-24.04-amd64"
ARCHIVE1="${OUT1}/${ROOT_NAME}.tar.gz"
ARCHIVE2="${OUT2}/${ROOT_NAME}.tar.gz"
SIDE1="${ARCHIVE1}.sha256"

assert_file "${ARCHIVE1}" "versioned installer archive is produced"
assert_file "${SIDE1}" "outer archive SHA-256 sidecar is produced"

(
    cd "${OUT1}"
    sha256sum -c "$(basename "${SIDE1}")" >/dev/null
) && pass "outer installer archive SHA-256 verifies" || fail_test "outer installer archive SHA-256 verifies"

SHA1="$(sha256sum "${ARCHIVE1}" | awk '{print $1}')"
SHA2="$(sha256sum "${ARCHIVE2}" | awk '{print $1}')"
assert_eq "${SHA1}" "${SHA2}" "identical release inputs produce a deterministic archive"

EXTRACTED="${TMP_ROOT}/extracted"
mkdir -p "${EXTRACTED}"
tar -xzf "${ARCHIVE1}" -C "${EXTRACTED}"
BUNDLE="${EXTRACTED}/${ROOT_NAME}"

assert_file "${BUNDLE}/VERSION" "bundle carries VERSION"
assert_file "${BUNDLE}/BUILD-INFO.json" "bundle carries BUILD-INFO.json"
assert_file "${BUNDLE}/SHA256SUMS" "bundle carries inner SHA256SUMS"
assert_file "${BUNDLE}/bootstrap/release.env" "bundle carries non-secret release.env"
assert_file "${BUNDLE}/bootstrap/cli/mem" "bundle carries MEM CLI binary"
assert_not_path "${BUNDLE}/bootstrap/tests" "release bundle excludes bootstrap source tests"

if grep -Fq -- 'App__PublicBaseUrl=${public_base_url}' "${BUNDLE}/bootstrap/lib/installer.sh"; then
    if grep -Fq -- 'control_plane_canonical_private_origin' "${BUNDLE}/bootstrap/lib/control-plane-access.sh"; then
        pass "release bundle carries canonical production browser-authority wiring"
    else
        fail_test "release bundle carries canonical production browser-authority wiring" "canonical private origin helper is missing from packaged access policy"
    fi
else
    fail_test "release bundle carries canonical production browser-authority wiring" "App__PublicBaseUrl is missing from packaged container launch"
fi

assert_eq "${VERSION}" "$(cat "${BUNDLE}/VERSION")" "bundle VERSION matches release"

(
    cd "${BUNDLE}"
    sha256sum -c SHA256SUMS >/dev/null
) && pass "inner SHA256SUMS verifies every bundled file" || fail_test "inner SHA256SUMS verifies every bundled file"

BUNDLED_CLI_SHA="$(sha256sum "${BUNDLE}/bootstrap/cli/mem" | awk '{print $1}')"
assert_eq "${FAKE_CLI_SHA}" "${BUNDLED_CLI_SHA}" "installer embeds the exact supplied CLI binary"

CLI_OUTPUT="$(${BUNDLE}/bootstrap/cli/mem --version)"
[[ "${CLI_OUTPUT}" == *"Version: ${VERSION}"* ]] && pass "bundled CLI reports MEM release version" || fail_test "bundled CLI reports MEM release version" "${CLI_OUTPUT}"

python3 - "${BUNDLE}/BUILD-INFO.json" "${VERSION}" "${IMAGE}" "${SOURCE_COMMIT}" "${FAKE_CLI_SHA}" <<'PY_BUILD'
import json, sys
path, version, image, commit, cli_sha = sys.argv[1:]
with open(path, encoding="utf-8") as h:
    doc = json.load(h)
assert doc["product"]["version"] == version
assert doc["controlPlane"]["image"] == image
assert doc["source"]["commit"] == commit
assert doc["platform"] == {"os": "ubuntu", "version": "24.04", "architecture": "amd64"}
assert doc["cli"]["runtime"] == "linux-x64"
assert doc["cli"]["sha256"] == cli_sha
PY_BUILD
pass "BUILD-INFO binds version, source, platform, Control Plane image, and CLI hash"

# shellcheck disable=SC1090
source "${BUNDLE}/bootstrap/release.env"
assert_eq "release" "${MEM_RELEASE_MODE}" "release.env marks packaged release mode"
assert_eq "${VERSION}" "${MEM_RELEASE_VERSION}" "release.env carries release version"
assert_eq "stable" "${MEM_RELEASE_CHANNEL}" "release.env carries stable release channel"
assert_eq "${IMAGE}" "${MEM_RELEASE_CONTROL_PLANE_IMAGE}" "release.env carries immutable image identity"
assert_eq "24.04" "${MEM_RELEASE_PLATFORM_VERSION}" "release.env carries certified Ubuntu version"
assert_eq "amd64" "${MEM_RELEASE_PLATFORM_ARCH}" "release.env carries certified architecture"

# Verify release-aware library defaults without starting Docker or requiring root.
(
    SCRIPT_DIR="${BUNDLE}/bootstrap"
    REPO_ROOT="${BUNDLE}"
    fail() { echo "ERROR: $*" >&2; exit 90; }
    log_info() { :; }
    log_warn() { :; }
    source "${BUNDLE}/bootstrap/lib/cli.sh"
    [[ "${MEM_CLI_VERSION}" == "${VERSION}" ]]
) && pass "packaged CLI version defaults to MEM release version" || fail_test "packaged CLI version defaults to MEM release version"

(
    SCRIPT_DIR="${BUNDLE}/bootstrap"
    REPO_ROOT="${BUNDLE}"
    fail() { echo "ERROR: $*" >&2; exit 90; }
    log_info() { :; }
    log_warn() { :; }
    source "${BUNDLE}/bootstrap/lib/installer.sh"
    CHANNEL="dev"
    CONTROL_PLANE_IMAGE_OVERRIDE=""
    resolve_control_plane_image
    [[ "${CONTROL_PLANE_IMAGE}" == "${IMAGE}" ]]
) && pass "packaged release image overrides mutable channel selection" || fail_test "packaged release image overrides mutable channel selection"

# Release host policy: Ubuntu 24.04 amd64 passes; Ubuntu 22.04 and arm64 fail closed.
(
    fail() { echo "ERROR: $*" >&2; exit 91; }
    log_info() { :; }
    log_warn() { :; }
    source "${BUNDLE}/bootstrap/lib/os.sh"
    MEM_RELEASE_MODE=release
    detect_os() { OS_ID=ubuntu; OS_NAME='Ubuntu 24.04'; OS_VERSION_ID=24.04; OS_CODENAME=noble; }
    uname() { echo x86_64; }
    validate_ubuntu
    validate_architecture
    [[ "${ARCH}" == amd64 ]]
) && pass "release host policy accepts Ubuntu 24.04 amd64" || fail_test "release host policy accepts Ubuntu 24.04 amd64"

set +e
HOST_2204_OUTPUT="$(
(
    fail() { echo "ERROR: $*" >&2; exit 91; }
    log_info() { :; }
    log_warn() { :; }
    source "${BUNDLE}/bootstrap/lib/os.sh"
    MEM_RELEASE_MODE=release
    detect_os() { OS_ID=ubuntu; OS_NAME='Ubuntu 22.04'; OS_VERSION_ID=22.04; OS_CODENAME=jammy; }
    validate_ubuntu
) 2>&1
)"
HOST_2204_STATUS=$?
set -e
if [[ ${HOST_2204_STATUS} -ne 0 && "${HOST_2204_OUTPUT}" == *"certified for Ubuntu 24.04"* ]]; then
    pass "release host policy rejects Ubuntu 22.04"
else
    fail_test "release host policy rejects Ubuntu 22.04" "${HOST_2204_OUTPUT}"
fi

set +e
ARM_OUTPUT="$(
(
    fail() { echo "ERROR: $*" >&2; exit 92; }
    log_info() { :; }
    log_warn() { :; }
    source "${BUNDLE}/bootstrap/lib/os.sh"
    MEM_RELEASE_MODE=release
    uname() { echo aarch64; }
    validate_architecture
) 2>&1
)"
ARM_STATUS=$?
set -e
if [[ ${ARM_STATUS} -ne 0 && "${ARM_OUTPUT}" == *"certified for amd64"* ]]; then
    pass "release host policy rejects arm64"
else
    fail_test "release host policy rejects arm64" "${ARM_OUTPUT}"
fi

# Packaged release metadata rejects a mutable dev channel before root/Docker work.
set +e
DEV_CHANNEL_OUTPUT="$("${BUNDLE}/bootstrap/install.sh" --dry-run --channel dev 2>&1)"
DEV_CHANNEL_STATUS=$?
set -e
if [[ ${DEV_CHANNEL_STATUS} -ne 0 && "${DEV_CHANNEL_OUTPUT}" == *"not valid for a packaged MEM release"* ]]; then
    pass "packaged release rejects --channel dev"
else
    fail_test "packaged release rejects --channel dev" "${DEV_CHANNEL_OUTPUT}"
fi

set +e
BAD_VERSION_OUTPUT="$("${BUNDLE}/bootstrap/install.sh" --dry-run --mem-cli-version 9.9.9 2>&1)"
BAD_VERSION_STATUS=$?
set -e
if [[ ${BAD_VERSION_STATUS} -ne 0 && "${BAD_VERSION_OUTPUT}" == *"does not match packaged MEM release"* ]]; then
    pass "packaged release rejects mismatched CLI version override"
else
    fail_test "packaged release rejects mismatched CLI version override" "${BAD_VERSION_OUTPUT}"
fi

# A prerelease bundle must preserve prerelease provenance all the way into the
# packaged bootstrap. This is deliberately separate from the mutable
# bootstrap image selector, whose default remains "stable" for development.
PRERELEASE_VERSION="0.2.0-rc.5.qa.5"
PRERELEASE_IMAGE="ghcr.io/message-easy-mode/mem-control-plane:${PRERELEASE_VERSION}@${DIGEST}"
PRERELEASE_CLI="${TMP_ROOT}/mem-prerelease"
cat > "${PRERELEASE_CLI}" <<EOF_PRERELEASE_CLI
#!/usr/bin/env bash
if [[ "\${1:-}" == "--version" || "\${1:-}" == "version" ]]; then
    echo "Message Easy Mode CLI"
    echo "Version: ${PRERELEASE_VERSION}"
    echo "Command: mem"
    exit 0
fi
echo "fake prerelease mem cli"
EOF_PRERELEASE_CLI
chmod +x "${PRERELEASE_CLI}"

PRERELEASE_OUT="${TMP_ROOT}/prerelease-out"
mkdir -p "${PRERELEASE_OUT}"
"${BUILDER}" \
    --version "${PRERELEASE_VERSION}" \
    --control-plane-image "${PRERELEASE_IMAGE}" \
    --source-commit "${SOURCE_COMMIT}" \
    --source-date-epoch "${SOURCE_DATE_EPOCH_VALUE}" \
    --cli-binary "${PRERELEASE_CLI}" \
    --output-dir "${PRERELEASE_OUT}" >/dev/null

PRERELEASE_ROOT_NAME="mem-installer-${PRERELEASE_VERSION}-ubuntu-24.04-amd64"
PRERELEASE_EXTRACTED="${TMP_ROOT}/prerelease-extracted"
mkdir -p "${PRERELEASE_EXTRACTED}"
tar -xzf "${PRERELEASE_OUT}/${PRERELEASE_ROOT_NAME}.tar.gz" -C "${PRERELEASE_EXTRACTED}"
PRERELEASE_BUNDLE="${PRERELEASE_EXTRACTED}/${PRERELEASE_ROOT_NAME}"

(
    # shellcheck disable=SC1090
    source "${PRERELEASE_BUNDLE}/bootstrap/release.env"
    [[ "${MEM_RELEASE_MODE}" == "release" ]]
    [[ "${MEM_RELEASE_VERSION}" == "${PRERELEASE_VERSION}" ]]
    [[ "${MEM_RELEASE_CHANNEL}" == "prerelease" ]]
) && pass "prerelease release.env carries prerelease channel" || fail_test "prerelease release.env carries prerelease channel"

(
    # shellcheck disable=SC1090
    source "${PRERELEASE_BUNDLE}/bootstrap/release.env"
    # shellcheck disable=SC1090
    source "${PRERELEASE_BUNDLE}/bootstrap/lib/release.sh"
    CHANNEL="stable"
    [[ "$(bootstrap_operator_channel)" == "prerelease" ]]
) && pass "packaged prerelease bootstrap reports release provenance instead of mutable stable selector" || fail_test "packaged prerelease bootstrap reports release provenance instead of mutable stable selector"

if grep -Fq -- 'log_info "Channel: $(bootstrap_operator_channel)"' "${PRERELEASE_BUNDLE}/bootstrap/install.sh"; then
    pass "packaged bootstrap uses release-aware Channel output"
else
    fail_test "packaged bootstrap uses release-aware Channel output"
fi

# Packaged release metadata must fail closed if release.env contradicts the
# immutable version. Validation runs before root/Docker work, so this can be
# proven without mutating the test host.
TAMPERED_BUNDLE="${TMP_ROOT}/tampered-prerelease"
cp -a "${PRERELEASE_BUNDLE}" "${TAMPERED_BUNDLE}"
sed -i "s/MEM_RELEASE_CHANNEL='prerelease'/MEM_RELEASE_CHANNEL='stable'/" "${TAMPERED_BUNDLE}/bootstrap/release.env"
set +e
MISMATCH_OUTPUT="$("${TAMPERED_BUNDLE}/bootstrap/install.sh" --dry-run 2>&1)"
MISMATCH_STATUS=$?
set -e
if [[ ${MISMATCH_STATUS} -ne 0 && "${MISMATCH_OUTPUT}" == *"does not match release version '${PRERELEASE_VERSION}'"* && "${MISMATCH_OUTPUT}" == *"Expected: prerelease"* ]]; then
    pass "packaged prerelease bootstrap rejects contradictory stable channel metadata"
else
    fail_test "packaged prerelease bootstrap rejects contradictory stable channel metadata" "${MISMATCH_OUTPUT}"
fi

# Builder refuses mutable/non-digest image input.
set +e
BAD_IMAGE_OUTPUT="$("${BUILDER}" \
    --version "${VERSION}" \
    --control-plane-image ghcr.io/message-easy-mode/mem-control-plane:stable \
    --source-commit "${SOURCE_COMMIT}" \
    --source-date-epoch "${SOURCE_DATE_EPOCH_VALUE}" \
    --cli-binary "${FAKE_CLI}" \
    --output-dir "${TMP_ROOT}/bad" 2>&1)"
BAD_IMAGE_STATUS=$?
set -e
if [[ ${BAD_IMAGE_STATUS} -ne 0 && "${BAD_IMAGE_OUTPUT}" == *"exact version ${VERSION} and sha256 digest"* ]]; then
    pass "installer builder rejects mutable Control Plane image input"
else
    fail_test "installer builder rejects mutable Control Plane image input" "${BAD_IMAGE_OUTPUT}"
fi

printf '\nInstaller bundle tests: %s passed, %s failed\n' "${PASSED}" "${FAILED}"
[[ ${FAILED} -eq 0 ]]
