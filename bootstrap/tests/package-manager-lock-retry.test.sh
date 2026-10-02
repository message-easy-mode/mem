#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# shellcheck source=bootstrap/lib/packages.sh
source "${BOOTSTRAP_ROOT}/lib/packages.sh"

fail_test() {
    echo "FAIL: $*" >&2
    exit 1
}

assert_contains_text() {
    local value="$1"
    local expected="$2"
    [[ "${value}" == *"${expected}"* ]] || fail_test "expected '${expected}' in output"
}

log_info() { echo "[INFO] $*"; }
log_warn() { echo "[WARN] $*" >&2; }
log_error() { echo "[ERROR] $*" >&2; }

# Keep retry tests instantaneous.
APT_LOCK_RETRY_ATTEMPTS=3
APT_LOCK_RETRY_DELAY_SECONDS=0
sleep() { :; }

# 1. Transient dpkg/frontend lock contention is retried and then succeeds.
CALL_COUNT_FILE="$(mktemp "${TMPDIR:-/tmp}/mem-apt-retry-count.XXXXXX")"
trap 'rm -f -- "${CALL_COUNT_FILE}"' EXIT
printf '0\n' > "${CALL_COUNT_FILE}"
apt-get() {
    local calls
    calls="$(( $(cat "${CALL_COUNT_FILE}") + 1 ))"
    printf '%s\n' "${calls}" > "${CALL_COUNT_FILE}"
    if (( calls == 1 )); then
        echo "E: Could not get lock /var/lib/dpkg/lock-frontend. It is held by process 2144 (unattended-upgr)" >&2
        echo "E: Unable to acquire the dpkg frontend lock (/var/lib/dpkg/lock-frontend), is another process using it?" >&2
        return 100
    fi
    echo "apt success"
    return 0
}

RETRY_OUTPUT="$(run_apt_get_with_lock_retry update 2>&1)"
APT_CALLS="$(cat "${CALL_COUNT_FILE}")"
[[ "${APT_CALLS}" -eq 2 ]] || fail_test "transient lock path called apt-get ${APT_CALLS} times instead of 2"
assert_contains_text "${RETRY_OUTPUT}" "Ubuntu package manager is busy"
assert_contains_text "${RETRY_OUTPUT}" "apt success"

# 2. A non-lock apt failure is returned immediately and is not retried.
printf '0\n' > "${CALL_COUNT_FILE}"
apt-get() {
    local calls
    calls="$(( $(cat "${CALL_COUNT_FILE}") + 1 ))"
    printf '%s\n' "${calls}" > "${CALL_COUNT_FILE}"
    echo "E: The repository 'https://example.invalid stable Release' does not have a Release file." >&2
    return 100
}

set +e
NON_LOCK_OUTPUT="$(run_apt_get_with_lock_retry update 2>&1)"
NON_LOCK_STATUS=$?
set -e
[[ "${NON_LOCK_STATUS}" -eq 100 ]] || fail_test "non-lock failure returned ${NON_LOCK_STATUS} instead of 100"
APT_CALLS="$(cat "${CALL_COUNT_FILE}")"
[[ "${APT_CALLS}" -eq 1 ]] || fail_test "non-lock failure was retried ${APT_CALLS} times"
assert_contains_text "${NON_LOCK_OUTPUT}" "does not have a Release file"

# 3. Persistent lock contention is bounded and returns the apt failure.
printf '0\n' > "${CALL_COUNT_FILE}"
apt-get() {
    local calls
    calls="$(( $(cat "${CALL_COUNT_FILE}") + 1 ))"
    printf '%s\n' "${calls}" > "${CALL_COUNT_FILE}"
    echo "E: Could not get lock /var/lib/dpkg/lock-frontend" >&2
    return 100
}

set +e
BOUNDED_OUTPUT="$(run_apt_get_with_lock_retry install -y curl 2>&1)"
BOUNDED_STATUS=$?
set -e
[[ "${BOUNDED_STATUS}" -eq 100 ]] || fail_test "bounded lock failure returned ${BOUNDED_STATUS} instead of 100"
APT_CALLS="$(cat "${CALL_COUNT_FILE}")"
[[ "${APT_CALLS}" -eq 3 ]] || fail_test "bounded lock path called apt-get ${APT_CALLS} times instead of 3"
assert_contains_text "${BOUNDED_OUTPUT}" "Ubuntu package manager remained busy after 3 attempts"

# 4. All bootstrap-owned apt operations must flow through the retry helper.
RAW_DOCKER_APT="$(grep -nE '(^|[[:space:]])(DEBIAN_FRONTEND=noninteractive[[:space:]]+)?apt-get[[:space:]]' "${BOOTSTRAP_ROOT}/lib/docker.sh" || true)"
[[ -z "${RAW_DOCKER_APT}" ]] || fail_test "docker.sh still contains raw apt-get calls: ${RAW_DOCKER_APT}"

bash -n "${BOOTSTRAP_ROOT}/lib/packages.sh"
bash -n "${BOOTSTRAP_ROOT}/lib/docker.sh"

echo "PASS: apt/dpkg lock contention is retried with bounded failure and Docker installs use the helper"
