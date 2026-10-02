#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# shellcheck source=bootstrap/lib/installer.sh
source "${BOOTSTRAP_ROOT}/lib/installer.sh"

fail_test() {
    echo "FAIL: $*" >&2
    exit 1
}

assert_event_order() {
    local expected="$1"
    local actual
    actual="$(IFS='>'; echo "${EVENTS[*]}")"
    [[ "${actual}" == "${expected}" ]] || \
        fail_test "expected event order '${expected}', got '${actual}'"
}

reset_fixture() {
    EVENTS=()
    DRY_RUN=false
    ASSUME_YES=true
    CONTROL_PLANE_RUNTIME_STATE="legacy-container"
    CONTROL_PLANE_VOLUME_NAME="mem-installer-data"
    CONTROL_PLANE_IMAGE="mem-control-plane:local"
    CONTROL_PLANE_PORT="8443"
    CONTROL_PLANE_LEGACY_WAS_RUNNING=true
    CONTROL_PLANE_FAILURE_EVIDENCE_CAPTURED=false
    EVIDENCE_CAPTURED=false
    CONTROL_PLANE_EXISTING_BIND_STATE="loopback"
    CONTROL_PLANE_EXISTING_BIND_ADDRESS="127.0.0.1"
    CONTROL_PLANE_EXISTING_BIND_PORT="8443"
    CONTROL_PLANE_EXISTING_BINDING_COUNT=1
}

bootstrap_set_phase() { :; }
bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
log_info() { :; }
log_warn() { :; }
log_error() { :; }
print_control_plane_launch_plan() { EVENTS+=(plan); }
confirm_or_exit() { EVENTS+=(confirm); }
pull_control_plane_image() { EVENTS+=(pull); }
create_control_plane_volume() { EVENTS+=(volume); }
prepare_setup_token() { EVENTS+=(token); }
prepare_control_plane_host_data_root() { EVENTS+=(host-data); }
ensure_control_plane_certificate() { EVENTS+=(certificate); }
run_new_control_plane_container() { EVENTS+=(run-new); }
capture_control_plane_failure_evidence() {
    if [[ "${EVIDENCE_CAPTURED}" != true ]]; then
        EVENTS+=(capture-evidence)
        EVIDENCE_CAPTURED=true
    fi
}
docker_container_exists() { [[ "$1" == "${CONTROL_PLANE_CONTAINER_NAME}" ]]; }

docker() {
    case "$1:$2:${3:-}" in
        stop:mem-installer:)
            EVENTS+=(stop-legacy)
            ;;
        rm:mem-installer:)
            EVENTS+=(remove-legacy)
            ;;
        rm:-f:mem-control-plane)
            EVENTS+=(remove-canonical)
            ;;
        start:mem-installer:)
            EVENTS+=(restart-legacy)
            ;;
        *)
            fail_test "unexpected fake docker invocation: $*"
            ;;
    esac
}

reset_fixture
wait_for_control_plane_health() { EVENTS+=(health-pass); return 0; }
fail() { fail_test "$*"; }
migrate_legacy_control_plane
assert_event_order "plan>confirm>pull>volume>token>host-data>certificate>stop-legacy>run-new>health-pass>remove-legacy"

reset_fixture
wait_for_control_plane_health() { EVENTS+=(health-fail); return 1; }
FAIL_MESSAGE=""
fail() { FAIL_MESSAGE="$*"; return 1; }
set +e
migrate_legacy_control_plane
status=$?
set -e

[[ ${status} -ne 0 ]] || fail_test "failed health verification unexpectedly succeeded"
[[ "${FAIL_MESSAGE}" == *"Legacy state was preserved"* ]] || \
    fail_test "rollback failure message did not confirm preserved legacy state"
assert_event_order "plan>confirm>pull>volume>token>host-data>certificate>stop-legacy>run-new>health-fail>capture-evidence>remove-canonical>restart-legacy"

# A failed migration from an already-unsafe legacy runtime must preserve its
# container/state for recovery without automatically reopening that exposure.
reset_fixture
CONTROL_PLANE_EXISTING_BIND_STATE="wildcard"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="0.0.0.0"
wait_for_control_plane_health() { EVENTS+=(health-fail); return 1; }
FAIL_MESSAGE=""
fail() { FAIL_MESSAGE="$*"; return 1; }
set +e
migrate_legacy_control_plane
status=$?
set -e

[[ ${status} -ne 0 ]] || fail_test "failed unsafe legacy migration unexpectedly succeeded"
[[ "${FAIL_MESSAGE}" == *"Legacy state was preserved"* ]] ||     fail_test "unsafe legacy rollback failure message did not confirm preserved state"
assert_event_order "plan>confirm>pull>volume>token>host-data>certificate>stop-legacy>run-new>health-fail>capture-evidence>remove-canonical"

echo "PASS: Control Plane legacy migration, private-boundary preservation, and fail-closed rollback orchestration"
