#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEV_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEV_ROOT}/.." && pwd)"

# shellcheck source=dev/mem-env
source "${DEV_ROOT}/mem-env"

fail_test() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

assert_true() {
    "$@" || fail_test "expected success: $*"
}

assert_false() {
    if "$@"; then
        fail_test "expected failure: $*"
    fi
}

assert_contains() {
    local file="$1"
    local expected="$2"
    grep -Fq -- "${expected}" "${file}" ||
        fail_test "${file} does not contain: ${expected}"
}

assert_not_contains() {
    local file="$1"
    local unexpected="$2"
    if grep -Fq -- "${unexpected}" "${file}"; then
        fail_test "${file} contains forbidden text: ${unexpected}"
    fi
}

assert_true validate_port 1
assert_true validate_port 5173
assert_true validate_port 65535
assert_false validate_port 0
assert_false validate_port 65536
assert_false validate_port invalid

# Containerized development is part of the release security boundary too. It
# must never regress from explicit loopback publication to Docker's wildcard
# default, because 01D runtime truth would correctly treat that as unsupported.
assert_contains "${DEV_ROOT}/compose.control-plane.yml" '127.0.0.1:${MEM_CONTROL_PLANE_HTTPS_PORT:-8443}:8443'
assert_not_contains "${DEV_ROOT}/compose.control-plane.yml" '"${MEM_CONTROL_PLANE_HTTPS_PORT:-8443}:8443"'

# Shared MEM-data repair is exact-ID-only. A same-slug manifest with another
# StackId must never satisfy an active durable row; if the exact manifest is
# absent, recovery reconstructs that exact identity from the durable database.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_STATE="${tmp}/interactive"
    INTERACTIVE_DATA_ROOT="${INTERACTIVE_STATE}/data"
    INTERACTIVE_MEM_DATA_ROOT="${INTERACTIVE_STATE}/mem-data"
    INTERACTIVE_MEM_DATA_AUTHORITY_FILE="${INTERACTIVE_STATE}/mem-data-authority.env"
    INTERACTIVE_AUTHORITY_FILE="${INTERACTIVE_STATE}/authority.env"
    INTERACTIVE_SETUP_TOKEN_PATH="${INTERACTIVE_DATA_ROOT}/setup-token"
    INTERACTIVE_SQLITE_PATH="${INTERACTIVE_DATA_ROOT}/mem-control-plane.dev.db"
    INTERACTIVE_HOST_DATA_ROOT="${tmp}/host-data"
    LEGACY_LOCAL_MEM_DATA_ROOT="${tmp}/legacy-mem-data"
    DEV_CONTAINER_NAME="mem-control-plane-dev-test"
    LOCAL_STATE="${tmp}/local"

    mkdir -p \
        "${INTERACTIVE_DATA_ROOT}" \
        "${INTERACTIVE_HOST_DATA_ROOT}" \
        "${INTERACTIVE_MEM_DATA_ROOT}/control-plane/runtime-stacks" \
        "${LEGACY_LOCAL_MEM_DATA_ROOT}/control-plane/runtime-stacks"
    printf 'token\n' > "${INTERACTIVE_SETUP_TOKEN_PATH}"
    write_env_file "${INTERACTIVE_AUTHORITY_FILE}" \
        "MEM_INTERACTIVE_AUTHORITY_VERSION=1" \
        "MEM_INTERACTIVE_AUTHORITY_SOURCE=test"
    write_mem_data_authority_metadata bad-fixture

    python3 - "${INTERACTIVE_SQLITE_PATH}" <<'PY'
import sqlite3, sys
con=sqlite3.connect(sys.argv[1])
con.execute('CREATE TABLE RuntimeStacks (Id TEXT, Slug TEXT, Status TEXT, LastVerifiedStatus TEXT, DataRoot TEXT, ManifestPath TEXT)')
con.execute('INSERT INTO RuntimeStacks VALUES (?, ?, ?, ?, ?, ?)', (
    '11111111-1111-1111-1111-111111111111', 'exact-stack', 'ready', 'public_routes_verified', '/old', '/old/exact.json'))
con.execute('INSERT INTO RuntimeStacks VALUES (?, ?, ?, ?, ?, ?)', (
    '44444444-4444-4444-4444-444444444444', 'same-slug-stack', 'ready', 'public_routes_verified', '/old', '/old/missing.json'))
con.execute('INSERT INTO RuntimeStacks VALUES (?, ?, ?, ?, ?, ?)', (
    '22222222-2222-2222-2222-222222222222', 'destroyed-stack', 'destroyed', 'destroyed', '/old', '/old/destroyed.json'))
con.commit()
con.close()
PY

    cat > "${LEGACY_LOCAL_MEM_DATA_ROOT}/control-plane/runtime-stacks/exact.json" <<'JSON'
{"stackId":"11111111-1111-1111-1111-111111111111","slug":"exact-stack"}
JSON
    cat > "${LEGACY_LOCAL_MEM_DATA_ROOT}/control-plane/runtime-stacks/wrong-same-slug.json" <<'JSON'
{"stackId":"99999999-9999-9999-9999-999999999999","slug":"same-slug-stack"}
JSON
    cat > "${LEGACY_LOCAL_MEM_DATA_ROOT}/control-plane/runtime-stacks/destroyed.json" <<'JSON'
{"stackId":"22222222-2222-2222-2222-222222222222","slug":"destroyed-stack"}
JSON
    cat > "${LEGACY_LOCAL_MEM_DATA_ROOT}/control-plane/runtime-stacks/stale.json" <<'JSON'
{"stackId":"33333333-3333-3333-3333-333333333333","slug":"stale-stack"}
JSON

    # Simulate the bad CORR-01 outcome: a canonical same-slug manifest with the
    # wrong durable identity and a ready marker. Exact-set validation must reject it.
    cat > "${INTERACTIVE_MEM_DATA_ROOT}/control-plane/runtime-stacks/wrong.json" <<'JSON'
{"stackId":"99999999-9999-9999-9999-999999999999","slug":"same-slug-stack"}
JSON
    if interactive_mem_data_authority_ready; then
        fail_test "same-slug wrong-ID manifest was accepted as ready MEM-data authority"
    fi

    require_docker() { :; }
    container_running() { return 1; }
    managed_process_running() { return 1; }
    port_is_listening() { return 1; }
    log() { :; }
    reconstruct_active_manifest_from_database() {
        local stack_id="$1"
        local output_path="$2"
        case "${stack_id,,}" in
            44444444-4444-4444-4444-444444444444)
                cat > "${output_path}" <<'JSON'
{"stackId":"44444444-4444-4444-4444-444444444444","slug":"same-slug-stack"}
JSON
                ;;
            *)
                fail_test "unexpected database reconstruction request: ${stack_id}"
                ;;
        esac
    }

    output="$(repair_interactive_mem_data)"
    [[ -f "${INTERACTIVE_MEM_DATA_ROOT}/control-plane/runtime-stacks/11111111111111111111111111111111.json" ]] ||
        fail_test "exact active durable manifest was not imported"
    [[ -f "${INTERACTIVE_MEM_DATA_ROOT}/control-plane/runtime-stacks/44444444444444444444444444444444.json" ]] ||
        fail_test "missing active manifest was not reconstructed from durable database rows"
    [[ ! -e "${INTERACTIVE_MEM_DATA_ROOT}/control-plane/runtime-stacks/wrong.json" ]] ||
        fail_test "same-slug wrong-ID canonical manifest survived exact identity repair"
    grep -Fq 'Active manifests reconstructed from durable database rows: 1' <<<"${output}" ||
        fail_test "MEM data repair did not report durable manifest reconstruction"
    grep -Fq 'Legacy manifests deliberately left retired: 3' <<<"${output}" ||
        fail_test "MEM data repair did not retire destroyed, stale, and same-slug wrong-ID legacy manifests"
    interactive_mem_data_authority_ready ||
        fail_test "MEM data repair did not create an exact identity authority"

    python3 - "${INTERACTIVE_SQLITE_PATH}" "${INTERACTIVE_MEM_DATA_ROOT}" <<'PY'
import sqlite3, sys
con=sqlite3.connect(sys.argv[1])
rows=con.execute('SELECT Id, DataRoot, ManifestPath FROM RuntimeStacks WHERE Status != "destroyed" ORDER BY Id').fetchall()
con.close()
assert len(rows) == 2, rows
for stack_id, data_root, manifest_path in rows:
    assert data_root == sys.argv[2], (stack_id, data_root)
    assert stack_id.lower().replace('-', '') in manifest_path.lower().replace('-', ''), (stack_id, manifest_path)
PY
)

assert_true is_recognized_control_plane_name mem-control-plane
assert_true is_recognized_control_plane_name mem-installer
assert_true is_recognized_control_plane_name mem-control-plane-dev
assert_true is_recognized_control_plane_name mem-control-plane-dev-e2e
assert_false is_recognized_control_plane_name mem-control-plane-random

[[ "$(seq_container_name_for_mode local)" == "mem-seq-local" ]] ||
    fail_test "local Seq container identity drifted"
[[ "$(seq_container_name_for_mode container)" == "mem-seq-dev" ]] ||
    fail_test "container Seq container identity drifted"
[[ "$(seq_runtime_mode_for_mode local)" == "local-development" ]] ||
    fail_test "local Seq runtime mode drifted"
[[ "$(seq_runtime_mode_for_mode container)" == "containerized-development" ]] ||
    fail_test "container Seq runtime mode drifted"
[[ "$(seq_preferred_host_port_for_mode local)" == "16341" ]] ||
    fail_test "local Seq preferred host port drifted"
[[ "$(seq_preferred_host_port_for_mode container)" == "17341" ]] ||
    fail_test "container Seq preferred host port drifted"
assert_false seq_container_name_for_mode unknown
assert_false seq_runtime_mode_for_mode unknown
assert_false seq_preferred_host_port_for_mode unknown

# Context matching is label-based and does not treat another development Seq as owned.
docker_container_label() {
    local name="$1" key="$2"
    case "${key}" in
        io.message-easy-mode.managed) printf '%s\n' true ;;
        io.message-easy-mode.service) printf '%s\n' seq ;;
        io.message-easy-mode.runtime-mode)
            [[ "${name}" == "mem-seq-local" ]] && printf '%s\n' local-development || printf '%s\n' containerized-development
            ;;
        *) printf '\n' ;;
    esac
}
assert_true seq_container_has_expected_context_labels local
assert_true seq_container_has_expected_context_labels container
# A conflicting runtime-mode label must fail closed.
docker_container_label() {
    local key="$2"
    case "${key}" in
        io.message-easy-mode.managed) printf '%s\n' true ;;
        io.message-easy-mode.service) printf '%s\n' seq ;;
        io.message-easy-mode.runtime-mode) printf '%s\n' production ;;
        *) printf '\n' ;;
    esac
}
assert_false seq_container_has_expected_context_labels local
assert_false seq_container_has_expected_context_labels container
# Restore the production helpers for the remaining tests.
# shellcheck source=dev/mem-env
source "${DEV_ROOT}/mem-env"

# Context-scoped reset cleanup removes only an exact, label-proven Seq identity.
REMOVED_SEQ=()
container_exists() { return 0; }
seq_container_has_expected_context_labels() { return 0; }
docker() {
    if [[ "$1" == "rm" && "$2" == "--force" ]]; then
        REMOVED_SEQ+=("$3")
        return 0
    fi
    return 1
}
log() { :; }
remove_scoped_seq_for_reset local
[[ "${REMOVED_SEQ[*]}" == "mem-seq-local" ]] ||
    fail_test "local reset cleanup targeted '${REMOVED_SEQ[*]}'"
REMOVED_SEQ=()
remove_scoped_seq_for_reset container
[[ "${REMOVED_SEQ[*]}" == "mem-seq-dev" ]] ||
    fail_test "container reset cleanup targeted '${REMOVED_SEQ[*]}'"
seq_container_has_expected_context_labels() { return 1; }
if (remove_scoped_seq_for_reset local >/dev/null 2>&1); then
    fail_test "Seq reset cleanup accepted conflicting ownership labels"
fi
# Restore again before the general Docker harness tests.
# shellcheck source=dev/mem-env
source "${DEV_ROOT}/mem-env"

assert_true require_reset_confirmation DELETE_MEM_DEV_STATE
assert_false require_reset_confirmation delete_mem_dev_state
assert_false require_reset_confirmation ""

[[ "${HOST_RESET_CONFIRMATION}" == "DELETE_MEM_DEV_HOST_STATE" ]] ||
    fail_test "host reset acknowledgement drifted"
assert_true host_reset_is_protected_control_plane_name mem-control-plane
assert_true host_reset_is_protected_control_plane_name mem-installer
assert_false host_reset_is_protected_control_plane_name mem-control-plane-dev
assert_true host_reset_is_legacy_mem_name mem-npm
assert_true host_reset_is_legacy_mem_name mem-matrix-demo
assert_false host_reset_is_legacy_mem_name portainer
assert_true host_reset_is_legacy_mem_volume_name mem-postgres-data
assert_true host_reset_is_legacy_mem_volume_name mem_postgres_data
assert_true host_reset_is_legacy_mem_volume_name mem_mem_npm_data
assert_false host_reset_is_legacy_mem_volume_name portainer_data
assert_false host_reset_is_legacy_mem_volume_name deltacore_drogon_pgdata
assert_true host_reset_is_mem_compose_project mem
assert_true host_reset_is_mem_compose_project mem-dev
assert_true host_reset_is_mem_compose_project mem-v010-dev
assert_true host_reset_is_mem_compose_project message-easy-mode-dev
assert_false host_reset_is_mem_compose_project portainer

# Clean-room discovery is label-first but keeps a bounded legacy mem-* namespace.
docker_container_label() {
    local name="$1" key="$2"
    if [[ "${name}" == "future-shared-service" &&
          "${key}" == "${MEM_MANAGED_LABEL}" ]]; then
        printf '%s\n' true
    fi
}
docker() {
    if [[ "$1" == "ps" && "$2" == "-a" ]]; then
        printf '%s\n' mem-npm mem-matrix-demo future-shared-service portainer mem-control-plane
        return 0
    fi
    return 1
}
actual="$(host_reset_list_target_containers false | paste -sd ',' -)"
[[ "${actual}" == "future-shared-service,mem-matrix-demo,mem-npm" ]] ||
    fail_test "host reset container inventory was '${actual}'"
actual="$(host_reset_list_target_containers true | paste -sd ',' -)"
[[ "${actual}" == "future-shared-service,mem-control-plane,mem-matrix-demo,mem-npm" ]] ||
    fail_test "host reset canonical inclusion inventory was '${actual}'"
actual="$(host_reset_list_protected_control_planes | paste -sd ',' -)"
[[ "${actual}" == "mem-control-plane" ]] ||
    fail_test "host reset protected inventory was '${actual}'"

docker_volume_label() {
    local name="$1" key="$2"
    if [[ "${name}" == "future-managed-volume" &&
          "${key}" == "${MEM_MANAGED_LABEL}" ]]; then
        printf '%s\n' true
        return 0
    fi
    if [[ "${name}" == "compose-owned-random-id" &&
          "${key}" == "${COMPOSE_PROJECT_LABEL}" ]]; then
        printf '%s\n' mem
        return 0
    fi
    if [[ "${name}" == "compose-owned-random-id" &&
          "${key}" == "${COMPOSE_VOLUME_LABEL}" ]]; then
        printf '%s\n' postgres_data
        return 0
    fi
    if [[ "${name}" == "unrelated-compose-volume" &&
          "${key}" == "${COMPOSE_PROJECT_LABEL}" ]]; then
        printf '%s\n' portainer
        return 0
    fi
}
docker() {
    if [[ "$1" == "volume" && "$2" == "ls" ]]; then
        printf '%s\n' \
            mem-postgres-data \
            mem_postgres_data \
            mem_mem_npm_data \
            future-managed-volume \
            compose-owned-random-id \
            unrelated-compose-volume \
            portainer_data
        return 0
    fi
    return 1
}
actual="$(host_reset_list_target_volumes | paste -sd ',' -)"
[[ "${actual}" == "compose-owned-random-id,future-managed-volume,mem-postgres-data,mem_mem_npm_data,mem_postgres_data" ]] ||
    fail_test "host reset volume inventory was '${actual}'"

# An unlabeled named volume is still reset-owned when it is mounted by an
# exact MEM-managed target container. This covers Portainer's historical
# Docker-created `portainer_data` volume without treating the name itself as
# globally MEM-owned.
docker_container_label() {
    local name="$1" key="$2"
    if [[ "${name}" == "portainer" &&
          "${key}" == "${MEM_MANAGED_LABEL}" ]]; then
        printf '%s\n' true
    fi
}
docker_volume_label() { :; }
docker() {
    if [[ "$1" == "ps" && "$2" == "-a" ]]; then
        printf '%s\n' portainer unrelated-portainer
        return 0
    fi
    if [[ "$1" == "volume" && "$2" == "ls" ]]; then
        printf '%s\n' portainer_data unrelated_portainer_data
        return 0
    fi
    if [[ "$1" == "inspect" ]]; then
        local container_name="${*: -1}"
        if [[ "${container_name}" == "portainer" ]]; then
            printf '%s\n' portainer_data
        elif [[ "${container_name}" == "unrelated-portainer" ]]; then
            printf '%s\n' unrelated_portainer_data
        fi
        return 0
    fi
    return 1
}
actual="$(host_reset_list_target_volumes | paste -sd ',' -)"
[[ "${actual}" == "portainer_data" ]] ||
    fail_test "host reset did not include the attachment-proven Portainer volume: '${actual}'"
reason="$(host_reset_resource_reason volume portainer_data)"
[[ "${reason}" == *"container=portainer"* ]] ||
    fail_test "host reset Portainer volume reason was '${reason}'"

docker_network_label() {
    local name="$1" key="$2"
    if [[ "${name}" == "future-managed-network" &&
          "${key}" == "${MEM_MANAGED_LABEL}" ]]; then
        printf '%s\n' true
    fi
}
docker() {
    if [[ "$1" == "network" && "$2" == "ls" ]]; then
        printf '%s\n' bridge host none mem-gateway future-managed-network portainer-network
        return 0
    fi
    return 1
}
actual="$(host_reset_list_target_networks | paste -sd ',' -)"
[[ "${actual}" == "future-managed-network,mem-gateway" ]] ||
    fail_test "host reset network inventory was '${actual}'"

# Restore production helpers before the remaining tests.
# shellcheck source=dev/mem-env
source "${DEV_ROOT}/mem-env"

fixture_root="$(mktemp -d)"
owned_process=""
cleanup() {
    if [[ -n "${owned_process}" ]] && kill -0 "${owned_process}" 2>/dev/null; then
        kill -TERM -- "-${owned_process}" 2>/dev/null || kill -TERM "${owned_process}" 2>/dev/null || true
    fi
    rm -rf "${fixture_root}"
}
trap cleanup EXIT

mkdir -p "${fixture_root}/exact"
touch "${fixture_root}/exact/proof"
safe_remove_directory "${fixture_root}/exact" "${fixture_root}/exact"
[[ ! -e "${fixture_root}/exact" ]] || fail_test "safe exact directory removal failed"


# Legacy local authority adoption must normalize the historical DB and key-ring names
# instead of allowing local/container modes to fork their identity state.
legacy_import="${fixture_root}/legacy-import"
mkdir -p "${legacy_import}/mem-data-protection-keys"
touch "${legacy_import}/mem-installer.dev.db" \
      "${legacy_import}/mem-installer.dev.db-wal" \
      "${legacy_import}/mem-installer.dev.db-shm" \
      "${legacy_import}/mem-data-protection-keys/key.xml"
canonicalize_legacy_local_data "${legacy_import}"
[[ -f "${legacy_import}/mem-control-plane.dev.db" ]] ||
    fail_test "legacy local DB was not normalized to the shared authority name"
[[ -f "${legacy_import}/mem-control-plane.dev.db-wal" ]] ||
    fail_test "legacy local DB WAL was not normalized"
[[ -f "${legacy_import}/mem-control-plane.dev.db-shm" ]] ||
    fail_test "legacy local DB SHM was not normalized"
[[ -f "${legacy_import}/data-protection-keys/key.xml" ]] ||
    fail_test "legacy Data Protection key ring was not normalized"
[[ ! -e "${legacy_import}/mem-installer.dev.db" ]] ||
    fail_test "historical local DB name remained after normalization"
[[ ! -e "${legacy_import}/mem-data-protection-keys" ]] ||
    fail_test "historical Data Protection key-ring directory remained after normalization"

if (reset_mode local --confirm DELETE_MEM_DEV_STATE >/dev/null 2>&1); then
    fail_test "mode-specific local reset remained enabled after shared authority"
fi
if (reset_mode container --confirm DELETE_MEM_DEV_STATE >/dev/null 2>&1); then
    fail_test "mode-specific container reset remained enabled after shared authority"
fi


# Local authority adoption must fail closed rather than retarget host-bound
# Matrix/TURN/Seq data that existing Docker containers may still reference.
LEGACY_LOCAL_DATA_ROOT="${fixture_root}/legacy-local"
mkdir -p "${LEGACY_LOCAL_DATA_ROOT}" "${fixture_root}/legacy-instances" "${fixture_root}/legacy-coturn"
legacy_local_host_paths() {
    printf '%s\n' "${fixture_root}/legacy-instances" "${fixture_root}/legacy-coturn"
}
touch "${fixture_root}/legacy-instances/live-resource"
if (assert_legacy_local_resource_adoption_safe >/dev/null 2>&1); then
    fail_test "local authority adoption accepted live legacy instance data"
fi
rm -f "${fixture_root}/legacy-instances/live-resource"
assert_true assert_legacy_local_resource_adoption_safe
# Restore production constants/helpers for subsequent tests.
# shellcheck source=dev/mem-env
source "${DEV_ROOT}/mem-env"

mkdir -p "${fixture_root}/privileged/locked"
touch "${fixture_root}/privileged/locked/proof"
chmod 0555 "${fixture_root}/privileged/locked"
assert_true host_reset_directory_requires_privileged_cleanup \
    "${fixture_root}/privileged" "${fixture_root}/privileged"
chmod 0755 "${fixture_root}/privileged/locked"
assert_false host_reset_directory_requires_privileged_cleanup \
    "${fixture_root}/privileged" "${fixture_root}/privileged"
rm -rf "${fixture_root}/privileged"

setsid sleep 30 >/dev/null 2>&1 &
owned_process=$!
sleep 0.1
assert_true pid_matches "${owned_process}" sleep
assert_false pid_matches "${owned_process}" dotnet
kill -TERM -- "-${owned_process}" 2>/dev/null || true
wait "${owned_process}" 2>/dev/null || true
owned_process=""

runtime_json() {
    printf '%s' '{"runtimeMode":"containerized-development","uiDeliveryMode":"embedded-spa"}'
}
assert_true assert_runtime unused containerized-development embedded-spa false
if assert_runtime unused local-development vite false >/dev/null 2>&1; then
    fail_test "runtime mismatch was accepted"
fi

# Verify conflict filtering without requiring a real Docker daemon.
docker() {
    if [[ "$1" == "ps" ]]; then
        printf '%s\n' mem-control-plane-dev mem-installer unrelated-service
        return 0
    fi
    return 1
}
actual="$(running_control_plane_names | paste -sd ',' -)"
[[ "${actual}" == "mem-control-plane-dev,mem-installer" ]] ||
    fail_test "recognized controller inventory was '${actual}'"
actual="$(competing_control_plane_names mem-control-plane-dev | paste -sd ',' -)"
[[ "${actual}" == "mem-installer" ]] ||
    fail_test "competing controller inventory was '${actual}'"

# Runtime networks are reused when present and created explicitly when absent.
NETWORK_EVENTS=()
docker() {
    if [[ "$1" == "network" && "$2" == "inspect" ]]; then
        [[ "$3" == "existing-gateway" ]]
        return
    fi
    if [[ "$1" == "network" && "$2" == "create" ]]; then
        NETWORK_EVENTS+=("$*")
        return 0
    fi
    return 1
}
assert_true ensure_runtime_network existing-gateway
[[ "${#NETWORK_EVENTS[@]}" -eq 0 ]] ||
    fail_test "existing runtime network was recreated"
assert_true ensure_runtime_network missing-gateway
[[ "${#NETWORK_EVENTS[@]}" -eq 1 ]] ||
    fail_test "missing runtime network was not created exactly once"
[[ "${NETWORK_EVENTS[0]}" == *"--driver"* && "${NETWORK_EVENTS[0]}" == *"bridge"* && "${NETWORK_EVENTS[0]}" == *"${MEM_MANAGED_LABEL}=true"* && "${NETWORK_EVENTS[0]}" == *"${MEM_RESOURCE_LABEL}=development-network"* && "${NETWORK_EVENTS[0]}" == *"missing-gateway"* ]] ||
    fail_test "runtime network creation did not request the expected managed bridge network"

# Shared Control Plane state and canonical HostAgent MEM data are both
# Control-Plane-owned developer state. Container mode may create entries as
# root, so the transition boundary must normalize both bind roots before a
# local source API is allowed to own the same authority.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_DATA_ROOT="${tmp}/interactive/data"
    INTERACTIVE_MEM_DATA_ROOT="${tmp}/interactive/mem-data"
    mkdir -p \
        "${INTERACTIVE_DATA_ROOT}/diagnostics" \
        "${INTERACTIVE_MEM_DATA_ROOT}/backups/stacks"

    DOCKER_EVENTS=()
    require_docker() { :; }
    docker() { DOCKER_EVENTS+=("$*"); }

    normalize_interactive_local_ownership

    [[ "${#DOCKER_EVENTS[@]}" -eq 1 ]] ||
        fail_test "interactive ownership normalization did not use one bounded helper container"
    ownership_command="${DOCKER_EVENTS[0]}"
    [[ "${ownership_command}" == *"-v ${INTERACTIVE_DATA_ROOT}:/control-plane-data"* ]] ||
        fail_test "interactive ownership normalization omitted the shared Control Plane data root"
    [[ "${ownership_command}" == *"-v ${INTERACTIVE_MEM_DATA_ROOT}:/mem-data"* ]] ||
        fail_test "interactive ownership normalization omitted the canonical MEM data root"
    [[ "${ownership_command}" == *"chown -R $(id -u):$(id -g) /control-plane-data /mem-data"* ]] ||
        fail_test "interactive ownership normalization did not target both shared developer-state roots"
)

# Local preparation must invoke the same shared-state normalization before
# checking the separately-owned stack host-data ACL contract.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    LOCAL_STATE="${tmp}/local"
    INTERACTIVE_DATA_ROOT="${tmp}/interactive/data"
    INTERACTIVE_MEM_DATA_ROOT="${tmp}/interactive/mem-data"
    LOCAL_API_URL="http://127.0.0.1:7105"
    LOCAL_WEB_URL="http://127.0.0.1:5173"
    order_file="${tmp}/order"

    ensure_interactive_authority() { :; }
    normalize_interactive_local_ownership() { printf 'normalize\n' >> "${order_file}"; }
    prepare_interactive_host_data_for_local_mutation() { printf 'host-data\n' >> "${order_file}"; }
    write_env_file() { :; }

    ensure_local_environment

    [[ "$(paste -sd ',' "${order_file}")" == "normalize,host-data" ]] ||
        fail_test "local environment preparation did not normalize shared state before host-data verification"
)

# Local source mode must never try to grant itself privilege. Shared
# container-owned host data is reconciled only through the explicit bounded
# `host-data access` command; container ownership remains unchanged.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    CONTAINER_STATE="${tmp}/container"
    INTERACTIVE_STATE="${tmp}/interactive"
    INTERACTIVE_HOST_DATA_ROOT="${CONTAINER_STATE}/host-data"
    INTERACTIVE_HOST_DATA_ACCESS_FILE="${INTERACTIVE_STATE}/host-data-access.env"
    mkdir -p \
        "${INTERACTIVE_STATE}" \
        "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a/media_store" \
        "${INTERACTIVE_HOST_DATA_ROOT}/platform/coturn"
    printf 'server_name: example.test\n' \
        > "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a/homeserver.yaml"
    printf 'media\n' \
        > "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a/media_store/payload.bin"
    printf 'protected\n' \
        > "${INTERACTIVE_HOST_DATA_ROOT}/platform/coturn/turnserver.conf"

    fake_bin="${tmp}/bin"
    acl_log="${tmp}/acl.log"
    mkdir -p "${fake_bin}"

    cat > "${fake_bin}/setfacl" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
printf 'setfacl %s\n' "$*" >> "${MEM_TEST_ACL_LOG}"
SH

    cat > "${fake_bin}/sudo" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${1:-}" == "-v" ]]; then
    printf 'sudo -v\n' >> "${MEM_TEST_ACL_LOG}"
    exit 0
fi
[[ "${1:-}" == "--" ]] && shift
printf 'sudo %s\n' "$*" >> "${MEM_TEST_ACL_LOG}"
"$@"
SH

    chmod +x "${fake_bin}/setfacl" "${fake_bin}/sudo"
    export MEM_TEST_ACL_LOG="${acl_log}"
    PATH="${fake_bin}:${PATH}"

    ensure_interactive_authority() { :; }
    require_docker() { :; }
    container_running() { return 1; }
    port_is_listening() { return 1; }

    host_data_access_apply

    developer_uid="$(id -u)"
    grep -Fq -- 'sudo -v' "${acl_log}" ||
        fail_test "shared host-data access did not authorize privilege before ACL mutation"
    grep -Fq -- "u:${developer_uid}:rwx" "${acl_log}" ||
        fail_test "shared host-data access did not grant directory rwx to the current developer UID"
    grep -Fq -- "d:u:${developer_uid}:rwx" "${acl_log}" ||
        fail_test "shared host-data access did not establish the developer default directory ACL"
    grep -Fq -- "u:${developer_uid}:rw-" "${acl_log}" ||
        fail_test "shared host-data access did not grant file rw access to the current developer UID"
    grep -Fq -- "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a" "${acl_log}" ||
        fail_test "shared host-data access did not cover an existing container-created stack control directory"
    grep -Fq -- "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a/homeserver.yaml" "${acl_log}" ||
        fail_test "shared host-data access did not cover an existing stack configuration file"
    if grep -Fq -- '/media_store/payload.bin' "${acl_log}"; then
        fail_test "shared host-data access traversed Matrix media payloads"
    fi
    if grep -Fq -- '/platform/coturn' "${acl_log}"; then
        fail_test "shared host-data access weakened the protected Coturn owner-only permission boundary"
    fi
    [[ -f "${INTERACTIVE_HOST_DATA_ACCESS_FILE}" ]] ||
        fail_test "shared host-data access did not record its bounded authority marker"
    grep -Fq -- 'MEM_DEV_HOST_DATA_ACCESS_VERSION=2' "${INTERACTIVE_HOST_DATA_ACCESS_FILE}" ||
        fail_test "shared host-data access marker did not record the privileged ACL contract version"

    # `local up` preparation is verification-only. If access is not usable it
    # must fail with explicit guidance instead of invoking setfacl or sudo.
    interactive_host_data_verify_local_access() { return 1; }
    before_lines="$(wc -l < "${acl_log}")"
    prepare_error="$(
        (prepare_interactive_host_data_for_local_mutation) 2>&1 || true
    )"
    after_lines="$(wc -l < "${acl_log}")"
    [[ "${before_lines}" == "${after_lines}" ]] ||
        fail_test "local host-data preparation attempted a privileged ACL mutation"
    [[ "${prepare_error}" == *"./dev/mem-env host-data access"* ]] ||
        fail_test "local host-data preparation did not direct the developer to the explicit access command"
)

# Access verification must treat an untraversable container-created control
# directory as "repair required" without leaking raw find permission errors.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    CONTAINER_STATE="${tmp}/container"
    INTERACTIVE_HOST_DATA_ROOT="${CONTAINER_STATE}/host-data"
    mkdir -p "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a"

    find() {
        printf 'simulated find: Permission denied\n' >&2
        return 1
    }

    verify_output=""
    if verify_output="$(interactive_host_data_verify_local_access 2>&1)"; then
        fail_test "shared host-data access verification accepted an untraversable control tree"
    fi
    [[ -z "${verify_output}" ]] ||
        fail_test "shared host-data access verification leaked raw traversal errors: ${verify_output}"
)

# A pre-repair symlink scan may be inconclusive because the developer UID cannot
# yet traverse container-owned control directories. `switch local` must continue
# into the privileged bounded reconciliation, which performs the authoritative
# symlink check before applying recursive ACLs.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_HOST_DATA_ROOT="${tmp}/host-data"
    mkdir -p "${INTERACTIVE_HOST_DATA_ROOT}/instances"
    events="${tmp}/events"
    verify_count=0

    require_command() { :; }
    interactive_host_data_validate_root() { :; }
    interactive_host_data_scope_has_symlink() { return 2; }
    interactive_host_data_verify_local_access() {
        verify_count=$((verify_count + 1))
        [[ "${verify_count}" -ge 2 ]]
    }
    host_data_access_apply() { printf 'repair\n' >> "${events}"; }
    log() { :; }

    reconcile_interactive_host_data_for_local_switch

    [[ "$(cat "${events}")" == "repair" ]] ||
        fail_test "switch-local reconciliation did not continue into privileged repair after an inconclusive developer traversal"
)

# `switch local` is an explicit cross-runtime transition, so it may reconcile
# the already-bounded host-data ACL after containerized development has stopped.
# A healthy ACL must not invoke the privileged repair path.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_HOST_DATA_ROOT="${tmp}/host-data"
    mkdir -p "${INTERACTIVE_HOST_DATA_ROOT}/instances"
    events="${tmp}/events"

    require_command() { :; }
    interactive_host_data_validate_root() { :; }
    interactive_host_data_scope_has_symlink() { return 1; }
    interactive_host_data_verify_local_access() { return 0; }
    host_data_access_apply() { printf 'repair\n' >> "${events}"; }
    log() { :; }

    reconcile_interactive_host_data_for_local_switch

    [[ ! -s "${events}" ]] ||
        fail_test "switch-local host-data reconciliation invoked privilege for an already-writable ACL"
)

# When access has drifted, `switch local` must repair it after stopping the
# container and before starting local development. Direct `local up` remains
# verification-only and is covered above.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_HOST_DATA_ROOT="${tmp}/host-data"
    mkdir -p "${INTERACTIVE_HOST_DATA_ROOT}/instances"
    events="${tmp}/events"
    verify_count=0

    command() {
        if [[ "${1:-}" == "-v" && "${2:-}" == "docker" ]]; then
            return 0
        fi
        builtin command "$@"
    }
    docker() { return 0; }
    container_exists() { return 0; }
    runtime_control_plane_instance_for_mode() { printf 'shared-instance\n'; }
    container_down() { printf 'container-down\n' >> "${events}"; }
    require_command() { :; }
    interactive_host_data_validate_root() { :; }
    interactive_host_data_scope_has_symlink() { return 1; }
    interactive_host_data_verify_local_access() {
        verify_count=$((verify_count + 1))
        [[ "${verify_count}" -ge 2 ]]
    }
    host_data_access_apply() { printf 'repair\n' >> "${events}"; }
    local_up() { printf 'local-up\n' >> "${events}"; }
    verify_switched_authority() { printf 'verify-switch\n' >> "${events}"; }
    log() { :; }

    switch_mode local

    [[ "$(paste -sd ',' "${events}")" == "container-down,repair,local-up,verify-switch" ]] ||
        fail_test "switch local did not reconcile host-data between container shutdown and local startup: $(paste -sd ',' "${events}")"
)

# A failed ACL reconciliation must fail closed and never start local development.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    INTERACTIVE_HOST_DATA_ROOT="${tmp}/host-data"
    mkdir -p "${INTERACTIVE_HOST_DATA_ROOT}/instances"
    events="${tmp}/events"

    require_command() { :; }
    interactive_host_data_validate_root() { :; }
    interactive_host_data_scope_has_symlink() { return 1; }
    interactive_host_data_verify_local_access() { return 1; }
    host_data_access_apply() { die "simulated ACL repair failure"; }
    local_up() { printf 'local-up\n' >> "${events}"; }
    log() { :; }

    if (reconcile_interactive_host_data_for_local_switch >/dev/null 2>&1); then
        fail_test "switch-local host-data reconciliation accepted a failed ACL repair"
    fi
    [[ ! -s "${events}" ]] ||
        fail_test "local development started after failed automatic host-data reconciliation"
)

# Managed local development defaults to full API restart-on-save. The explicit
# --no-watch escape hatch retains the historical one-shot API process.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT
    events="${tmp}/events"

    local_up() { printf 'up:%s\n' "${1:-<unset>}" >> "${events}"; }
    local_down() { printf 'down\n' >> "${events}"; }

    local_command up
    local_command up --no-watch
    local_command up --watch
    local_command restart --no-watch

    [[ "$(paste -sd ',' "${events}")" == "up:true,up:false,up:true,down,up:false" ]] ||
        fail_test "managed local watch defaults/options drifted: $(paste -sd ',' "${events}")"
)

# Managed processes are launched asynchronously by this Bash harness. Without
# an explicit reset, Bash marks SIGINT/SIGQUIT ignored in the background child
# before exec, so kill -INT cannot provide Ctrl+C semantics to dotnet watch.
# Exercise the real launcher rather than mocking the shutdown selection so this
# regression catches signal-disposition drift.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    pid_file="${tmp}/managed.pid"
    log_file="${tmp}/managed.log"

    start_managed_process \
        "signal fixture" \
        "${pid_file}" \
        "${log_file}" \
        "sleep 30" \
        sleep 30

    pid="$(read_pid_file "${pid_file}")"
    sig_ign="$(awk '/^SigIgn:/ { print $2 }' "/proc/${pid}/status")"
    python3 - "${sig_ign}" <<'PY_SIGNAL_MASK'
import sys
mask = int(sys.argv[1], 16)
for signum, name in ((2, "SIGINT"), (3, "SIGQUIT")):
    if mask & (1 << (signum - 1)):
        raise SystemExit(f"{name} is still ignored by the managed child: 0x{mask:x}")
PY_SIGNAL_MASK

    stop_managed_process "signal fixture" "${pid_file}" "sleep 30" INT
    if pid_is_running "${pid}"; then
        fail_test "managed child remained alive after graceful SIGINT"
    fi
)

# Watch-mode local shutdown must request Ctrl+C semantics from dotnet watch.
# SIGTERM can stop only the watched ASP.NET Core child and leave the watcher
# alive in its "waiting for a file to change" state, which previously forced
# ordinary runtime switching down the SIGKILL fallback. One-shot dotnet run
# keeps the ordinary SIGTERM contract.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    LOCAL_STATE="${tmp}/local"
    mkdir -p "${LOCAL_STATE}"
    printf '41001\n' > "${LOCAL_STATE}/api.pid"
    printf '41002\n' > "${LOCAL_STATE}/vite.pid"
    events="${tmp}/events"

    managed_process_running() {
        [[ "$1" == "${LOCAL_STATE}/api.pid" && "$2" == "dotnet watch" ]]
    }
    stop_managed_process() {
        printf '%s|%s\n' "$1" "${4:-TERM}" >> "${events}"
    }

    local_down

    [[ "$(paste -sd ',' "${events}")" == "Vite UI|TERM,local API|INT" ]] ||
        fail_test "watch-mode local shutdown did not use SIGINT for dotnet watch: $(paste -sd ',' "${events}")"
)

(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    LOCAL_STATE="${tmp}/local"
    mkdir -p "${LOCAL_STATE}"
    printf '42001\n' > "${LOCAL_STATE}/api.pid"
    printf '42002\n' > "${LOCAL_STATE}/vite.pid"
    events="${tmp}/events"

    managed_process_running() { return 1; }
    stop_managed_process() {
        printf '%s|%s\n' "$1" "${4:-TERM}" >> "${events}"
    }

    local_down

    [[ "$(paste -sd ',' "${events}")" == "Vite UI|TERM,local API|TERM" ]] ||
        fail_test "one-shot local shutdown did not preserve SIGTERM semantics: $(paste -sd ',' "${events}")"
)

# The generic managed-process stop primitive must honor an explicitly selected
# graceful signal while preserving SIGKILL as the bounded last-resort fallback.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    pid_file="${tmp}/managed.pid"
    events="${tmp}/events"
    printf '43001\n' > "${pid_file}"
    signaled=false

    pid_matches() { return 0; }
    pid_is_running() { [[ "${signaled}" != true ]]; }
    kill() {
        printf '%s|%s|%s\n' "${1:-}" "${2:-}" "${3:-}" >> "${events}"
        signaled=true
        return 0
    }
    sleep() { :; }
    log() { :; }
    warn() { :; }

    stop_managed_process "watch fixture" "${pid_file}" "fixture" INT

    grep -Fxq -- '-INT|--|-43001' "${events}" ||
        fail_test "managed-process shutdown did not signal the owned process group with SIGINT"
    if grep -Fq -- '-KILL' "${events}"; then
        fail_test "managed-process shutdown used SIGKILL after successful graceful SIGINT"
    fi
)

(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    pid_file="${tmp}/managed.pid"
    events="${tmp}/events"
    printf '44001\n' > "${pid_file}"

    pid_matches() { return 0; }
    pid_is_running() { return 0; }
    kill() {
        printf '%s|%s|%s\n' "${1:-}" "${2:-}" "${3:-}" >> "${events}"
        return 0
    }
    sleep() { :; }
    log() { :; }
    warn() { :; }

    stop_managed_process "stuck fixture" "${pid_file}" "fixture" INT

    grep -Fxq -- '-INT|--|-44001' "${events}" ||
        fail_test "managed-process fallback fixture did not attempt graceful SIGINT first"
    grep -Fxq -- '-KILL|--|-44001' "${events}" ||
        fail_test "managed-process fallback no longer retains bounded SIGKILL recovery"
)

assert_contains "${DEV_ROOT}/mem-env" 'dotnet watch'
assert_contains "${DEV_ROOT}/mem-env" '--no-hot-reload'
assert_contains "${DEV_ROOT}/mem-env" '--non-interactive'
assert_contains "${DEV_ROOT}/mem-env" 'API source watch'
assert_contains "${DEV_ROOT}/mem-env" 'local up [--watch|--no-watch]'

# Symlinks in the managed control scope must fail closed. Matrix media payloads
# are deliberately outside the recursive ACL bootstrap.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    CONTAINER_STATE="${tmp}/container"
    INTERACTIVE_STATE="${tmp}/interactive"
    INTERACTIVE_HOST_DATA_ROOT="${CONTAINER_STATE}/host-data"
    INTERACTIVE_HOST_DATA_ACCESS_FILE="${INTERACTIVE_STATE}/host-data-access.env"
    mkdir -p \
        "${INTERACTIVE_STATE}" \
        "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a"
    ln -s /tmp "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-a/matrix-a/unsafe-link"

    ensure_interactive_authority() { :; }
    require_docker() { :; }
    container_running() { return 1; }
    port_is_listening() { return 1; }
    sudo() {
        if [[ "${1:-}" == "-v" ]]; then
            return 0
        fi
        [[ "${1:-}" == "--" ]] && shift
        "$@"
    }

    if (host_data_access_apply >/dev/null 2>&1); then
        fail_test "shared host-data access accepted a symlink inside the managed stack control scope"
    fi
)

# The authoritative symlink scan must run through the privileged traversal.
# Simulate a link that the ordinary developer traversal cannot discover but the
# sudo traversal can see; ACL mutation must still fail closed before setfacl.
(
    tmp="$(mktemp -d)"
    trap 'rm -rf -- "${tmp}"' EXIT

    CONTAINER_STATE="${tmp}/container"
    INTERACTIVE_STATE="${tmp}/interactive"
    INTERACTIVE_HOST_DATA_ROOT="${CONTAINER_STATE}/host-data"
    INTERACTIVE_HOST_DATA_ACCESS_FILE="${INTERACTIVE_STATE}/host-data-access.env"
    mkdir -p \
        "${INTERACTIVE_STATE}" \
        "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-hidden/matrix-hidden"

    fake_bin="${tmp}/bin"
    mutation_log="${tmp}/mutation.log"
    mkdir -p "${fake_bin}"

    real_find="$(command -v find)"
    cat > "${fake_bin}/find" <<SH
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "\${MEM_TEST_PRIVILEGED_FIND:-0}" == "1" && " \$* " == *" -type l "* ]]; then
    printf '%s\n' "${INTERACTIVE_HOST_DATA_ROOT}/instances/stack-hidden/matrix-hidden/hidden-link"
    exit 0
fi
exec "${real_find}" "\$@"
SH

    cat > "${fake_bin}/setfacl" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
printf 'setfacl %s\n' "$*" >> "${MEM_TEST_MUTATION_LOG}"
SH

    cat > "${fake_bin}/sudo" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${1:-}" == "-v" ]]; then
    exit 0
fi
[[ "${1:-}" == "--" ]] && shift
MEM_TEST_PRIVILEGED_FIND=1 "$@"
SH

    chmod +x "${fake_bin}/find" "${fake_bin}/setfacl" "${fake_bin}/sudo"
    export MEM_TEST_MUTATION_LOG="${mutation_log}"
    PATH="${fake_bin}:${PATH}"
    hash -r

    ensure_interactive_authority() { :; }
    require_docker() { :; }
    container_running() { return 1; }
    port_is_listening() { return 1; }

    if (host_data_access_apply >/dev/null 2>&1); then
        fail_test "shared host-data access accepted a symlink visible only to privileged traversal"
    fi
    [[ ! -s "${mutation_log}" ]] ||
        fail_test "shared host-data access mutated ACLs before the privileged symlink safety scan completed"
)

# The developer ACL contract must remain explicit and developer-only.
assert_contains "${DEV_ROOT}/mem-env" './dev/mem-env host-data access'
assert_contains "${DEV_ROOT}/mem-env" 'sudo -v'
assert_contains "${DEV_ROOT}/mem-env" 'media_store'
assert_not_contains "${DEV_ROOT}/mem-env" 'chmod -R 777'
assert_not_contains "${DEV_ROOT}/mem-env" 'chown -R "$(id -u):$(id -g)" "${INTERACTIVE_HOST_DATA_ROOT}"'

# Normal container down must preserve the shared interactive bind-mounted state and runtime network.
EVENTS=()
NORMALIZE_CALLS=0
require_docker() { :; }
ensure_container_environment() { :; }
normalize_interactive_local_ownership() { NORMALIZE_CALLS=$((NORMALIZE_CALLS + 1)); }
compose() { EVENTS+=("$*"); }
container_down >/dev/null
[[ "${EVENTS[*]}" == "down --remove-orphans" ]] ||
    fail_test "container down was not state-preserving: ${EVENTS[*]}"
[[ "${NORMALIZE_CALLS}" -eq 1 ]] ||
    fail_test "container down did not normalize shared developer-owned data exactly once"

assert_contains "${REPO_ROOT}/installer/Dockerfile" 'FROM ${DOTNET_RUNTIME_IMAGE} AS runtime'
assert_contains "${REPO_ROOT}/installer/Dockerfile" "COPY --from=web-build"
assert_contains "${REPO_ROOT}/installer/Dockerfile" "ENTRYPOINT [\"dotnet\", \"Api.dll\"]"
assert_not_contains "${REPO_ROOT}/installer/Dockerfile" "mem_test123"

assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'container_name: ${MEM_CONTROL_PLANE_CONTAINER_NAME:-mem-control-plane-dev}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" "image: \${MEM_CONTROL_PLANE_IMAGE:-mem-control-plane:local}"
assert_contains "${DEV_ROOT}/compose.control-plane.yml" "MEM_RUNTIME_MODE: containerized-development"
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'App__PublicBaseUrl: "https://127.0.0.1:${MEM_CONTROL_PLANE_HTTPS_PORT:-8443}"'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'MemRuntime__ContainerName: ${MEM_CONTROL_PLANE_CONTAINER_NAME:-mem-control-plane-dev}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" '${MEM_CONTROL_PLANE_STATE_ROOT}:/data'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" '${MEM_CONTROL_PLANE_MEM_DATA_ROOT}:${MEM_CONTROL_PLANE_MEM_DATA_ROOT}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" '${MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT}:${MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'MEM_DATA_ROOT: ${MEM_CONTROL_PLANE_MEM_DATA_ROOT}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" '${MEM_CONTROL_PLANE_HOST_DATA_ROOT}:${MEM_CONTROL_PLANE_HOST_DATA_ROOT}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'Provisioning__InstanceDataRoot: ${MEM_CONTROL_PLANE_HOST_DATA_ROOT}/instances'
assert_not_contains "${DEV_ROOT}/compose.control-plane.yml" 'MEM_CONTROL_PLANE_VOLUME_NAME'
assert_not_contains "${DEV_ROOT}/compose.control-plane.yml" 'mem-control-plane-dev-data:/data'
assert_not_contains "${DEV_ROOT}/compose.control-plane.yml" "MEM_CONTROL_PLANE_SETUP_TOKEN:"
assert_contains "${REPO_ROOT}/.gitignore" "/dev/.state/"
[[ ! -e "${DEV_ROOT}/.gitignore" ]] || fail_test "dev/.gitignore should not duplicate the root ignore policy"

assert_contains "${DEV_ROOT}/compose.control-plane.yml" '${MEM_CONTROL_PLANE_COMPOSE_PROJECT:-mem-control-plane-dev}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'name: ${MEM_CONTROL_PLANE_NETWORK_NAME:-mem-gateway}'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'mem-runtime:'
assert_contains "${DEV_ROOT}/compose.control-plane.yml" 'external: true'
assert_not_contains "${DEV_ROOT}/compose.control-plane.yml" 'name: ${MEM_CONTROL_PLANE_NETWORK_NAME:-mem-control-plane-dev}'

assert_contains "${DEV_ROOT}/mem-env" 'INTERACTIVE_STATE="${STATE_ROOT}/interactive"'
assert_contains "${DEV_ROOT}/mem-env" 'INTERACTIVE_DATA_ROOT="${INTERACTIVE_STATE}/data"'
assert_contains "${DEV_ROOT}/mem-env" 'INTERACTIVE_MEM_DATA_ROOT="${INTERACTIVE_STATE}/mem-data"'
assert_contains "${DEV_ROOT}/mem-env" 'normalize_interactive_local_ownership'
assert_contains "${DEV_ROOT}/mem-env" 'LEGACY_LOCAL_MEM_DATA_ROOT="${HOME}/mem-data"'
assert_contains "${DEV_ROOT}/mem-env" 'INTERACTIVE_HOST_DATA_ROOT="${CONTAINER_STATE}/host-data"'
assert_contains "${DEV_ROOT}/mem-env" 'LEGACY_CONTAINER_VOLUME_NAME="mem-control-plane-dev-data"'
assert_contains "${DEV_ROOT}/mem-env" 'MEM_CONTROL_PLANE_STATE_ROOT=${INTERACTIVE_DATA_ROOT}'
assert_contains "${DEV_ROOT}/mem-env" 'MEM_CONTROL_PLANE_MEM_DATA_ROOT=${INTERACTIVE_MEM_DATA_ROOT}'
assert_contains "${DEV_ROOT}/mem-env" 'MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT=${LEGACY_LOCAL_MEM_DATA_ROOT}'
assert_contains "${DEV_ROOT}/mem-env" 'MEM_CONTROL_PLANE_HOST_DATA_ROOT=${INTERACTIVE_HOST_DATA_ROOT}'
assert_contains "${DEV_ROOT}/mem-env" 'authority adopt local|container'
assert_contains "${DEV_ROOT}/mem-env" 'authority repair-mem-data'
assert_contains "${DEV_ROOT}/mem-env" 'Legacy manifests deliberately left retired'
assert_contains "${DEV_ROOT}/mem-env" 'reset interactive --confirm DELETE_MEM_DEV_STATE'
assert_contains "${DEV_ROOT}/mem-env" 'Shared Control Plane identity preserved across switch'
assert_contains "${DEV_ROOT}/mem-env" 'DEV_NETWORK_NAME="${MEM_CONTROL_PLANE_NETWORK_NAME:-mem-gateway}"'
assert_contains "${DEV_ROOT}/mem-env" 'ensure_runtime_network "${DEV_NETWORK_NAME}"'
assert_contains "${DEV_ROOT}/mem-env" 'ensure_runtime_network "${E2E_NETWORK_NAME}"'
assert_contains "${DEV_ROOT}/mem-env" 'docker network rm "${E2E_NETWORK_NAME}"'
assert_contains "${DEV_ROOT}/mem-env" 'E2E_CONTAINER_DATA_ROOT="${E2E_CONTAINER_STATE}/data"'
assert_contains "${DEV_ROOT}/mem-env" 'E2E_LOCAL_MEM_DATA_ROOT="${E2E_LOCAL_STATE}/mem-data"'
assert_contains "${DEV_ROOT}/mem-env" 'E2E_CONTAINER_MEM_DATA_ROOT="${E2E_CONTAINER_STATE}/mem-data"'
assert_not_contains "${DEV_ROOT}/mem-env" 'E2E_VOLUME_NAME='
assert_contains "${DEV_ROOT}/mem-env" 'mem-control-plane-dev-e2e'
assert_contains "${DEV_ROOT}/mem-env" 'E2E local-development passed.'
assert_contains "${DEV_ROOT}/mem-env" 'E2E containerized-development passed.'
assert_contains "${REPO_ROOT}/installer/src/Web/package.json" '"test:e2e:all": "../../../dev/mem-env e2e all"'
assert_contains "${REPO_ROOT}/installer/src/Web/playwright.config.ts" 'name: "local-development"'
assert_contains "${REPO_ROOT}/installer/src/Web/playwright.config.ts" 'name: "containerized-development"'

launch_settings="${REPO_ROOT}/installer/src/Api/Properties/launchSettings.json"
assert_contains "${launch_settings}" '../../../dev/.state/interactive/data'
assert_contains "${launch_settings}" '../../../dev/.state/interactive/mem-data'
assert_contains "${launch_settings}" '../../../dev/.state/container/host-data/instances'
assert_contains "${launch_settings}" '../dev/.state/interactive/data/mem-control-plane.dev.db'
assert_contains "${launch_settings}" '"InstallerAuth__DevelopmentSetupToken": ""'

assert_contains "${DEV_ROOT}/mem-env" 'LOCAL_SEQ_CONTAINER_NAME="mem-seq-local"'
assert_contains "${DEV_ROOT}/mem-env" 'CONTAINER_SEQ_CONTAINER_NAME="mem-seq-dev"'
assert_contains "${DEV_ROOT}/mem-env" 'LOCAL_SEQ_PREFERRED_HOST_PORT="16341"'
assert_contains "${DEV_ROOT}/mem-env" 'CONTAINER_SEQ_PREFERRED_HOST_PORT="17341"'
assert_contains "${DEV_ROOT}/mem-env" 'seq_context_status local'
assert_contains "${DEV_ROOT}/mem-env" 'seq_context_status container'
assert_contains "${DEV_ROOT}/mem-env" 'remove_scoped_seq_for_reset local'
assert_contains "${DEV_ROOT}/mem-env" 'remove_scoped_seq_for_reset container'
assert_contains "${DEV_ROOT}/mem-env" 'other_managed_seq_container_names'
assert_not_contains "${DEV_ROOT}/mem-env" 'docker rm --force "mem-seq"'
assert_contains "${DEV_ROOT}/mem-env" 'reset host --dry-run'
assert_contains "${DEV_ROOT}/mem-env" 'DELETE_MEM_DEV_HOST_STATE'
assert_contains "${DEV_ROOT}/mem-env" '--include-canonical-control-plane'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_list_target_containers'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_list_target_volumes'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_is_legacy_mem_volume_name'
assert_contains "${DEV_ROOT}/mem-env" 'com.docker.compose.project'
assert_contains "${DEV_ROOT}/mem-env" 'com.docker.compose.volume'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_volume_has_mem_compose_provenance'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_list_target_container_volumes'
assert_contains "${DEV_ROOT}/mem-env" 'docker_container_named_volumes'
assert_contains "${DEV_ROOT}/mem-env" 'named volume mounted by reset-target container'
assert_contains "${DEV_ROOT}/mem-env" 'LC_ALL=C sort -u'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_list_target_networks'
assert_contains "${DEV_ROOT}/mem-env" 'Docker images and build cache were retained.'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_prepare_repository_cleanup'
assert_contains "${DEV_ROOT}/mem-env" 'Container-owned development-state files require privileged cleanup.'
assert_contains "${DEV_ROOT}/mem-env" 'sudo -v'
assert_contains "${DEV_ROOT}/mem-env" 'host_reset_remove_directory'
assert_contains "${DEV_ROOT}/mem-env" 'sudo rm -rf -- "${resolved_target}"'
assert_not_contains "${DEV_ROOT}/mem-env" 'docker system prune'
assert_not_contains "${DEV_ROOT}/mem-env" 'docker volume prune'
assert_not_contains "${DEV_ROOT}/mem-env" 'docker image prune'

seq_profile_source="${REPO_ROOT}/installer/src/Modules/Modules/Integrations/Seq/Services/SeqRuntimeContextProfile.cs"
assert_contains "${seq_profile_source}" '"mem-seq-local"'
assert_contains "${seq_profile_source}" '"mem-seq-dev"'
assert_contains "${seq_profile_source}" 'LocalDevelopmentPreferredHostPort = 16341'
assert_contains "${seq_profile_source}" 'ContainerizedDevelopmentPreferredHostPort = 17341'

bash -n "${DEV_ROOT}/mem-env"

echo "PASS: MEM developer environment command, safety and image contracts"
