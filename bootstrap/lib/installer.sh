#!/usr/bin/env bash

# MEM Control Plane bootstrap and legacy-runtime migration helpers.
# This file is sourced by bootstrap/install.sh.

# shellcheck source=bootstrap/lib/control-plane-access.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/control-plane-access.sh"

CONTROL_PLANE_CONTAINER_NAME="mem-control-plane"
CONTROL_PLANE_FRESH_VOLUME_NAME="mem-control-plane-data"
CONTROL_PLANE_VOLUME_NAME="${CONTROL_PLANE_FRESH_VOLUME_NAME}"

CONTROL_PLANE_IMAGE_STABLE="ghcr.io/message-easy-mode/mem-control-plane:stable"
CONTROL_PLANE_IMAGE_DEV="ghcr.io/message-easy-mode/mem-control-plane:dev"
CONTROL_PLANE_IMAGE_LOCAL="mem-control-plane:local"
CONTROL_PLANE_IMAGE_RELEASE="${MEM_RELEASE_CONTROL_PLANE_IMAGE:-}"
CONTROL_PLANE_IMAGE=""

LEGACY_CONTROL_PLANE_CONTAINER_NAME="mem-installer"
DEVELOPMENT_CONTROL_PLANE_CONTAINER_PREFIX="mem-control-plane-dev"
LEGACY_CONTROL_PLANE_VOLUME_NAME="mem-installer-data"
LEGACY_CONTROL_PLANE_IMAGE_STABLE="ghcr.io/matrix-easy-mode/mem-installer:stable"
LEGACY_CONTROL_PLANE_IMAGE_DEV="ghcr.io/matrix-easy-mode/mem-installer:dev"

CONTROL_PLANE_CONTAINER_HTTPS_PORT="8443"
CONTROL_PLANE_CERT_DIR="/data/certs"
CONTROL_PLANE_CANONICAL_CERT_PATH="${CONTROL_PLANE_CERT_DIR}/mem-control-plane.crt"
CONTROL_PLANE_CANONICAL_KEY_PATH="${CONTROL_PLANE_CERT_DIR}/mem-control-plane.key"
CONTROL_PLANE_LEGACY_CERT_PATH="${CONTROL_PLANE_CERT_DIR}/mem-installer.crt"
CONTROL_PLANE_LEGACY_KEY_PATH="${CONTROL_PLANE_CERT_DIR}/mem-installer.key"
CONTROL_PLANE_CERT_PATH="${CONTROL_PLANE_CANONICAL_CERT_PATH}"
CONTROL_PLANE_KEY_PATH="${CONTROL_PLANE_CANONICAL_KEY_PATH}"
CONTROL_PLANE_TOKEN_PATH="/data/setup-token"

# Host-visible data used by sibling containers created through the host Docker
# daemon. The path is bind-mounted into the Control Plane at the same absolute
# location so a path written by the API names the same physical host file.
CONTROL_PLANE_HOST_DATA_ROOT="${MEM_CONTROL_PLANE_HOST_DATA_ROOT:-/var/lib/message-easy-mode}"
CONTROL_PLANE_MEM_DATA_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/mem-data"
CONTROL_PLANE_INSTANCE_DATA_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/instances"
CONTROL_PLANE_COTURN_STORAGE_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/platform/coturn"
CONTROL_PLANE_SEQ_HOST_DATA_PATH="${CONTROL_PLANE_HOST_DATA_ROOT}/seq"
CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=false
CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME="mem-control-plane-pre-host-data-migration"

# The established platform network used by the containerized Control Plane to
# administer MEM-managed services such as Nginx Proxy Manager. Fresh bootstrap
# runs before this network normally exists; recreating an established Control
# Plane must reassert the attachment when the network is already present.
CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME="mem-gateway"

# Existing-runtime private-administration inspection. These values describe
# the actual Docker host publication discovered on an installed Control Plane,
# independently of labels or remembered bootstrap choices.
CONTROL_PLANE_EXISTING_BIND_STATE="unobserved"
CONTROL_PLANE_EXISTING_BIND_ADDRESS=""
CONTROL_PLANE_EXISTING_BIND_PORT=""
CONTROL_PLANE_EXISTING_BINDING_COUNT=0
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=false
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON=""
CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME="mem-control-plane-pre-private-admin-migration"

CONTROL_PLANE_RUNTIME_STATE="unknown"
CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=false
CONTROL_PLANE_LEGACY_WAS_RUNNING=false
SETUP_TOKEN=""
SETUP_TOKEN_WAS_CREATED=false
CONTROL_PLANE_FAILURE_EVIDENCE_CAPTURED=false

resolve_control_plane_image() {
    if [[ -n "${CONTROL_PLANE_IMAGE_OVERRIDE:-}" ]]; then
        CONTROL_PLANE_IMAGE="${CONTROL_PLANE_IMAGE_OVERRIDE}"
        log_info "Control Plane image override provided: ${CONTROL_PLANE_IMAGE}"
        return 0
    fi

    if [[ -n "${CONTROL_PLANE_IMAGE_RELEASE}" ]]; then
        CONTROL_PLANE_IMAGE="${CONTROL_PLANE_IMAGE_RELEASE}"
        log_info "Control Plane image from packaged MEM release: ${CONTROL_PLANE_IMAGE}"
        return 0
    fi

    case "${CHANNEL}" in
        stable)
            CONTROL_PLANE_IMAGE="${CONTROL_PLANE_IMAGE_STABLE}"
            ;;
        dev)
            CONTROL_PLANE_IMAGE="${CONTROL_PLANE_IMAGE_DEV}"
            ;;
        *)
            fail "Unknown Control Plane channel: ${CHANNEL}"
            ;;
    esac

    log_info "Control Plane image: ${CONTROL_PLANE_IMAGE}"
}

generate_setup_token() {
    if [[ -n "${SETUP_TOKEN:-}" ]]; then
        return 0
    fi

    if command -v openssl >/dev/null 2>&1; then
        SETUP_TOKEN="mem_$(openssl rand -hex 24)"
    else
        SETUP_TOKEN="mem_$(date +%s)_$RANDOM$RANDOM"
        log_warn "openssl not found. Generated weaker setup token."
    fi

    SETUP_TOKEN_WAS_CREATED=true
}

docker_container_exists() {
    local container_name="$1"
    docker ps -a --format '{{.Names}}' | grep -Fxq "${container_name}"
}

docker_container_running() {
    local container_name="$1"
    docker ps --format '{{.Names}}' | grep -Fxq "${container_name}"
}

docker_volume_exists() {
    local volume_name="$1"
    docker volume inspect "${volume_name}" >/dev/null 2>&1
}

is_development_control_plane_container_name() {
    local name="$1"
    [[ "${name}" == "${DEVELOPMENT_CONTROL_PLANE_CONTAINER_PREFIX}" ||
       "${name}" == "${DEVELOPMENT_CONTROL_PLANE_CONTAINER_PREFIX}-"* ]]
}

development_control_plane_containers() {
    local name
    docker ps -a --format '{{.Names}}' 2>/dev/null | while IFS= read -r name; do
        if is_development_control_plane_container_name "${name}"; then
            printf '%s\n' "${name}"
        fi
    done
}

classify_control_plane_state() {
    local canonical_container_exists="$1"
    local legacy_container_exists="$2"
    local canonical_volume_exists="$3"
    local legacy_volume_exists="$4"

    if [[ "${canonical_container_exists}" == true && "${legacy_container_exists}" == true ]]; then
        printf '%s\n' "container-conflict"
    elif [[ "${canonical_container_exists}" == true ]]; then
        printf '%s\n' "canonical-container"
    elif [[ "${legacy_container_exists}" == true ]]; then
        printf '%s\n' "legacy-container"
    elif [[ "${canonical_volume_exists}" == true && "${legacy_volume_exists}" == true ]]; then
        printf '%s\n' "volume-conflict"
    elif [[ "${legacy_volume_exists}" == true ]]; then
        printf '%s\n' "legacy-volume-only"
    elif [[ "${canonical_volume_exists}" == true ]]; then
        printf '%s\n' "canonical-volume-only"
    else
        printf '%s\n' "fresh"
    fi
}

container_data_volume() {
    local container_name="$1"

    docker inspect \
        --format '{{range .Mounts}}{{if eq .Destination "/data"}}{{if eq .Type "volume"}}{{.Name}}{{else}}bind:{{.Source}}{{end}}{{end}}{{end}}' \
        "${container_name}" 2>/dev/null
}

container_published_bindings() {
    local container_name="$1"

    docker inspect \
        --format '{{with index .NetworkSettings.Ports "8443/tcp"}}{{range .}}{{printf "%s|%s\n" .HostIp .HostPort}}{{end}}{{end}}' \
        "${container_name}" 2>/dev/null
}

inspect_existing_control_plane_binding() {
    local container_name="$1"
    local -a bindings=()
    local row

    CONTROL_PLANE_EXISTING_BIND_STATE="unknown"
    CONTROL_PLANE_EXISTING_BIND_ADDRESS=""
    CONTROL_PLANE_EXISTING_BIND_PORT=""
    CONTROL_PLANE_EXISTING_BINDING_COUNT=0

    while IFS= read -r row; do
        [[ -n "${row}" ]] && bindings+=("${row}")
    done < <(container_published_bindings "${container_name}")

    CONTROL_PLANE_EXISTING_BINDING_COUNT="${#bindings[@]}"
    if (( ${#bindings[@]} == 0 )); then
        fail "Could not determine the HTTPS host binding for existing Control Plane container '${container_name}'."
    fi

    local first_address first_port address port
    IFS='|' read -r first_address first_port <<< "${bindings[0]}"
    first_address="${first_address:-0.0.0.0}"
    [[ -n "${first_port:-}" ]] || fail "Existing Control Plane binding did not contain a host port."

    for row in "${bindings[@]}"; do
        IFS='|' read -r address port <<< "${row}"
        address="${address:-0.0.0.0}"
        if [[ "${port}" != "${first_port}" ]]; then
            fail "Existing Control Plane publishes HTTPS on multiple host ports. MEM will not guess which port to preserve."
        fi
    done

    CONTROL_PLANE_EXISTING_BIND_PORT="${first_port}"

    if (( ${#bindings[@]} > 1 )); then
        CONTROL_PLANE_EXISTING_BIND_STATE="multiple"
        CONTROL_PLANE_EXISTING_BIND_ADDRESS="${first_address}"
        log_warn "Existing Control Plane has multiple HTTPS host bindings; reviewed private-binding recreation is required."
        return 0
    fi

    CONTROL_PLANE_EXISTING_BIND_ADDRESS="${first_address}"
    CONTROL_PLANE_EXISTING_BIND_STATE="$(classify_control_plane_host_bind_address "${first_address}")"

    case "${CONTROL_PLANE_EXISTING_BIND_STATE}" in
        loopback)
            log_info "Existing Control Plane binding is private loopback: ${first_address}:${first_port}."
            ;;
        trusted-lan)
            log_info "Existing Control Plane binding is an assigned Trusted LAN address: ${first_address}:${first_port}."
            ;;
        wildcard)
            log_warn "Existing Control Plane is published on a wildcard host address (${first_address:-0.0.0.0}:${first_port}). This is not supported by MEM 0.2.0 private administration."
            ;;
        loopback-noncanonical)
            log_warn "Existing Control Plane uses non-canonical IPv6 loopback (${first_address}:${first_port}); MEM 0.2.0 canonicalizes SSH/local-only administration on 127.0.0.1."
            ;;
        trusted-lan-unassigned)
            log_warn "Existing Control Plane is bound to private address ${first_address}:${first_port}, but that address is no longer assigned to an eligible host interface."
            ;;
        nonprivate-ipv4)
            log_warn "Existing Control Plane is bound directly to non-private IPv4 address ${first_address}:${first_port}. Direct public Control Plane exposure is unsupported."
            ;;
        *)
            log_warn "Existing Control Plane binding '${first_address}:${first_port}' is not a supported private MEM administration boundary."
            ;;
    esac
}

existing_control_plane_binding_is_safe() {
    [[ "${CONTROL_PLANE_EXISTING_BIND_STATE}" == "loopback" || \
       "${CONTROL_PLANE_EXISTING_BIND_STATE}" == "trusted-lan" ]]
}

existing_control_plane_binding_matches_desired() {
    [[ "${CONTROL_PLANE_EXISTING_BINDING_COUNT}" -eq 1 && \
       "${CONTROL_PLANE_EXISTING_BIND_ADDRESS}" == "${CONTROL_PLANE_BIND_ADDRESS}" && \
       "${CONTROL_PLANE_EXISTING_BIND_PORT}" == "${CONTROL_PLANE_PORT}" ]]
}

preserve_existing_safe_control_plane_access() {
    if [[ "${CONTROL_PLANE_EXISTING_BIND_STATE}" == "loopback" ]]; then
        CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
        CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
        CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
    elif [[ "${CONTROL_PLANE_EXISTING_BIND_STATE}" == "trusted-lan" ]]; then
        CONTROL_PLANE_ACCESS_MODE="trusted-lan"
        CONTROL_PLANE_BIND_ADDRESS="${CONTROL_PLANE_EXISTING_BIND_ADDRESS}"
        CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=true
    else
        fail "Cannot preserve Control Plane access from unsupported binding state '${CONTROL_PLANE_EXISTING_BIND_STATE}'."
    fi

    validate_control_plane_access_configuration
    log_info "Preserving the existing safe Control Plane administration boundary: ${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT}."
}

resolve_legacy_control_plane_access_policy() {
    # A safe preview/legacy 0.2.0 runtime should not unexpectedly change its
    # private administration address merely because the permanent container
    # name is migrated. Unsafe legacy exposure is never grandfathered.
    if existing_control_plane_binding_is_safe && \
       [[ "${CONTROL_PLANE_ACCESS_EXPLICIT:-false}" != true ]]; then
        preserve_existing_safe_control_plane_access
        return 0
    fi

    resolve_control_plane_access_policy
}

resolve_canonical_control_plane_access_policy() {
    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=false
    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON=""

    # Re-running bootstrap should not silently change an already-safe private
    # administration boundary. Operators can deliberately change it by using
    # --control-plane-access (and a Trusted LAN address when required).
    if existing_control_plane_binding_is_safe && \
       [[ "${CONTROL_PLANE_ACCESS_EXPLICIT:-false}" != true ]]; then
        preserve_existing_safe_control_plane_access
        return 0
    fi

    if existing_control_plane_binding_is_safe; then
        # An explicit operator request may intentionally move between loopback
        # and a Trusted LAN address. Reuse the normal 01A validation contract.
        resolve_control_plane_access_policy
        if ! existing_control_plane_binding_matches_desired; then
            CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=true
            CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON="operator-requested private administration binding change"
        fi
        return 0
    fi

    # Unsafe or ambiguous existing exposure must never be preserved simply for
    # compatibility. In unattended operation the fail-safe target is loopback;
    # an interactive operator may deliberately select an eligible Trusted LAN.
    resolve_control_plane_access_policy
    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=true
    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON="existing Control Plane binding '${CONTROL_PLANE_EXISTING_BIND_STATE}' is not a supported MEM 0.2.0 private administration boundary"
}

resolve_control_plane_access_for_detected_runtime() {
    bootstrap_set_phase "control-plane-access"
    bootstrap_set_operation "resolve private Control Plane administration boundary"

    case "${CONTROL_PLANE_RUNTIME_STATE}" in
        canonical-container)
            resolve_canonical_control_plane_access_policy
            ;;
        legacy-container)
            resolve_legacy_control_plane_access_policy
            ;;
        *)
            resolve_control_plane_access_policy
            ;;
    esac
}

verify_control_plane_host_binding() {
    local container_name="${1:-${CONTROL_PLANE_CONTAINER_NAME}}"
    local expected="${CONTROL_PLANE_BIND_ADDRESS}|${CONTROL_PLANE_PORT}"
    local -a actual=()
    local row

    while IFS= read -r row; do
        [[ -n "${row}" ]] && actual+=("${row}")
    done < <(container_published_bindings "${container_name}")

    if (( ${#actual[@]} != 1 )); then
        log_error "Control Plane binding verification expected exactly one host publication '${expected}', observed ${#actual[@]}."
        return 1
    fi

    local address port
    IFS='|' read -r address port <<< "${actual[0]}"
    address="${address:-0.0.0.0}"

    if [[ "${address}|${port}" != "${expected}" ]]; then
        log_error "Control Plane binding verification expected '${expected}', observed '${address}|${port}'."
        return 1
    fi

    log_info "Control Plane private host binding verified: ${address}:${port}."
    return 0
}

container_environment_value() {
    local container_name="$1"
    local variable_name="$2"

    docker inspect \
        --format '{{range .Config.Env}}{{println .}}{{end}}' \
        "${container_name}" 2>/dev/null \
        | awk -F= -v key="${variable_name}" '$1 == key {sub(/^[^=]*=/, ""); print; exit}'
}

validate_control_plane_host_data_root() {
    if [[ -z "${CONTROL_PLANE_HOST_DATA_ROOT}" ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" != /* ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == "/" ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == "/data" ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == "/data/"* ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == *:* ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == *$'\n'* ||
          "${CONTROL_PLANE_HOST_DATA_ROOT}" == *$'\r'* ||
          "/${CONTROL_PLANE_HOST_DATA_ROOT#/}/" == *"/../"* ]]; then
        fail "MEM_CONTROL_PLANE_HOST_DATA_ROOT must be a safe absolute host directory that does not overlap /data."
    fi

    CONTROL_PLANE_HOST_DATA_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT%/}"
    CONTROL_PLANE_MEM_DATA_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/mem-data"
    CONTROL_PLANE_INSTANCE_DATA_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/instances"
    CONTROL_PLANE_COTURN_STORAGE_ROOT="${CONTROL_PLANE_HOST_DATA_ROOT}/platform/coturn"
    CONTROL_PLANE_SEQ_HOST_DATA_PATH="${CONTROL_PLANE_HOST_DATA_ROOT}/seq"
}

prepare_control_plane_host_data_root() {
    validate_control_plane_host_data_root

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would create or preserve the canonical host-data root ${CONTROL_PLANE_HOST_DATA_ROOT} and its mem-data, instances, platform/coturn, and seq children with owner-only permissions."
        return 0
    fi

    bootstrap_set_phase "host-data-preparation"
    bootstrap_set_operation "prepare host-visible Control Plane service data"
    bootstrap_mark_changes_begun

    install -d -o root -g root -m 0700 \
        "${CONTROL_PLANE_HOST_DATA_ROOT}" \
        "${CONTROL_PLANE_MEM_DATA_ROOT}" \
        "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" \
        "${CONTROL_PLANE_HOST_DATA_ROOT}/platform" \
        "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" \
        "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
}

container_bind_source_for_destination() {
    local container_name="$1"
    local destination="$2"

    docker inspect \
        --format '{{range .Mounts}}{{printf "%s|%s|%s\n" .Type .Source .Destination}}{{end}}' \
        "${container_name}" 2>/dev/null \
        | awk -F'|' -v destination="${destination}" \
            '$1 == "bind" && $3 == destination {print $2; exit}'
}

container_has_required_host_data_mount() {
    local container_name="$1"
    local mounted_source

    validate_control_plane_host_data_root
    mounted_source="$(container_bind_source_for_destination \
        "${container_name}" \
        "${CONTROL_PLANE_HOST_DATA_ROOT}")"

    [[ "${mounted_source}" == "${CONTROL_PLANE_HOST_DATA_ROOT}" ]]
}

container_has_required_host_data_environment() {
    local container_name="$1"

    validate_control_plane_host_data_root

    [[ "$(container_environment_value "${container_name}" "MEM_CONTROL_PLANE_HOST_DATA_ROOT")" == "${CONTROL_PLANE_HOST_DATA_ROOT}" &&
       "$(container_environment_value "${container_name}" "MEM_DATA_ROOT")" == "${CONTROL_PLANE_MEM_DATA_ROOT}" &&
       "$(container_environment_value "${container_name}" "Provisioning__InstanceDataRoot")" == "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" &&
       "$(container_environment_value "${container_name}" "Coturn__StorageRootPath")" == "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" &&
       "$(container_environment_value "${container_name}" "Diagnostics__Seq__HostDataPath")" == "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}" ]]
}

container_has_required_host_data_contract() {
    local container_name="$1"

    container_has_required_host_data_mount "${container_name}" &&
        container_has_required_host_data_environment "${container_name}"
}

select_certificate_paths_for_volume() {
    local volume_name="$1"

    if [[ "${volume_name}" == "${LEGACY_CONTROL_PLANE_VOLUME_NAME}" ]]; then
        CONTROL_PLANE_CERT_PATH="${CONTROL_PLANE_LEGACY_CERT_PATH}"
        CONTROL_PLANE_KEY_PATH="${CONTROL_PLANE_LEGACY_KEY_PATH}"
    else
        CONTROL_PLANE_CERT_PATH="${CONTROL_PLANE_CANONICAL_CERT_PATH}"
        CONTROL_PLANE_KEY_PATH="${CONTROL_PLANE_CANONICAL_KEY_PATH}"
    fi
}

validate_container_data_volume() {
    local container_name="$1"
    local expected_kind="$2"
    local mounted_volume
    mounted_volume="$(container_data_volume "${container_name}")"

    if [[ -z "${mounted_volume}" ]]; then
        fail "Control Plane container '${container_name}' does not expose a named Docker volume at /data. No migration or recreation was attempted."
    fi

    if [[ "${mounted_volume}" == bind:* ]]; then
        fail "Control Plane container '${container_name}' uses a bind mount at /data. This bootstrap only migrates reviewed named-volume installations."
    fi

    case "${expected_kind}" in
        canonical)
            if [[ "${mounted_volume}" != "${CONTROL_PLANE_FRESH_VOLUME_NAME}" && \
                  "${mounted_volume}" != "${LEGACY_CONTROL_PLANE_VOLUME_NAME}" ]]; then
                fail "Canonical Control Plane container '${container_name}' uses unexpected data volume '${mounted_volume}'."
            fi
            ;;
        legacy)
            if [[ "${mounted_volume}" != "${LEGACY_CONTROL_PLANE_VOLUME_NAME}" ]]; then
                fail "Legacy Control Plane container '${container_name}' uses unexpected data volume '${mounted_volume}'."
            fi
            ;;
        *)
            fail "Unknown Control Plane volume validation kind: ${expected_kind}"
            ;;
    esac

    CONTROL_PLANE_VOLUME_NAME="${mounted_volume}"
    select_certificate_paths_for_volume "${CONTROL_PLANE_VOLUME_NAME}"
}

detect_control_plane_state() {
    validate_control_plane_host_data_root
    CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=false

    if [[ "${DOCKER_AVAILABLE:-false}" != true || "${DOCKER_DAEMON_AVAILABLE:-false}" != true ]]; then
        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Docker runtime state cannot be observed during this dry run. The fresh canonical plan is shown without assuming the host is empty."
            CONTROL_PLANE_RUNTIME_STATE="unobserved"
            CONTROL_PLANE_VOLUME_NAME="${CONTROL_PLANE_FRESH_VOLUME_NAME}"
            select_certificate_paths_for_volume "${CONTROL_PLANE_VOLUME_NAME}"
            return 0
        fi

        fail "Docker is unavailable, so MEM cannot determine the Control Plane runtime state."
    fi

    local development_containers
    development_containers="$(development_control_plane_containers)"
    if [[ -n "${development_containers}" ]]; then
        fail "A development Control Plane exists on this Docker host: $(echo "${development_containers}" | paste -sd ', ' -). Stop it with the supported developer harness before running production bootstrap."
    fi

    local canonical_container_exists=false
    local legacy_container_exists=false
    local canonical_volume_exists=false
    local legacy_volume_exists=false

    docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}" && canonical_container_exists=true
    docker_container_exists "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" && legacy_container_exists=true
    docker_volume_exists "${CONTROL_PLANE_FRESH_VOLUME_NAME}" && canonical_volume_exists=true
    docker_volume_exists "${LEGACY_CONTROL_PLANE_VOLUME_NAME}" && legacy_volume_exists=true

    CONTROL_PLANE_RUNTIME_STATE="$(classify_control_plane_state \
        "${canonical_container_exists}" \
        "${legacy_container_exists}" \
        "${canonical_volume_exists}" \
        "${legacy_volume_exists}")"

    case "${CONTROL_PLANE_RUNTIME_STATE}" in
        container-conflict)
            fail "Both '${CONTROL_PLANE_CONTAINER_NAME}' and legacy '${LEGACY_CONTROL_PLANE_CONTAINER_NAME}' exist. MEM will not choose or mutate either runtime automatically."
            ;;
        volume-conflict)
            fail "Both '${CONTROL_PLANE_FRESH_VOLUME_NAME}' and legacy '${LEGACY_CONTROL_PLANE_VOLUME_NAME}' exist without a Control Plane container. Review the state before continuing."
            ;;
        legacy-volume-only)
            fail "Legacy data volume '${LEGACY_CONTROL_PLANE_VOLUME_NAME}' exists without its legacy container. Use the documented recovery path; bootstrap will not guess a container definition."
            ;;
        canonical-container)
            validate_container_data_volume "${CONTROL_PLANE_CONTAINER_NAME}" canonical
            if ! container_has_required_host_data_contract "${CONTROL_PLANE_CONTAINER_NAME}"; then
                CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=true
                log_warn "The canonical Control Plane predates the complete production host-data contract at ${CONTROL_PLANE_HOST_DATA_ROOT}. A reviewed container recreation is required before host-visible platform and stack resources can be managed safely."
            fi
            if docker_container_running "${CONTROL_PLANE_CONTAINER_NAME}"; then
                CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=true
                log_info "MEM Control Plane is already running: ${CONTROL_PLANE_CONTAINER_NAME}"
            else
                CONTROL_PLANE_CONTAINER_ALREADY_RUNNING=false
                log_warn "MEM Control Plane exists but is stopped: ${CONTROL_PLANE_CONTAINER_NAME}"
            fi
            ;;
        legacy-container)
            validate_container_data_volume "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" legacy
            CONTROL_PLANE_LEGACY_WAS_RUNNING=false
            if docker_container_running "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"; then
                CONTROL_PLANE_LEGACY_WAS_RUNNING=true
                log_warn "Legacy Control Plane runtime detected and running: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            else
                log_warn "Legacy Control Plane runtime detected and stopped: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            fi
            ;;
        canonical-volume-only)
            CONTROL_PLANE_VOLUME_NAME="${CONTROL_PLANE_FRESH_VOLUME_NAME}"
            select_certificate_paths_for_volume "${CONTROL_PLANE_VOLUME_NAME}"
            log_warn "Canonical Control Plane data volume exists without a container. The reviewed data volume will be reused."
            ;;
        fresh)
            CONTROL_PLANE_VOLUME_NAME="${CONTROL_PLANE_FRESH_VOLUME_NAME}"
            select_certificate_paths_for_volume "${CONTROL_PLANE_VOLUME_NAME}"
            log_info "No existing MEM Control Plane runtime was detected."
            ;;
        unknown)
            ;;
        *)
            fail "Unsupported Control Plane runtime state: ${CONTROL_PLANE_RUNTIME_STATE}"
            ;;
    esac

    resolve_existing_runtime_port
}

resolve_existing_runtime_port() {
    local existing_container=""

    case "${CONTROL_PLANE_RUNTIME_STATE}" in
        canonical-container)
            existing_container="${CONTROL_PLANE_CONTAINER_NAME}"
            ;;
        legacy-container)
            existing_container="${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            ;;
        *)
            return 0
            ;;
    esac

    inspect_existing_control_plane_binding "${existing_container}"
    local detected_port="${CONTROL_PLANE_EXISTING_BIND_PORT}"

    if [[ -z "${detected_port}" ]]; then
        fail "Could not determine the published HTTPS port for existing Control Plane container '${existing_container}'."
    fi

    if [[ "${CONTROL_PLANE_PORT_EXPLICIT:-false}" == true ]]; then
        if [[ "${CONTROL_PLANE_PORT}" != "${detected_port}" ]]; then
            if [[ "${CONTROL_PLANE_RUNTIME_STATE}" == "canonical-container" ]]; then
                log_warn "Existing canonical container publishes port ${detected_port}; requested port ${CONTROL_PLANE_PORT} cannot be applied independently of reviewed container recreation. Preserving ${detected_port} for this private-binding migration."
                CONTROL_PLANE_PORT="${detected_port}"
            else
                log_warn "Legacy runtime publishes port ${detected_port}; migration target was explicitly requested on ${CONTROL_PLANE_PORT}."
            fi
        fi
    else
        CONTROL_PLANE_PORT="${detected_port}"
        log_info "Using existing Control Plane HTTPS port: ${CONTROL_PLANE_PORT}"
    fi
}

create_control_plane_volume() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would ensure Docker volume ${CONTROL_PLANE_VOLUME_NAME} exists."
        return 0
    fi

    bootstrap_set_operation "ensure Control Plane data volume"
    bootstrap_mark_changes_begun
    log_info "Ensuring Docker volume exists: ${CONTROL_PLANE_VOLUME_NAME}"
    docker volume create "${CONTROL_PLANE_VOLUME_NAME}" >/dev/null
}

volume_file_exists() {
    local volume_name="$1"
    local file_path="$2"

    docker run --rm \
        -v "${volume_name}:/data:ro" \
        alpine:3.20 \
        sh -c "test -f '${file_path}'" >/dev/null 2>&1
}

read_volume_file() {
    local volume_name="$1"
    local file_path="$2"

    docker run --rm \
        -v "${volume_name}:/data:ro" \
        alpine:3.20 \
        sh -c "cat '${file_path}' 2>/dev/null || true"
}

load_existing_setup_token() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        return 0
    fi

    if docker_volume_exists "${CONTROL_PLANE_VOLUME_NAME}"; then
        SETUP_TOKEN="$(read_volume_file "${CONTROL_PLANE_VOLUME_NAME}" "${CONTROL_PLANE_TOKEN_PATH}")"
    fi

    if [[ -n "${SETUP_TOKEN}" ]]; then
        SETUP_TOKEN="$(printf '%s' "${SETUP_TOKEN}" | tr -d '\r\n')"
        log_info "Reusing the persisted Control Plane setup token."
        return 0
    fi

    local source_container=""
    local source_variable=""
    case "${CONTROL_PLANE_RUNTIME_STATE}" in
        canonical-container)
            source_container="${CONTROL_PLANE_CONTAINER_NAME}"
            source_variable="MEM_CONTROL_PLANE_SETUP_TOKEN"
            ;;
        legacy-container)
            source_container="${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            source_variable="MEM_INSTALLER_SETUP_TOKEN"
            ;;
    esac

    if [[ -n "${source_container}" ]]; then
        SETUP_TOKEN="$(container_environment_value "${source_container}" "${source_variable}")"
    fi

    if [[ -n "${SETUP_TOKEN}" ]]; then
        SETUP_TOKEN="$(printf '%s' "${SETUP_TOKEN}" | tr -d '\r\n')"
        log_info "Recovered the existing setup token from the reviewed Control Plane container definition."
    fi
}

persist_setup_token() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would persist a new setup token to the Control Plane data volume only if one is absent."
        return 0
    fi

    if [[ "${SETUP_TOKEN_WAS_CREATED}" != true ]]; then
        return 0
    fi

    bootstrap_set_operation "persist Control Plane setup authority"
    bootstrap_mark_changes_begun
    log_info "Persisting the new setup token to the Control Plane data volume."

    docker run --rm \
        -v "${CONTROL_PLANE_VOLUME_NAME}:/data" \
        alpine:3.20 \
        sh -c "
            set -eu
            umask 077
            printf '%s\n' '${SETUP_TOKEN}' > '${CONTROL_PLANE_TOKEN_PATH}'
            chmod 600 '${CONTROL_PLANE_TOKEN_PATH}'
        "
}

prepare_setup_token() {
    load_existing_setup_token

    if [[ -z "${SETUP_TOKEN}" ]]; then
        generate_setup_token
    fi

    persist_setup_token
}

ensure_control_plane_certificate() {
    local extra_san_ip
    extra_san_ip="$(control_plane_certificate_extra_san_ip)"

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would preserve an existing certificate or create ${CONTROL_PLANE_CERT_PATH} for the selected private Control Plane access boundary."
        return 0
    fi

    local cert_exists=false
    local key_exists=false
    volume_file_exists "${CONTROL_PLANE_VOLUME_NAME}" "${CONTROL_PLANE_CERT_PATH}" && cert_exists=true
    volume_file_exists "${CONTROL_PLANE_VOLUME_NAME}" "${CONTROL_PLANE_KEY_PATH}" && key_exists=true

    if [[ "${cert_exists}" == true && "${key_exists}" == true ]]; then
        log_info "Reusing Control Plane HTTPS certificate: ${CONTROL_PLANE_CERT_PATH}"
        return 0
    fi

    if [[ "${cert_exists}" != "${key_exists}" ]]; then
        fail "Control Plane certificate state is incomplete in '${CONTROL_PLANE_VOLUME_NAME}'. No certificate was overwritten."
    fi

    bootstrap_set_operation "generate Control Plane HTTPS certificate"
    bootstrap_mark_changes_begun
    log_info "Generating Control Plane HTTPS certificate: ${CONTROL_PLANE_CERT_PATH}"

    docker run --rm \
        -v "${CONTROL_PLANE_VOLUME_NAME}:/data" \
        alpine:3.20 \
        sh -c "
            set -eu
            apk add --no-cache openssl >/dev/null
            mkdir -p '${CONTROL_PLANE_CERT_DIR}'

            SAN='DNS:localhost,IP:127.0.0.1'
            if [ -n '${extra_san_ip}' ]; then
                SAN=\"\${SAN},IP:${extra_san_ip}\"
            fi

            openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
                -keyout '${CONTROL_PLANE_KEY_PATH}' \
                -out '${CONTROL_PLANE_CERT_PATH}' \
                -subj '/CN=MEM Control Plane' \
                -addext \"subjectAltName=\${SAN}\" >/dev/null 2>&1

            chmod 600 '${CONTROL_PLANE_KEY_PATH}'
            chmod 644 '${CONTROL_PLANE_CERT_PATH}'
        "
}

control_plane_image_exists_locally() {
    local image="$1"
    docker image inspect "${image}" >/dev/null 2>&1
}

pull_control_plane_image() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        if control_plane_image_exists_locally "${CONTROL_PLANE_IMAGE}"; then
            log_info "Dry-run mode: Control Plane image already exists locally: ${CONTROL_PLANE_IMAGE}"
        else
            log_info "Dry-run mode: would pull Control Plane image ${CONTROL_PLANE_IMAGE}."
        fi
        return 0
    fi

    if control_plane_image_exists_locally "${CONTROL_PLANE_IMAGE}"; then
        log_info "Control Plane image already exists locally: ${CONTROL_PLANE_IMAGE}"
        return 0
    fi

    bootstrap_set_operation "pull Control Plane image"
    bootstrap_mark_changes_begun
    log_info "Pulling Control Plane image: ${CONTROL_PLANE_IMAGE}"
    docker pull "${CONTROL_PLANE_IMAGE}"
}

run_new_control_plane_container() {
    validate_control_plane_host_data_root
    validate_control_plane_access_configuration

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would start canonical container ${CONTROL_PLANE_CONTAINER_NAME} with host binding $(control_plane_publish_spec)."
        return 0
    fi

    if docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}"; then
        fail "Canonical Control Plane container already exists: ${CONTROL_PLANE_CONTAINER_NAME}"
    fi

    if [[ ! -d "${CONTROL_PLANE_HOST_DATA_ROOT}" ||
          ! -d "${CONTROL_PLANE_MEM_DATA_ROOT}" ||
          ! -d "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" ||
          ! -d "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" ||
          ! -d "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}" ]]; then
        fail "The required canonical Control Plane host-data directories were not prepared beneath: ${CONTROL_PLANE_HOST_DATA_ROOT}"
    fi

    local publish_spec public_base_url
    publish_spec="$(control_plane_publish_spec)"
    public_base_url="$(control_plane_canonical_private_origin)"

    bootstrap_set_operation "start MEM Control Plane container"
    bootstrap_mark_changes_begun
    log_info "Starting MEM Control Plane container: ${CONTROL_PLANE_CONTAINER_NAME}"
    log_info "Control Plane host publication: ${publish_spec}"

    local -a docker_args=(
        run -d
        --name "${CONTROL_PLANE_CONTAINER_NAME}"
        --restart unless-stopped
        --label "io.message-easy-mode.managed=true"
        --label "io.message-easy-mode.runtime-mode=containerized-production"
        --label "io.message-easy-mode.resource=control-plane"
        --label "io.message-easy-mode.control-plane-access=${CONTROL_PLANE_ACCESS_MODE}"
        --label "io.message-easy-mode.control-plane-bind-address=${CONTROL_PLANE_BIND_ADDRESS}"
        -p "${publish_spec}"
        -v /var/run/docker.sock:/var/run/docker.sock
        -v "${CONTROL_PLANE_VOLUME_NAME}:/data"
        -v "${CONTROL_PLANE_HOST_DATA_ROOT}:${CONTROL_PLANE_HOST_DATA_ROOT}"
        -e "ASPNETCORE_URLS=https://0.0.0.0:${CONTROL_PLANE_CONTAINER_HTTPS_PORT}"
        -e "ASPNETCORE_Kestrel__Certificates__Default__Path=${CONTROL_PLANE_CERT_PATH}"
        -e "ASPNETCORE_Kestrel__Certificates__Default__KeyPath=${CONTROL_PLANE_KEY_PATH}"
        -e "MEM_CONTROL_PLANE_SETUP_TOKEN_PATH=${CONTROL_PLANE_TOKEN_PATH}"
        -e "MEM_CONTROL_PLANE_CHANNEL=${CHANNEL}"
        -e "MEM_CONTROL_PLANE_PUBLIC_PORT=${CONTROL_PLANE_PORT}"
        -e "MEM_CONTROL_PLANE_ACCESS_MODE=${CONTROL_PLANE_ACCESS_MODE}"
        -e "MEM_CONTROL_PLANE_BIND_ADDRESS=${CONTROL_PLANE_BIND_ADDRESS}"
        -e "App__PublicBaseUrl=${public_base_url}"
        -e "MEM_RUNTIME_MODE=containerized-production"
        -e "MEM_STATE_ROOT=/data"
        -e "MEM_CONTROL_PLANE_HOST_DATA_ROOT=${CONTROL_PLANE_HOST_DATA_ROOT}"
        -e "MEM_DATA_ROOT=${CONTROL_PLANE_MEM_DATA_ROOT}"
        -e "Provisioning__InstanceDataRoot=${CONTROL_PLANE_INSTANCE_DATA_ROOT}"
        -e "Coturn__StorageRootPath=${CONTROL_PLANE_COTURN_STORAGE_ROOT}"
        -e "Diagnostics__Seq__HostDataPath=${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
        -e "MemRuntime__ContainerName=${CONTROL_PLANE_CONTAINER_NAME}"
    )

    if [[ "${CONTROL_PLANE_ACCESS_MODE}" == "trusted-lan" ]]; then
        docker_args+=( -e "MEM_CONTROL_PLANE_HOST_IPV4=${CONTROL_PLANE_BIND_ADDRESS}" )
    fi

    docker_args+=( "${CONTROL_PLANE_IMAGE}" )
    docker "${docker_args[@]}" >/dev/null

    connect_control_plane_to_existing_managed_gateway
}

connect_control_plane_to_existing_managed_gateway() {
    local network_name="${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}"

    if ! docker network inspect "${network_name}" >/dev/null 2>&1; then
        return 0
    fi

    bootstrap_set_operation "attach MEM Control Plane to managed gateway network"
    bootstrap_mark_changes_begun
    log_info "Existing managed gateway network detected; attaching ${CONTROL_PLANE_CONTAINER_NAME} to ${network_name}."

    if ! docker network connect "${network_name}" "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null; then
        log_error "MEM Control Plane could not be attached to existing managed gateway network '${network_name}'."
        return 1
    fi

    log_info "MEM Control Plane attached to existing managed gateway network: ${network_name}"
}

capture_control_plane_failure_evidence() {
    local container_name="${1:-${CONTROL_PLANE_CONTAINER_NAME}}"

    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT:-false}" != true || \
          "${CONTROL_PLANE_FAILURE_EVIDENCE_CAPTURED:-false}" == true ]]; then
        return 0
    fi

    CONTROL_PLANE_FAILURE_EVIDENCE_CAPTURED=true

    {
        echo "Observed at: $(bootstrap_utc_now)"
        echo "Selected image: ${CONTROL_PLANE_IMAGE:-unknown}"
        echo "Expected data volume: ${CONTROL_PLANE_VOLUME_NAME:-unknown}"
        echo "Expected host-data root: ${CONTROL_PLANE_HOST_DATA_ROOT:-unknown}"
        echo "Expected MEM data root: ${CONTROL_PLANE_MEM_DATA_ROOT:-unknown}"
        echo "Expected instance data root: ${CONTROL_PLANE_INSTANCE_DATA_ROOT:-unknown}"
        echo "Expected Coturn data root: ${CONTROL_PLANE_COTURN_STORAGE_ROOT:-unknown}"
        echo "Expected Seq data root: ${CONTROL_PLANE_SEQ_HOST_DATA_PATH:-unknown}"

        if command -v docker >/dev/null 2>&1; then
            echo "Docker server version: $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo unavailable)"
        else
            echo "Docker server version: unavailable"
        fi

        if command -v docker >/dev/null 2>&1 && docker inspect "${container_name}" >/dev/null 2>&1; then
            echo "Container: ${container_name}"
            echo "Container summary: $(docker inspect --format 'image={{.Config.Image}} state={{.State.Status}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} restartCount={{.RestartCount}}' "${container_name}" 2>/dev/null || echo unavailable)"
            echo "Mounts: $(docker inspect --format '{{range .Mounts}}{{printf "%s:%s:%s:%s;" .Type .Name .Source .Destination}}{{end}}' "${container_name}" 2>/dev/null || echo unavailable)"
            echo "Published ports: $(docker inspect --format '{{json .HostConfig.PortBindings}}' "${container_name}" 2>/dev/null || echo unavailable)"
            echo "Networks: $(docker inspect --format '{{range $name, $cfg := .NetworkSettings.Networks}}{{printf "%s;" $name}}{{end}}' "${container_name}" 2>/dev/null || echo unavailable)"

            if command -v curl >/dev/null 2>&1 && [[ -n "${CONTROL_PLANE_PORT:-}" ]]; then
                local base_url
                base_url="$(control_plane_health_base_url)"
                local live_status
                local ready_status
                live_status="$(curl --insecure --silent --output /dev/null --write-out '%{http_code}' --connect-timeout 2 --max-time 5 "${base_url}/health/live" 2>/dev/null || true)"
                ready_status="$(curl --insecure --silent --output /dev/null --write-out '%{http_code}' --connect-timeout 2 --max-time 5 "${base_url}/health/ready" 2>/dev/null || true)"
                echo "Health live HTTP: ${live_status:-unavailable}"
                echo "Health ready HTTP: ${ready_status:-unavailable}"
            fi

            local log_tail
            log_tail="$(docker logs --timestamps --tail 200 "${container_name}" 2>&1 || true)"
            if [[ -n "${log_tail}" ]]; then
                echo "Container log tail (bounded to 32768 characters):"
                printf '%s\n' "${log_tail:0:32768}"
            else
                echo "Container log tail: unavailable or empty"
            fi
        else
            echo "Container '${container_name}' was not available for inspection."
        fi
    } | bootstrap_append_evidence
}

wait_for_control_plane_health() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would verify /health and /health/ready before accepting the runtime."
        return 0
    fi

    local base_url
    base_url="$(control_plane_health_base_url)"
    local attempts=60
    local delay_seconds=2
    local attempt
    local live_seen=false

    bootstrap_set_phase "control-plane-live-health"
    bootstrap_set_operation "verify Control Plane live and ready health"
    log_info "Waiting for MEM Control Plane health at ${base_url}."

    for ((attempt = 1; attempt <= attempts; attempt++)); do
        if curl --insecure --fail --silent --show-error \
            --connect-timeout 2 --max-time 5 \
            "${base_url}/health/live" >/dev/null 2>&1; then
            if [[ "${live_seen}" != true ]]; then
                live_seen=true
                log_info "MEM Control Plane live health responded successfully."
                bootstrap_set_phase "control-plane-ready-health"
            fi

            if curl --insecure --fail --silent --show-error \
                --connect-timeout 2 --max-time 5 \
                "${base_url}/health/ready" >/dev/null 2>&1; then
                log_info "MEM Control Plane health verification passed."
                return 0
            fi
        fi

        sleep "${delay_seconds}"
    done

    log_error "MEM Control Plane did not become healthy at ${base_url}."
    return 1
}

start_existing_canonical_container() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would start existing container ${CONTROL_PLANE_CONTAINER_NAME} without replacing its image or volume."
        return 0
    fi

    bootstrap_set_phase "container-start"
    bootstrap_set_operation "start existing MEM Control Plane container"
    bootstrap_mark_changes_begun
    docker start "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null
    if ! wait_for_control_plane_health; then
        capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
        docker stop "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
        fail "Existing MEM Control Plane failed health verification and was returned to its stopped state."
    fi
}

rollback_legacy_migration() {
    log_warn "Rolling back the Control Plane runtime-name migration."

    if docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}"; then
        capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
        bootstrap_set_operation "remove failed canonical Control Plane during rollback"
        bootstrap_mark_changes_begun
        docker rm -f "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
    fi

    if [[ "${CONTROL_PLANE_LEGACY_WAS_RUNNING}" == true ]]; then
        if existing_control_plane_binding_is_safe; then
            if docker start "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1; then
                log_warn "Legacy runtime restarted on its previously verified private binding: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            else
                log_error "Legacy runtime could not be restarted automatically: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
            fi
        else
            log_warn "Legacy runtime state was preserved but left stopped because its former host binding was not a supported private MEM administration boundary. Fix the migration failure and rerun bootstrap; MEM will not automatically reopen the unsafe exposure."
        fi
    else
        log_warn "Legacy runtime was previously stopped and remains stopped."
    fi
}

rollback_canonical_host_data_migration() {
    log_warn "Rolling back the Control Plane host-data mount migration."

    if docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}"; then
        capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
        bootstrap_set_operation "remove failed host-data migration target"
        bootstrap_mark_changes_begun
        docker rm -f "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
    fi

    if docker_container_exists "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}"; then
        bootstrap_set_operation "restore pre-migration Control Plane container"
        bootstrap_mark_changes_begun
        if docker rename \
            "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}" \
            "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1; then
            if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
                docker start "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
            fi
            log_warn "The previous canonical Control Plane container was restored."
        else
            log_error "The previous Control Plane container could not be renamed automatically from '${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}'."
        fi
    fi
}

migrate_canonical_control_plane_host_data_mount() {
    print_control_plane_launch_plan

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would recreate '${CONTROL_PLANE_CONTAINER_NAME}' with the complete same-path host-data contract rooted at '${CONTROL_PLANE_HOST_DATA_ROOT}', retain '${CONTROL_PLANE_VOLUME_NAME}', restore the managed gateway attachment when '${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}' already exists, verify health, and remove only the temporary old container record."
        return 0
    fi

    if docker_container_exists "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}"; then
        fail "A previous host-data migration backup container already exists: ${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}. Review it before retrying bootstrap."
    fi

    confirm_or_exit "The existing MEM Control Plane predates the required host-data contract. MEM will recreate only the Control Plane container, retain volume '${CONTROL_PLANE_VOLUME_NAME}', and mount '${CONTROL_PLANE_HOST_DATA_ROOT}' at the same absolute path."

    bootstrap_set_phase "host-data-preparation"
    prepare_control_plane_host_data_root

    bootstrap_set_phase "image-resolution"
    bootstrap_set_operation "resolve host-data migration target image"
    pull_control_plane_image

    bootstrap_set_phase "volume-preparation"
    bootstrap_set_operation "prepare retained Control Plane authority"
    prepare_setup_token

    bootstrap_set_phase "certificate-preparation"
    bootstrap_set_operation "prepare retained Control Plane certificate"
    ensure_control_plane_certificate

    bootstrap_set_phase "container-migration"
    if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
        bootstrap_set_operation "stop canonical Control Plane before host-data migration"
        bootstrap_mark_changes_begun
        docker stop "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null
    fi

    bootstrap_set_operation "retain pre-migration Control Plane container record"
    bootstrap_mark_changes_begun
    if ! docker rename \
        "${CONTROL_PLANE_CONTAINER_NAME}" \
        "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}" >/dev/null; then
        if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
            docker start "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
        fi
        fail "The existing Control Plane container could not be retained for rollback. No replacement was started."
    fi

    if ! run_new_control_plane_container || ! wait_for_control_plane_health; then
        rollback_canonical_host_data_migration
        fail "Control Plane host-data mount migration failed health verification. The previous container was restored where possible."
        return 1
    fi

    bootstrap_set_operation "retire pre-migration Control Plane container record"
    bootstrap_mark_changes_begun
    if ! docker rm "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}" >/dev/null; then
        rollback_canonical_host_data_migration
        fail "The host-data-enabled runtime was healthy, but the temporary old container record could not be retired safely. The migration was rolled back."
        return 1
    fi

    CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=false
    log_info "Control Plane host-data mount migration completed successfully."
}

rollback_canonical_private_binding_migration() {
    log_warn "Rolling back the Control Plane private-administration binding migration."

    if docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}"; then
        capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
        bootstrap_set_operation "remove failed private-binding migration target"
        bootstrap_mark_changes_begun
        docker rm -f "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
    fi

    if docker_container_exists "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}"; then
        bootstrap_set_operation "restore pre-migration Control Plane container"
        bootstrap_mark_changes_begun
        if docker rename \
            "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}" \
            "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1; then
            if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
                if existing_control_plane_binding_is_safe; then
                    docker start "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
                    log_warn "The previous canonical Control Plane container was restarted on its previously verified private binding."
                else
                    log_warn "The previous canonical container record was restored but left stopped because its former host binding was not a supported private MEM administration boundary. Fix the migration failure and rerun bootstrap; MEM will not automatically reopen the unsafe exposure."
                fi
            else
                log_warn "The previous canonical Control Plane container was restored and remains stopped, matching its pre-migration state."
            fi
        else
            log_error "The previous Control Plane container could not be renamed automatically from '${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}'."
        fi
    fi
}

migrate_canonical_control_plane_private_binding() {
    print_control_plane_launch_plan

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would recreate '${CONTROL_PLANE_CONTAINER_NAME}' on private binding $(control_plane_publish_spec), retain '${CONTROL_PLANE_VOLUME_NAME}', restore the managed gateway attachment when '${CONTROL_PLANE_MANAGED_GATEWAY_NETWORK_NAME}' already exists, verify health and the exact Docker HostIp, and remove only the temporary old container record."
        return 0
    fi

    if docker_container_exists "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}"; then
        fail "A previous private-binding migration backup container already exists: ${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}. Review it before retrying bootstrap."
    fi

    if docker_container_exists "${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}"; then
        fail "A previous host-data migration backup container already exists: ${CONTROL_PLANE_HOST_DATA_MIGRATION_BACKUP_NAME}. Review it before retrying private-binding migration."
    fi

    local current_binding="${CONTROL_PLANE_EXISTING_BIND_ADDRESS:-unknown}:${CONTROL_PLANE_EXISTING_BIND_PORT:-unknown}"
    local target_binding="${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT}"
    confirm_or_exit "The existing MEM Control Plane binding '${current_binding}' is not the selected 0.2.0 private-administration boundary (${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON}). MEM will recreate only the Control Plane container on '${target_binding}', retain volume '${CONTROL_PLANE_VOLUME_NAME}', and preserve rollback until the replacement is healthy and its exact Docker host binding is verified."

    bootstrap_set_phase "host-data-preparation"
    prepare_control_plane_host_data_root

    bootstrap_set_phase "image-resolution"
    bootstrap_set_operation "resolve private-binding migration target image"
    pull_control_plane_image

    bootstrap_set_phase "volume-preparation"
    bootstrap_set_operation "prepare retained Control Plane authority"
    prepare_setup_token

    bootstrap_set_phase "certificate-preparation"
    bootstrap_set_operation "prepare retained Control Plane certificate"
    ensure_control_plane_certificate

    bootstrap_set_phase "container-migration"
    if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
        bootstrap_set_operation "stop canonical Control Plane before private-binding migration"
        bootstrap_mark_changes_begun
        docker stop "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null
    fi

    bootstrap_set_operation "retain pre-migration Control Plane container record"
    bootstrap_mark_changes_begun
    if ! docker rename \
        "${CONTROL_PLANE_CONTAINER_NAME}" \
        "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}" >/dev/null; then
        if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
            docker start "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
        fi
        fail "The existing Control Plane container could not be retained for rollback. No replacement was started."
    fi

    if ! run_new_control_plane_container || \
       ! wait_for_control_plane_health || \
       ! verify_control_plane_host_binding "${CONTROL_PLANE_CONTAINER_NAME}"; then
        rollback_canonical_private_binding_migration
        fail "Control Plane private-binding migration failed health or binding verification. The previous container was restored where possible."
        return 1
    fi

    bootstrap_set_operation "retire pre-migration Control Plane container record"
    bootstrap_mark_changes_begun
    if ! docker rm "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_BACKUP_NAME}" >/dev/null; then
        rollback_canonical_private_binding_migration
        fail "The private Control Plane runtime was healthy, but the temporary old container record could not be retired safely. The migration was rolled back."
        return 1
    fi

    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED=false
    CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON=""
    CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED=false
    log_info "Control Plane private-administration binding migration completed successfully."
}

migrate_legacy_control_plane() {
    print_control_plane_launch_plan

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would stop legacy '${LEGACY_CONTROL_PLANE_CONTAINER_NAME}', start '${CONTROL_PLANE_CONTAINER_NAME}' against retained volume '${CONTROL_PLANE_VOLUME_NAME}', verify health, then remove only the legacy container record."
        return 0
    fi

    confirm_or_exit "MEM will migrate the permanent runtime name from '${LEGACY_CONTROL_PLANE_CONTAINER_NAME}' to '${CONTROL_PLANE_CONTAINER_NAME}'. The existing volume '${CONTROL_PLANE_VOLUME_NAME}' and certificate files will be retained."

    bootstrap_set_phase "image-resolution"
    bootstrap_set_operation "resolve migration target Control Plane image"
    pull_control_plane_image

    bootstrap_set_phase "volume-preparation"
    bootstrap_set_operation "prepare retained Control Plane data volume and setup authority"
    create_control_plane_volume
    prepare_setup_token
    prepare_control_plane_host_data_root

    bootstrap_set_phase "certificate-preparation"
    bootstrap_set_operation "prepare retained Control Plane certificate"
    ensure_control_plane_certificate

    bootstrap_set_phase "container-start"

    if [[ "${CONTROL_PLANE_LEGACY_WAS_RUNNING}" == true ]]; then
        bootstrap_set_operation "stop legacy Control Plane before runtime-name migration"
        bootstrap_mark_changes_begun
        log_info "Stopping legacy Control Plane container: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
        docker stop "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" >/dev/null
    fi

    if ! run_new_control_plane_container || ! wait_for_control_plane_health; then
        capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
        rollback_legacy_migration
        fail "Control Plane runtime-name migration failed health verification. Legacy state was preserved."
        return 1
    fi

    bootstrap_set_operation "retire legacy Control Plane container record"
    bootstrap_mark_changes_begun
    if ! docker rm "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}" >/dev/null; then
        rollback_legacy_migration
        fail "The canonical runtime was healthy, but the legacy container record could not be retired safely. The migration was rolled back."
        return 1
    fi

    log_info "Removed the retired legacy container record after successful health verification: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
    log_info "Control Plane runtime-name migration completed successfully."
}

print_existing_setup_token() {
    if ! command -v docker >/dev/null 2>&1; then
        fail "Docker is not installed or not available."
    fi

    local source_container=""
    if docker_container_exists "${CONTROL_PLANE_CONTAINER_NAME}"; then
        source_container="${CONTROL_PLANE_CONTAINER_NAME}"
        CONTROL_PLANE_VOLUME_NAME="$(container_data_volume "${source_container}")"
    elif docker_container_exists "${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"; then
        source_container="${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
        CONTROL_PLANE_VOLUME_NAME="$(container_data_volume "${source_container}")"
    elif docker_volume_exists "${CONTROL_PLANE_FRESH_VOLUME_NAME}"; then
        CONTROL_PLANE_VOLUME_NAME="${CONTROL_PLANE_FRESH_VOLUME_NAME}"
    elif docker_volume_exists "${LEGACY_CONTROL_PLANE_VOLUME_NAME}"; then
        CONTROL_PLANE_VOLUME_NAME="${LEGACY_CONTROL_PLANE_VOLUME_NAME}"
    else
        fail "No MEM Control Plane data volume was found."
    fi

    SETUP_TOKEN="$(read_volume_file "${CONTROL_PLANE_VOLUME_NAME}" "${CONTROL_PLANE_TOKEN_PATH}")"

    if [[ -z "${SETUP_TOKEN}" && -n "${source_container}" ]]; then
        if [[ "${source_container}" == "${CONTROL_PLANE_CONTAINER_NAME}" ]]; then
            SETUP_TOKEN="$(container_environment_value "${source_container}" "MEM_CONTROL_PLANE_SETUP_TOKEN")"
        else
            SETUP_TOKEN="$(container_environment_value "${source_container}" "MEM_INSTALLER_SETUP_TOKEN")"
        fi
    fi

    SETUP_TOKEN="$(printf '%s' "${SETUP_TOKEN}" | tr -d '\r\n')"
    if [[ -z "${SETUP_TOKEN}" ]]; then
        fail "No setup token was found in the reviewed Control Plane state."
    fi

    echo
    echo "MEM Control Plane setup token"
    echo "-----------------------------"
    echo
    echo "  ${SETUP_TOKEN}"
    echo
    echo "Keep this token private."
    echo
}

print_control_plane_launch_plan() {
    echo
    echo "MEM Control Plane launch plan"
    echo "-----------------------------"
    echo "State:       ${CONTROL_PLANE_RUNTIME_STATE}"
    echo "Image:       ${CONTROL_PLANE_IMAGE}"
    echo "Container:   ${CONTROL_PLANE_CONTAINER_NAME}"
    echo "HTTPS port:  ${CONTROL_PLANE_PORT}:${CONTROL_PLANE_CONTAINER_HTTPS_PORT}"
    echo "Access:      ${CONTROL_PLANE_ACCESS_MODE}"
    echo "Host bind:   ${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT}"
    echo "Volume:      ${CONTROL_PLANE_VOLUME_NAME}:/data"
    echo "Host data:   ${CONTROL_PLANE_HOST_DATA_ROOT}:${CONTROL_PLANE_HOST_DATA_ROOT}"
    echo "MEM data:    ${CONTROL_PLANE_MEM_DATA_ROOT}"
    echo "Instances:   ${CONTROL_PLANE_INSTANCE_DATA_ROOT}"
    echo "Coturn data: ${CONTROL_PLANE_COTURN_STORAGE_ROOT}"
    echo "Seq data:    ${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
    echo "Certificate: ${CONTROL_PLANE_CERT_PATH}"
    echo "Docker sock: /var/run/docker.sock"

    if [[ "${CONTROL_PLANE_RUNTIME_STATE}" == "legacy-container" ]]; then
        echo "Legacy name: ${LEGACY_CONTROL_PLANE_CONTAINER_NAME}"
        echo "Migration:   reviewed, health-verified, rollback-capable"
    fi

    if [[ "${CONTROL_PLANE_RUNTIME_STATE}" == "canonical-container" ]]; then
        echo "Observed:    ${CONTROL_PLANE_EXISTING_BIND_ADDRESS:-unknown}:${CONTROL_PLANE_EXISTING_BIND_PORT:-unknown} (${CONTROL_PLANE_EXISTING_BIND_STATE:-unknown})"

        if [[ "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED:-false}" == true ]]; then
            echo "Migration:   private-administration binding recreation required"
            echo "Reason:      ${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REASON}"
        elif [[ "${CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED:-false}" != true ]]; then
            echo "Binding:     existing canonical private binding is retained by this operation"
        fi
    fi

    echo
}

control_plane_certificate_sha256_fingerprint() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        return 0
    fi

    command -v openssl >/dev/null 2>&1 || return 0

    local certificate_pem fingerprint
    certificate_pem="$(read_volume_file "${CONTROL_PLANE_VOLUME_NAME}" "${CONTROL_PLANE_CERT_PATH}")"
    [[ -n "${certificate_pem}" ]] || return 0

    fingerprint="$(
        printf '%s\n' "${certificate_pem}" \
            | openssl x509 -noout -fingerprint -sha256 2>/dev/null \
            | sed -E 's/^sha256 Fingerprint=//I'
    )"

    [[ -n "${fingerprint}" ]] || return 0
    printf '%s\n' "${fingerprint}"
}

control_plane_first_owner_bootstrap_state() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        printf '%s\n' "unknown"
        return 0
    fi

    local base_url response compact
    base_url="$(control_plane_health_base_url)"
    response="$(
        curl --insecure --fail --silent --show-error \
            --connect-timeout 2 --max-time 5 \
            "${base_url}/api/auth/session" 2>/dev/null || true
    )"
    compact="$(printf '%s' "${response}" | tr -d '[:space:]')"

    if [[ "${compact}" == *'"requiresFirstOwnerBootstrap":true'* && \
          "${compact}" == *'"hasCompletedPlatformOwner":false'* ]]; then
        printf '%s\n' "required"
        return 0
    fi

    if [[ "${compact}" == *'"requiresFirstOwnerBootstrap":false'* && \
          "${compact}" == *'"hasCompletedPlatformOwner":true'* ]]; then
        printf '%s\n' "completed"
        return 0
    fi

    printf '%s\n' "unknown"
}

print_control_plane_setup_token_handoff() {
    [[ -n "${SETUP_TOKEN:-}" ]] || return 0

    local first_owner_state
    first_owner_state="$(control_plane_first_owner_bootstrap_state)"

    case "${first_owner_state}" in
        required)
            echo
            echo "Setup token"
            echo "-----------"
            echo
            echo "  ${SETUP_TOKEN}"
            echo
            echo "Keep this token private."
            echo "To show this token again later, run:"
            echo
            echo "  sudo ./install.sh --show-setup-token"
            ;;
        completed)
            log_info "Platform Owner enrollment is complete; the persisted setup token will not be displayed automatically."
            ;;
        *)
            log_warn "MEM could not prove that first-owner enrollment is still required, so the persisted setup token will not be displayed automatically. If first-owner enrollment is still pending, retrieve it explicitly with: sudo ./install.sh --show-setup-token"
            ;;
    esac
}

print_control_plane_certificate_guidance() {
    local fingerprint
    fingerprint="$(control_plane_certificate_sha256_fingerprint || true)"

    echo
    echo "Browser certificate"
    echo "-------------------"
    echo "MEM uses a locally generated self-signed HTTPS certificate for the private Control Plane."
    echo "A browser certificate warning is expected until you explicitly trust that certificate."

    if [[ -n "${fingerprint}" ]]; then
        echo "SHA-256 fingerprint:"
        echo
        echo "  ${fingerprint}"
        echo
        echo "Verify this fingerprint before accepting the browser certificate warning."
    else
        echo "SHA-256 fingerprint: unavailable."
        echo "Do not bypass a certificate warning you did not expect."
    fi
}

print_control_plane_access_details() {
    local ssh_command ssh_browser_url
    ssh_command="$(control_plane_ssh_tunnel_command)"
    ssh_browser_url="$(control_plane_ssh_browser_url)"

    echo
    echo "MEM Control Plane is ready"
    echo "--------------------------"
    echo
    echo "MEM 0.2.0 does not support directly exposing the Control Plane on a publicly routable interface."

    if [[ "${CONTROL_PLANE_ACCESS_MODE}" == "trusted-lan" ]]; then
        echo
        echo "Administration: Trusted LAN"
        echo "Host binding:   ${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT}"
        echo "Exposure:       selected private host address only"
        echo
        echo "Open from a device on this trusted network:"
        echo
        echo "  https://${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT}"
        echo
        echo "Do not publish this Control Plane port through Internet-facing NAT, firewall,"
        echo "public DNS, or a reverse proxy."
        echo
        echo "SSH remains available as a break-glass/private tunnel path:"
        echo
        echo "  ${ssh_command}"
        echo
        echo "Then open on your workstation:"
        echo
        echo "  ${ssh_browser_url}"
    else
        echo
        echo "Administration: SSH tunnel / local-only"
        echo "Host binding:   127.0.0.1:${CONTROL_PLANE_PORT}"
        echo "Exposure:       loopback only"
        echo
        echo "From your workstation, run:"
        echo
        echo "  ${ssh_command}"
        echo
        echo "Then open:"
        echo
        echo "  ${ssh_browser_url}"
        echo
        echo "Keep the SSH session open while using MEM. Press Ctrl-C in that terminal to close the tunnel."
        echo "There is no supported direct public Control Plane URL."
    fi

    print_control_plane_certificate_guidance

    print_control_plane_setup_token_handoff

    echo
}

run_control_plane_container() {
    bootstrap_set_phase "runtime-classification"
    bootstrap_set_operation "classify existing Control Plane runtime"
    resolve_control_plane_image
    detect_control_plane_state
    resolve_control_plane_access_for_detected_runtime

    case "${CONTROL_PLANE_RUNTIME_STATE}" in
        canonical-container)
            if [[ "${CONTROL_PLANE_PRIVATE_BINDING_MIGRATION_REQUIRED}" == true ]]; then
                migrate_canonical_control_plane_private_binding
            elif [[ "${CONTROL_PLANE_HOST_DATA_MIGRATION_REQUIRED}" == true ]]; then
                migrate_canonical_control_plane_host_data_mount
            else
                print_control_plane_launch_plan
                if [[ "${CONTROL_PLANE_CONTAINER_ALREADY_RUNNING}" == true ]]; then
                    if [[ "${DRY_RUN:-false}" == true ]]; then
                        log_info "Dry-run mode: canonical Control Plane is already running; no image, container, volume, certificate, or host-data mount change would occur."
                    else
                        prepare_setup_token
                        if ! wait_for_control_plane_health; then
                            capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
                            fail "The existing MEM Control Plane is running but did not pass health verification. No runtime was replaced."
                        fi
                    fi
                else
                    prepare_setup_token
                    start_existing_canonical_container
                fi
            fi
            print_control_plane_access_details
            ;;
        legacy-container)
            migrate_legacy_control_plane
            if [[ "${DRY_RUN:-false}" != true ]]; then
                print_control_plane_access_details
            fi
            ;;
        fresh|canonical-volume-only|unobserved)
            print_control_plane_launch_plan
            if [[ "${DRY_RUN:-false}" == true ]]; then
                log_info "Dry-run mode: would create or reuse the canonical volume and host-data root, preserve or create the canonical certificate, persist setup authority, pull the canonical image, start '${CONTROL_PLANE_CONTAINER_NAME}', and verify health."
                return 0
            fi

            bootstrap_set_phase "volume-preparation"
            bootstrap_set_operation "prepare Control Plane data volume and setup authority"
            create_control_plane_volume
            prepare_setup_token
            prepare_control_plane_host_data_root

            bootstrap_set_phase "certificate-preparation"
            bootstrap_set_operation "prepare Control Plane HTTPS certificate"
            ensure_control_plane_certificate

            bootstrap_set_phase "image-resolution"
            bootstrap_set_operation "resolve Control Plane runtime image"
            pull_control_plane_image

            bootstrap_set_phase "container-start"
            bootstrap_set_operation "start canonical MEM Control Plane"
            run_new_control_plane_container
            if ! wait_for_control_plane_health; then
                capture_control_plane_failure_evidence "${CONTROL_PLANE_CONTAINER_NAME}"
                bootstrap_set_operation "remove failed fresh Control Plane container"
                docker rm -f "${CONTROL_PLANE_CONTAINER_NAME}" >/dev/null 2>&1 || true
                fail "Fresh MEM Control Plane failed health verification. Persistent volume '${CONTROL_PLANE_VOLUME_NAME}' was retained."
            fi
            print_control_plane_access_details
            ;;
        unknown)
            fail "MEM could not determine the Control Plane runtime state."
            ;;
        *)
            fail "MEM cannot continue from Control Plane state '${CONTROL_PLANE_RUNTIME_STATE}'."
            ;;
    esac
}

run_control_plane_stage() {
    run_control_plane_container
}

# Transitional function alias for callers outside this bootstrap archive.
run_installer_stage() {
    run_control_plane_stage
}
