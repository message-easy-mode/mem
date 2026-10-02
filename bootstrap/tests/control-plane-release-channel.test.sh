#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# shellcheck source=bootstrap/lib/release.sh
source "${BOOTSTRAP_ROOT}/lib/release.sh"

fail_test() {
    echo "FAIL: $*" >&2
    exit 1
}

assert_eq() {
    local expected="$1"
    local actual="$2"
    local label="$3"
    [[ "${actual}" == "${expected}" ]] || \
        fail_test "${label}: expected '${expected}', got '${actual}'"
}

assert_valid() {
    local version="$1"
    local channel="$2"
    local label="$3"
    mem_release_channel_contract_is_valid "${version}" "${channel}" || \
        fail_test "${label}: expected valid release channel contract"
}

assert_invalid() {
    local version="$1"
    local channel="$2"
    local label="$3"
    if mem_release_channel_contract_is_valid "${version}" "${channel}"; then
        fail_test "${label}: expected invalid release channel contract"
    fi
}

# Stable and prerelease identity is derived only from the immutable MEM version.
assert_eq "stable" "$(mem_release_channel_for_version '0.2.0')" "stable release derivation"
assert_eq "prerelease" "$(mem_release_channel_for_version '0.2.0-rc.5.qa.5')" "prerelease release derivation"

assert_valid "0.2.0" "stable" "stable release contract"
assert_valid "0.2.0-rc.5.qa.5" "prerelease" "prerelease release contract"
assert_invalid "0.2.0" "prerelease" "stable version rejects prerelease channel"
assert_invalid "0.2.0-rc.5.qa.5" "stable" "prerelease version rejects stable channel"
assert_invalid "0.2.0-rc.5.qa.5" "" "release rejects missing channel"
assert_invalid "0.2.0-rc.5.qa.5" "dev" "release rejects development channel"

# Development keeps the mutable image selector behavior.
MEM_RELEASE_MODE="development"
MEM_RELEASE_CHANNEL=""
CHANNEL="stable"
assert_eq "stable" "$(bootstrap_operator_channel)" "development stable display"
CHANNEL="dev"
assert_eq "dev" "$(bootstrap_operator_channel)" "development dev display"

# Packaged releases display packaged release provenance and do not mislabel a
# prerelease as the bootstrap's default mutable image channel.
MEM_RELEASE_MODE="release"
CHANNEL="stable"
MEM_RELEASE_VERSION="0.2.0-rc.5.qa.5"
MEM_RELEASE_CHANNEL="prerelease"
assert_eq "prerelease" "$(bootstrap_operator_channel)" "packaged prerelease display"

MEM_RELEASE_VERSION="0.2.0"
MEM_RELEASE_CHANNEL="stable"
assert_eq "stable" "$(bootstrap_operator_channel)" "packaged stable display"

# The executable bootstrap must use the release-aware display helper.
grep -Fq -- 'log_info "Channel: $(bootstrap_operator_channel)"' "${BOOTSTRAP_ROOT}/install.sh" || \
    fail_test "bootstrap does not use release-aware operator channel output"
grep -Fq -- 'mem_release_channel_contract_is_valid "${MEM_RELEASE_VERSION}" "${MEM_RELEASE_CHANNEL}"' "${BOOTSTRAP_ROOT}/install.sh" || \
    fail_test "bootstrap does not validate packaged release-channel provenance"

bash -n "${BOOTSTRAP_ROOT}/lib/release.sh"
bash -n "${BOOTSTRAP_ROOT}/install.sh"

echo "PASS: packaged release channel is validated and reported independently from the mutable image selector"
