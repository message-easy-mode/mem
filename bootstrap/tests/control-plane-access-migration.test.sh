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

assert_true() {
    local actual="$1"
    local label="$2"
    [[ "${actual}" == true ]] || fail_test "${label}: expected true, got '${actual}'"
}

assert_false() {
    local actual="$1"
    local label="$2"
    [[ "${actual}" == false ]] || fail_test "${label}: expected false, got '${actual}'"
}

bootstrap_set_phase() { :; }
bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
log_info() { :; }
log_warn() { :; }
log_error() { :; }
fail() {
    echo "FAIL-CALLED: $*" >&2
    return 1
}

CONTROL_PLANE_PORT="8443"
ASSUME_YES=true
DRY_RUN=false

# Address classification is based on the real host publication, not labels.
host_ipv4_interface_for_address() {
    case "$1" in
        192.168.10.20) printf '%s\n' 'ens18' ;;
        10.10.0.193) printf '%s\n' 'vmbr0' ;;
        *) return 1 ;;
    esac
}

assert_eq "wildcard" "$(classify_control_plane_host_bind_address '0.0.0.0')" "IPv4 wildcard classification"
assert_eq "wildcard" "$(classify_control_plane_host_bind_address '::')" "IPv6 wildcard classification"
assert_eq "loopback" "$(classify_control_plane_host_bind_address '127.0.0.1')" "loopback classification"
assert_eq "loopback-noncanonical" "$(classify_control_plane_host_bind_address '::1')" "IPv6 loopback classification"
assert_eq "trusted-lan" "$(classify_control_plane_host_bind_address '192.168.10.20')" "assigned RFC1918 classification"
assert_eq "trusted-lan-unassigned" "$(classify_control_plane_host_bind_address '192.168.10.99')" "stale RFC1918 classification"
assert_eq "nonprivate-ipv4" "$(classify_control_plane_host_bind_address '8.8.8.8')" "public IPv4 classification"

# Docker NetworkSettings.Ports is the authority for an installed runtime. The
# inspection path must preserve HostIp rather than reducing the mapping to a port.
docker() {
    if [[ "$1" == "inspect" ]]; then
        printf '%s\n' '0.0.0.0|8443'
        return 0
    fi
    return 1
}
inspect_existing_control_plane_binding "mem-control-plane"
assert_eq "wildcard" "${CONTROL_PLANE_EXISTING_BIND_STATE}" "inspected wildcard binding state"
assert_eq "0.0.0.0" "${CONTROL_PLANE_EXISTING_BIND_ADDRESS}" "inspected wildcard HostIp"
assert_eq "8443" "${CONTROL_PLANE_EXISTING_BIND_PORT}" "inspected wildcard host port"
assert_eq "1" "${CONTROL_PLANE_EXISTING_BINDING_COUNT}" "inspected binding count"

# Safe existing loopback is preserved automatically when no new access mode was
# explicitly requested.
CONTROL_PLANE_EXISTING_BIND_STATE="loopback"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
resolve_canonical_control_plane_access_policy
assert_eq "ssh-tunnel" "${CONTROL_PLANE_ACCESS_MODE}" "preserved loopback access mode"
assert_eq "127.0.0.1" "${CONTROL_PLANE_BIND_ADDRESS}" "preserved loopback bind"
assert_eq "https://127.0.0.1:8443" "$(control_plane_canonical_private_origin)" "preserved loopback browser authority"
assert_false "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" "safe loopback migration"

# Safe existing Trusted LAN binding is preserved without making the operator
# reselect it every time bootstrap is rerun.
CONTROL_PLANE_EXISTING_BIND_STATE="trusted-lan"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
resolve_canonical_control_plane_access_policy
assert_eq "trusted-lan" "${CONTROL_PLANE_ACCESS_MODE}" "preserved Trusted LAN access mode"
assert_eq "192.168.10.20" "${CONTROL_PLANE_BIND_ADDRESS}" "preserved Trusted LAN bind"
assert_eq "https://192.168.10.20:8443" "$(control_plane_canonical_private_origin)" "preserved Trusted LAN browser authority"
assert_false "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" "safe Trusted LAN migration"

# Runtime-name migration preserves a safe legacy administration boundary too;
# changing the permanent container name must not unexpectedly change how an
# on-prem operator reaches MEM.
CONTROL_PLANE_EXISTING_BIND_STATE="trusted-lan"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
resolve_legacy_control_plane_access_policy
assert_eq "trusted-lan" "${CONTROL_PLANE_ACCESS_MODE}" "legacy Trusted LAN preserved mode"
assert_eq "192.168.10.20" "${CONTROL_PLANE_BIND_ADDRESS}" "legacy Trusted LAN preserved address"
assert_eq "https://192.168.10.20:8443" "$(control_plane_canonical_private_origin)" "legacy Trusted LAN browser authority"

# An unsafe legacy wildcard is not preserved during runtime-name migration.
# Unattended migration chooses loopback rather than carrying the exposure into
# the canonical Control Plane.
CONTROL_PLANE_EXISTING_BIND_STATE="wildcard"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="0.0.0.0"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
ASSUME_YES=true
resolve_legacy_control_plane_access_policy
assert_eq "ssh-tunnel" "${CONTROL_PLANE_ACCESS_MODE}" "legacy wildcard fallback mode"
assert_eq "127.0.0.1" "${CONTROL_PLANE_BIND_ADDRESS}" "legacy wildcard fallback address"

# Wildcard/public exposure is never grandfathered. Unattended operation selects
# the safe SSH/loopback target and requires reviewed recreation.
CONTROL_PLANE_EXISTING_BIND_STATE="wildcard"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="0.0.0.0"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
ASSUME_YES=true
resolve_canonical_control_plane_access_policy
assert_eq "ssh-tunnel" "${CONTROL_PLANE_ACCESS_MODE}" "unsafe binding fallback mode"
assert_eq "127.0.0.1" "${CONTROL_PLANE_BIND_ADDRESS}" "unsafe binding fallback address"
assert_true "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" "wildcard migration requirement"

# A safe existing boundary can be changed deliberately through the CLI, but it
# still goes through the same reviewed recreation rather than in-place mutation.
CONTROL_PLANE_EXISTING_BIND_STATE="trusted-lan"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_EXISTING_BINDING_COUNT=1
CONTROL_PLANE_ACCESS_EXPLICIT=true
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
resolve_canonical_control_plane_access_policy
assert_true "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" "explicit Trusted LAN to SSH migration"

# Exact post-recreation Docker HostIp is a release gate.
container_published_bindings() { printf '%s\n' '127.0.0.1|8443'; }
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_PORT="8443"
verify_control_plane_host_binding "mem-control-plane" || fail_test "exact loopback binding verification failed"
container_published_bindings() { printf '%s\n' '0.0.0.0|8443'; }
if verify_control_plane_host_binding "mem-control-plane"; then
    fail_test "wildcard binding unexpectedly passed exact HostIp verification"
fi
container_published_bindings() { printf '%s\n' '127.0.0.1|8443' '::|8443'; }
if verify_control_plane_host_binding "mem-control-plane"; then
    fail_test "multiple host bindings unexpectedly passed verification"
fi

# Successful canonical hardening keeps the previous container as rollback until
# replacement health AND exact binding have passed.
EVENTS=()
BACKUP_EXISTS=false
NEW_EXISTS=false
CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=true
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=true
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON="existing wildcard binding"
CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=true
CONTROL_PLANE_EXISTING_BIND_STATE="wildcard"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="0.0.0.0"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_PORT="8443"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
CONTROL_PLANE_HOST_DATA_ROOT="/var/lib/message-easy-mode"
CONTROL_PLANE_COTURN_STORAGE_ROOT="/var/lib/message-easy-mode/platform/coturn"
DRY_RUN=false

print_control_plane_launch_plan() { EVENTS+=(plan); }
confirm_or_exit() { EVENTS+=(confirm); }
prepare_control_plane_host_data_root() { EVENTS+=(host-data); }
pull_control_plane_image() { EVENTS+=(pull); }
prepare_setup_token() { EVENTS+=(token); }
ensure_control_plane_certificate() { EVENTS+=(certificate); }
run_new_control_plane_container() { EVENTS+=(run-new); NEW_EXISTS=true; }
wait_for_control_plane_health() { EVENTS+=(health); return 0; }
verify_control_plane_host_binding() { EVENTS+=(binding); return 0; }
capture_control_plane_failure_evidence() { EVENTS+=(capture); }

# Migration backup checks see no previous backup until the old runtime is renamed.
docker_container_exists() {
    case "$1" in
        mem-control-plane) [[ "${NEW_EXISTS}" == true ]] ;;
        mem-control-plane-pre-private-admin-migration) [[ "${BACKUP_EXISTS}" == true ]] ;;
        mem-control-plane-pre-host-data-migration) return 1 ;;
        *) return 1 ;;
    esac
}

docker() {
    case "$1:${2:-}:${3:-}" in
        stop:mem-control-plane:) EVENTS+=(stop-old) ;;
        rename:mem-control-plane:mem-control-plane-pre-private-admin-migration)
            EVENTS+=(retain-old)
            BACKUP_EXISTS=true
            ;;
        rm:mem-control-plane-pre-private-admin-migration:)
            EVENTS+=(retire-old)
            BACKUP_EXISTS=false
            ;;
        *) fail_test "unexpected successful migration docker invocation: $*" ;;
    esac
}

migrate_canonical_control_plane_private_binding
ACTUAL="$(IFS='>'; echo "${EVENTS[*]}")"
EXPECTED="plan>confirm>host-data>pull>token>certificate>stop-old>retain-old>run-new>health>binding>retire-old"
assert_eq "${EXPECTED}" "${ACTUAL}" "private-binding migration order"
assert_false "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" "migration flag after success"
assert_false "${CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED}" "combined host-data flag after success"

# A failed replacement restores the exact old container and its previous
# exposure instead of silently falling back to a different unverified runtime.
EVENTS=()
BACKUP_EXISTS=false
NEW_EXISTS=false
CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=true
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=true
CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=false
CONTROL_PLANE_EXISTING_BIND_STATE="wildcard"
run_new_control_plane_container() { EVENTS+=(run-new); NEW_EXISTS=true; }
wait_for_control_plane_health() { EVENTS+=(health); return 0; }
verify_control_plane_host_binding() { EVENTS+=(binding-fail); return 1; }
fail() { EVENTS+=(failed); return 1; }

docker_container_exists() {
    case "$1" in
        mem-control-plane) [[ "${NEW_EXISTS}" == true ]] ;;
        mem-control-plane-pre-private-admin-migration) [[ "${BACKUP_EXISTS}" == true ]] ;;
        mem-control-plane-pre-host-data-migration) return 1 ;;
        *) return 1 ;;
    esac
}

docker() {
    case "$1:${2:-}:${3:-}" in
        stop:mem-control-plane:) EVENTS+=(stop-old) ;;
        rename:mem-control-plane:mem-control-plane-pre-private-admin-migration)
            EVENTS+=(retain-old)
            BACKUP_EXISTS=true
            ;;
        rm:-f:mem-control-plane)
            EVENTS+=(remove-new)
            NEW_EXISTS=false
            ;;
        rename:mem-control-plane-pre-private-admin-migration:mem-control-plane)
            EVENTS+=(restore-old)
            BACKUP_EXISTS=false
            ;;
        start:mem-control-plane:) EVENTS+=(restart-old) ;;
        *) fail_test "unexpected rollback docker invocation: $*" ;;
    esac
}

set +e
migrate_canonical_control_plane_private_binding
STATUS=$?
set -e
[[ ${STATUS} -ne 0 ]] || fail_test "failed private-binding migration unexpectedly succeeded"
ACTUAL="$(IFS='>'; echo "${EVENTS[*]}")"
EXPECTED="plan>confirm>host-data>pull>token>certificate>stop-old>retain-old>run-new>health>binding-fail>capture>remove-new>restore-old>failed"
assert_eq "${EXPECTED}" "${ACTUAL}" "private-binding rollback order"

# If the operator deliberately changes one already-safe private boundary to
# another and migration fails, rollback may restart the previously safe runtime.
EVENTS=()
BACKUP_EXISTS=true
NEW_EXISTS=false
CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=true
CONTROL_PLANE_EXISTING_BIND_STATE="trusted-lan"
CONTROL_PLANE_EXISTING_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_EXISTING_BIND_PORT="8443"

docker_container_exists() {
    case "$1" in
        mem-control-plane) [[ "${NEW_EXISTS}" == true ]] ;;
        mem-control-plane-pre-private-admin-migration) [[ "${BACKUP_EXISTS}" == true ]] ;;
        *) return 1 ;;
    esac
}

docker() {
    case "$1:${2:-}:${3:-}" in
        rename:mem-control-plane-pre-private-admin-migration:mem-control-plane)
            EVENTS+=(restore-old-safe)
            BACKUP_EXISTS=false
            ;;
        start:mem-control-plane:) EVENTS+=(restart-old-safe) ;;
        *) fail_test "unexpected safe rollback docker invocation: $*" ;;
    esac
}

rollback_canonical_private_binding_migration
ACTUAL="$(IFS='>'; echo "${EVENTS[*]}")"
assert_eq "restore-old-safe>restart-old-safe" "${ACTUAL}" "safe previous binding restart on rollback"

echo "PASS: existing Control Plane exposure classification, secure recreation, exact HostIp verification, fail-closed unsafe rollback, and safe rollback contract"
