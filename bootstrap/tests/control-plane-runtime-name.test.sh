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

assert_eq() {
    local expected="$1"
    local actual="$2"
    local label="$3"

    [[ "${actual}" == "${expected}" ]] || \
        fail_test "${label}: expected '${expected}', got '${actual}'"
}

assert_contains() {
    local file="$1"
    local expected="$2"

    grep -Fq -- "${expected}" "${file}" || \
        fail_test "${file} does not contain expected text: ${expected}"
}

assert_not_contains() {
    local file="$1"
    local unexpected="$2"

    if grep -Fq -- "${unexpected}" "${file}"; then
        fail_test "${file} contains forbidden text: ${unexpected}"
    fi
}

assert_eq "mem-control-plane" "${CONTROL_PLANE_CONTAINER_NAME}" "canonical container"
assert_eq "mem-control-plane-data" "${CONTROL_PLANE_FRESH_VOLUME_NAME}" "canonical volume"
assert_eq "ghcr.io/message-easy-mode/mem-control-plane:stable" "${CONTROL_PLANE_IMAGE_STABLE}" "stable image"
assert_eq "ghcr.io/message-easy-mode/mem-control-plane:dev" "${CONTROL_PLANE_IMAGE_DEV}" "development image"
assert_eq "mem-control-plane:local" "${CONTROL_PLANE_IMAGE_LOCAL}" "local image"
assert_eq "mem-installer" "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" "legacy container"
assert_eq "mem-control-plane-dev" "${DEVELOPMENT_CONTROL_PLANE_CONTAINER_PREFIX}" "development container prefix"
assert_eq "mem-installer-data" "${LEGACY_CONTROL_PLANE_VOLUME_NAME}" "legacy volume"
assert_eq "/var/lib/message-easy-mode" "${CONTROL_PLANE_HOST_DATA_ROOT}" "production host-data root"
assert_eq "/var/lib/message-easy-mode/mem-data" "${CONTROL_PLANE_MEM_DATA_ROOT}" "production MEM data root"
assert_eq "/var/lib/message-easy-mode/instances" "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" "production instance data root"
assert_eq "/var/lib/message-easy-mode/platform/coturn" "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" "Coturn storage root"
assert_eq "/var/lib/message-easy-mode/seq" "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}" "Seq host-data root"

assert_eq "fresh" "$(classify_control_plane_state false false false false)" "fresh classification"
assert_eq "canonical-container" "$(classify_control_plane_state true false true false)" "canonical classification"
assert_eq "legacy-container" "$(classify_control_plane_state false true false true)" "legacy classification"
assert_eq "container-conflict" "$(classify_control_plane_state true true true true)" "container conflict"
assert_eq "volume-conflict" "$(classify_control_plane_state false false true true)" "volume conflict"
assert_eq "legacy-volume-only" "$(classify_control_plane_state false false false true)" "legacy volume recovery classification"
assert_eq "canonical-volume-only" "$(classify_control_plane_state false false true false)" "canonical volume recovery classification"

if ! is_development_control_plane_container_name "mem-control-plane-dev"; then
    fail_test "canonical development container was not recognized"
fi
if ! is_development_control_plane_container_name "mem-control-plane-dev-e2e"; then
    fail_test "scoped development container was not recognized"
fi
if is_development_control_plane_container_name "mem-control-plane"; then
    fail_test "production container was misclassified as development"
fi

select_certificate_paths_for_volume "${CONTROL_PLANE_FRESH_VOLUME_NAME}"
assert_eq "/data/certs/mem-control-plane.crt" "${CONTROL_PLANE_CERT_PATH}" "canonical certificate"
assert_eq "/data/certs/mem-control-plane.key" "${CONTROL_PLANE_KEY_PATH}" "canonical key"
select_certificate_paths_for_volume "${LEGACY_CONTROL_PLANE_VOLUME_NAME}"
assert_eq "/data/certs/mem-installer.crt" "${CONTROL_PLANE_CERT_PATH}" "legacy certificate compatibility"
assert_eq "/data/certs/mem-installer.key" "${CONTROL_PLANE_KEY_PATH}" "legacy key compatibility"

assert_contains "${BOOTSTRAP_ROOT}/install.sh" "--control-plane-image mem-control-plane:local"
assert_contains "${BOOTSTRAP_ROOT}/install.sh" "--installer-image"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "MEM_CONTROL_PLANE_SETUP_TOKEN_PATH"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "MEM_CONTROL_PLANE_HOST_IPV4"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'CONTROL_PLANE_HOST_DATA_ROOT="${MEM_CONTROL_PLANE_HOST_DATA_ROOT:-/var/lib/message-easy-mode}"'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" '-v "${CONTROL_PLANE_HOST_DATA_ROOT}:${CONTROL_PLANE_HOST_DATA_ROOT}"'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'MEM_CONTROL_PLANE_HOST_DATA_ROOT=${CONTROL_PLANE_HOST_DATA_ROOT}'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'MEM_DATA_ROOT=${CONTROL_PLANE_MEM_DATA_ROOT}'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'Provisioning__InstanceDataRoot=${CONTROL_PLANE_INSTANCE_DATA_ROOT}'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'Coturn__StorageRootPath=${CONTROL_PLANE_COTURN_STORAGE_ROOT}'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" 'Diagnostics__Seq__HostDataPath=${CONTROL_PLANE_SEQ_HOST_DATA_PATH}'
assert_not_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" '/home/master'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "migrate_canonical_control_plane_host_data_mount"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "rollback_canonical_host_data_migration"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "MEM_RUNTIME_MODE=containerized-production"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "MemRuntime__ContainerName="
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "io.message-easy-mode.managed=true"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "io.message-easy-mode.runtime-mode=containerized-production"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "io.message-easy-mode.resource=control-plane"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "io.message-easy-mode.control-plane-access="
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "io.message-easy-mode.control-plane-bind-address="
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "control-plane-access.sh"
assert_contains "${BOOTSTRAP_ROOT}/lib/control-plane-access.sh" 'CONTROL_PLANE_ACCESS_MODE="${CONTROL_PLANE_ACCESS_MODE:-ssh-tunnel}"'
assert_contains "${BOOTSTRAP_ROOT}/lib/control-plane-access.sh" 'CONTROL_PLANE_BIND_ADDRESS="${CONTROL_PLANE_BIND_ADDRESS:-127.0.0.1}"'
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" '-p "${publish_spec}"'
assert_contains "${BOOTSTRAP_ROOT}/install.sh" "--control-plane-access <ssh|trusted-lan>"
assert_contains "${BOOTSTRAP_ROOT}/install.sh" "--control-plane-bind-address <ipv4>"
assert_not_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" '-p "${CONTROL_PLANE_PORT}:${CONTROL_PLANE_CONTAINER_HTTPS_PORT}"'
assert_not_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "https://<public-server-ip>:"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "wait_for_control_plane_health"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "/health/live"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "/health/ready"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "rollback_legacy_migration"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "development_control_plane_containers"
assert_contains "${BOOTSTRAP_ROOT}/lib/installer.sh" "supported developer harness"
assert_not_contains "${BOOTSTRAP_ROOT}/install.sh" "ghcr.io/matrix-easy-mode/mem-installer"

bash -n "${BOOTSTRAP_ROOT}/install.sh"
bash -n "${BOOTSTRAP_ROOT}/lib/control-plane-access.sh"
bash -n "${BOOTSTRAP_ROOT}/lib/installer.sh"

echo "PASS: Control Plane bootstrap identity and migration contract"
