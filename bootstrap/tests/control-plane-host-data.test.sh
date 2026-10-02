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

assert_contains_text() {
    local value="$1"
    local expected="$2"
    [[ "${value}" == *"${expected}"* ]] || \
        fail_test "expected '${expected}' in '${value}'"
}

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-bootstrap-host-data-test.XXXXXX")"
trap 'rm -rf -- "${TMP_ROOT}"' EXIT

# 1. One canonical root derives every production host-data child.
CONTROL_PLANE_HOST_DATA_ROOT="${TMP_ROOT}/custom-host-data/"
validate_control_plane_host_data_root

[[ "${CONTROL_PLANE_HOST_DATA_ROOT}" == "${TMP_ROOT}/custom-host-data" ]] || \
    fail_test "host-data root trailing slash was not normalized"
[[ "${CONTROL_PLANE_MEM_DATA_ROOT}" == "${CONTROL_PLANE_HOST_DATA_ROOT}/mem-data" ]] || \
    fail_test "MEM data root was not derived from the canonical host-data root"
[[ "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" == "${CONTROL_PLANE_HOST_DATA_ROOT}/instances" ]] || \
    fail_test "instance data root was not derived from the canonical host-data root"
[[ "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" == "${CONTROL_PLANE_HOST_DATA_ROOT}/platform/coturn" ]] || \
    fail_test "Coturn data root was not derived from the canonical host-data root"
[[ "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}" == "${CONTROL_PLANE_HOST_DATA_ROOT}/seq" ]] || \
    fail_test "Seq data root was not derived from the canonical host-data root"

# 2. Host-data preparation creates every canonical child through one bounded
# owner-only install operation.
INSTALL_ARGS_FILE="${TMP_ROOT}/install-args"
DRY_RUN=false
bootstrap_set_phase() { :; }
bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
install() {
    printf '%s\n' "$@" > "${INSTALL_ARGS_FILE}"
}
prepare_control_plane_host_data_root
INSTALL_ARGS="$(cat "${INSTALL_ARGS_FILE}")"
assert_contains_text "${INSTALL_ARGS}" "0700"
assert_contains_text "${INSTALL_ARGS}" "${CONTROL_PLANE_HOST_DATA_ROOT}"
assert_contains_text "${INSTALL_ARGS}" "${CONTROL_PLANE_MEM_DATA_ROOT}"
assert_contains_text "${INSTALL_ARGS}" "${CONTROL_PLANE_INSTANCE_DATA_ROOT}"
assert_contains_text "${INSTALL_ARGS}" "${CONTROL_PLANE_COTURN_STORAGE_ROOT}"
assert_contains_text "${INSTALL_ARGS}" "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
unset -f install

# 3. Existing canonical runtime validation requires both the same-path bind and
# the complete production environment contract.
OMIT_INSTANCE_ENV=false
docker() {
    if [[ "$1" != "inspect" ]]; then
        return 1
    fi

    case "$*" in
        *'.Mounts'*)
            printf 'bind|%s|%s\n' \
                "${CONTROL_PLANE_HOST_DATA_ROOT}" \
                "${CONTROL_PLANE_HOST_DATA_ROOT}"
            ;;
        *'.Config.Env'*)
            printf 'MEM_CONTROL_PLANE_HOST_DATA_ROOT=%s\n' "${CONTROL_PLANE_HOST_DATA_ROOT}"
            printf 'MEM_DATA_ROOT=%s\n' "${CONTROL_PLANE_MEM_DATA_ROOT}"
            if [[ "${OMIT_INSTANCE_ENV}" != true ]]; then
                printf 'Provisioning__InstanceDataRoot=%s\n' "${CONTROL_PLANE_INSTANCE_DATA_ROOT}"
            fi
            printf 'Coturn__StorageRootPath=%s\n' "${CONTROL_PLANE_COTURN_STORAGE_ROOT}"
            printf 'Diagnostics__Seq__HostDataPath=%s\n' "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
            ;;
        *)
            return 1
            ;;
    esac
}

container_has_required_host_data_contract "mem-control-plane" || \
    fail_test "complete same-path host-data contract was not accepted"

OMIT_INSTANCE_ENV=true
if container_has_required_host_data_contract "mem-control-plane"; then
    fail_test "incomplete production host-data environment unexpectedly passed"
fi
OMIT_INSTANCE_ENV=false

# 4. The canonical container launch includes the same-path mount and all
# production host-data environment values.
mkdir -p \
    "${CONTROL_PLANE_MEM_DATA_ROOT}" \
    "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" \
    "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" \
    "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
DRY_RUN=false
CONTROL_PLANE_IMAGE="mem-control-plane:test"
CONTROL_PLANE_PORT="8443"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
CHANNEL="dev"
CONTROL_PLANE_CERT_PATH="/data/certs/mem-control-plane.crt"
CONTROL_PLANE_KEY_PATH="/data/certs/mem-control-plane.key"
DOCKER_ARGS_FILE="${TMP_ROOT}/docker-run-args"
NETWORK_EVENTS_FILE="${TMP_ROOT}/network-events"

bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
log_info() { :; }
log_error() { :; }
docker_container_exists() { return 1; }
docker() {
    if [[ "$1" == "run" ]]; then
        printf '%s\n' "$@" > "${DOCKER_ARGS_FILE}"
        return 0
    fi
    if [[ "$1:${2:-}:${3:-}" == "network:inspect:${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}" ]]; then
        return 0
    fi
    if [[ "$1:${2:-}:${3:-}:${4:-}" == "network:connect:${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}:${CONTROL_PLANE_CONTAINER_NAME}" ]]; then
        printf '%s\n' "$*" >> "${NETWORK_EVENTS_FILE}"
        return 0
    fi
    return 1
}

run_new_control_plane_container
DOCKER_ARGS="$(cat "${DOCKER_ARGS_FILE}")"
assert_contains_text "${DOCKER_ARGS}" "${CONTROL_PLANE_HOST_DATA_ROOT}:${CONTROL_PLANE_HOST_DATA_ROOT}"
assert_contains_text "${DOCKER_ARGS}" "MEM_CONTROL_PLANE_HOST_DATA_ROOT=${CONTROL_PLANE_HOST_DATA_ROOT}"
assert_contains_text "${DOCKER_ARGS}" "MEM_DATA_ROOT=${CONTROL_PLANE_MEM_DATA_ROOT}"
assert_contains_text "${DOCKER_ARGS}" "Provisioning__InstanceDataRoot=${CONTROL_PLANE_INSTANCE_DATA_ROOT}"
assert_contains_text "${DOCKER_ARGS}" "Coturn__StorageRootPath=${CONTROL_PLANE_COTURN_STORAGE_ROOT}"
assert_contains_text "${DOCKER_ARGS}" "Diagnostics__Seq__HostDataPath=${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
assert_contains_text "${DOCKER_ARGS}" "MEM_RUNTIME_MODE=containerized-production"
if [[ "${DOCKER_ARGS}" == *"/home/master"* ]]; then
    fail_test "canonical production launch retained a developer-home path"
fi
NETWORK_EVENTS="$(cat "${NETWORK_EVENTS_FILE}")"
assert_contains_text "${NETWORK_EVENTS}" "network connect ${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME} ${CONTROL_PLANE_CONTAINER_NAME}"

# 5. A fresh/recreated Control Plane does not require the managed gateway when
# the platform network has not been created yet.
rm -f "${NETWORK_EVENTS_FILE}"
docker() {
    if [[ "$1" == "run" ]]; then
        printf '%s\n' "$@" > "${DOCKER_ARGS_FILE}"
        return 0
    fi
    if [[ "$1:${2:-}:${3:-}" == "network:inspect:${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}" ]]; then
        return 1
    fi
    if [[ "$1" == "network" && "${2:-}" == "connect" ]]; then
        fail_test "managed gateway connect was attempted when the network did not exist"
    fi
    return 1
}
run_new_control_plane_container

# 6. Reviewed canonical migration retains the old container until the new one is healthy.
EVENTS=()
CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=true
CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME="mem-control-plane-pre-host-data-migration"
CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=true

print_control_plane_launch_plan() { EVENTS+=(plan); }
confirm_or_exit() { EVENTS+=(confirm); }
prepare_control_plane_host_data_root() { EVENTS+=(host-data); }
pull_control_plane_image() { EVENTS+=(pull); }
prepare_setup_token() { EVENTS+=(token); }
ensure_control_plane_certificate() { EVENTS+=(certificate); }
run_new_control_plane_container() { EVENTS+=(run-new); }
wait_for_control_plane_health() { EVENTS+=(health); return 0; }
capture_control_plane_failure_evidence() { EVENTS+=(capture); }
bootstrap_set_phase() { :; }
bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
log_warn() { :; }
log_error() { :; }
fail() { fail_test "$*"; }

docker_container_exists() { return 1; }
docker() {
    case "$1:${2:-}:${3:-}" in
        stop:mem-control-plane:) EVENTS+=(stop-old) ;;
        rename:mem-control-plane:mem-control-plane-pre-host-data-migration) EVENTS+=(retain-old) ;;
        rm:mem-control-plane-pre-host-data-migration:) EVENTS+=(retire-old) ;;
        *) fail_test "unexpected migration docker invocation: $*" ;;
    esac
}

migrate_canonical_control_plane_host_data_mount
ACTUAL="$(IFS='>'; echo "${EVENTS[*]}")"
EXPECTED="plan>confirm>host-data>pull>token>certificate>stop-old>retain-old>run-new>health>retire-old"
[[ "${ACTUAL}" == "${EXPECTED}" ]] || \
    fail_test "migration order expected '${EXPECTED}', got '${ACTUAL}'"

# 7. A failed replacement removes the new runtime and restores the retained one.
EVENTS=()
BACKUP_EXISTS=false
NEW_EXISTS=false
CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=true

run_new_control_plane_container() { EVENTS+=(run-new); NEW_EXISTS=true; }
wait_for_control_plane_health() { EVENTS+=(health-fail); return 1; }
fail() { EVENTS+=(failed); return 1; }

docker_container_exists() {
    case "$1" in
        mem-control-plane) [[ "${NEW_EXISTS}" == true ]] ;;
        mem-control-plane-pre-host-data-migration) [[ "${BACKUP_EXISTS}" == true ]] ;;
        *) return 1 ;;
    esac
}

docker() {
    case "$1:${2:-}:${3:-}" in
        stop:mem-control-plane:)
            EVENTS+=(stop-old)
            ;;
        rename:mem-control-plane:mem-control-plane-pre-host-data-migration)
            EVENTS+=(retain-old)
            BACKUP_EXISTS=true
            ;;
        rm:-f:mem-control-plane)
            EVENTS+=(remove-new)
            NEW_EXISTS=false
            ;;
        rename:mem-control-plane-pre-host-data-migration:mem-control-plane)
            EVENTS+=(restore-old)
            BACKUP_EXISTS=false
            ;;
        start:mem-control-plane:)
            EVENTS+=(restart-old)
            ;;
        *) fail_test "unexpected rollback docker invocation: $*" ;;
    esac
}

set +e
migrate_canonical_control_plane_host_data_mount
STATUS=$?
set -e
[[ ${STATUS} -ne 0 ]] || fail_test "failed host-data migration unexpectedly succeeded"
ACTUAL="$(IFS='>'; echo "${EVENTS[*]}")"
EXPECTED="plan>confirm>host-data>pull>token>certificate>stop-old>retain-old>run-new>health-fail>capture>remove-new>restore-old>restart-old>failed"
[[ "${ACTUAL}" == "${EXPECTED}" ]] || \
    fail_test "rollback order expected '${EXPECTED}', got '${ACTUAL}'"

echo "PASS: Control Plane canonical host-data derivation, environment, launch, migration, and rollback contract"
