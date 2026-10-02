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

assert_contains_line() {
    local file="$1"
    local expected="$2"
    grep -Fxq -- "${expected}" "${file}" || \
        fail_test "${file} did not contain exact line: ${expected}"
}

assert_not_contains_line() {
    local file="$1"
    local unexpected="$2"
    if grep -Fxq -- "${unexpected}" "${file}"; then
        fail_test "${file} contained forbidden exact line: ${unexpected}"
    fi
}

assert_contains_text() {
    local value="$1"
    local expected="$2"
    [[ "${value}" == *"${expected}"* ]] || \
        fail_test "expected '${expected}' in '${value}'"
}

assert_not_contains_text() {
    local value="$1"
    local unexpected="$2"
    [[ "${value}" != *"${unexpected}"* ]] || \
        fail_test "did not expect '${unexpected}' in '${value}'"
}

bootstrap_set_operation() { :; }
bootstrap_mark_changes_begun() { :; }
log_info() { :; }
log_warn() { :; }
log_error() { :; }
fail() {
    echo "FAIL-CALLED: $*" >&2
    return 1
}

# 1. RFC1918 classification is deliberately narrow.
for address in \
    10.0.0.1 \
    10.255.255.254 \
    172.16.0.1 \
    172.31.255.254 \
    192.168.1.20; do
    is_rfc1918_ipv4 "${address}" || fail_test "RFC1918 address was rejected: ${address}"
done

for address in \
    172.32.0.1 \
    192.0.2.20 \
    8.8.8.8 \
    169.254.1.1 \
    0.0.0.0 \
    127.0.0.1 \
    999.1.1.1 \
    not-an-ip; do
    if is_rfc1918_ipv4 "${address}"; then
        fail_test "non-RFC1918 address was accepted: ${address}"
    fi
done

# 2. Eligible LAN enumeration excludes obvious container/VPN interfaces while
# retaining ordinary physical and management-bridge interfaces.
ip() {
    cat <<'EOF_IP'
2: ens18    inet 192.168.10.20/24 brd 192.168.10.255 scope global ens18
3: docker0  inet 172.17.0.1/16 brd 172.17.255.255 scope global docker0
4: br-deadbeef inet 172.18.0.1/16 brd 172.18.255.255 scope global br-deadbeef
5: wg0      inet 10.90.0.1/24 brd 10.90.0.255 scope global wg0
6: vmbr0    inet 10.10.0.193/24 brd 10.10.0.255 scope global vmbr0
7: eth1     inet 203.0.113.20/24 brd 203.0.113.255 scope global eth1
EOF_IP
}

CANDIDATES="$(eligible_trusted_lan_ipv4s)"
assert_contains_text "${CANDIDATES}" "ens18|192.168.10.20"
assert_contains_text "${CANDIDATES}" "vmbr0|10.10.0.193"
assert_not_contains_text "${CANDIDATES}" "docker0|"
assert_not_contains_text "${CANDIDATES}" "br-deadbeef|"
assert_not_contains_text "${CANDIDATES}" "wg0|"
assert_not_contains_text "${CANDIDATES}" "203.0.113.20"

# 3. CLI validation rejects unsafe or ambiguous direct-binding requests before
# bootstrap can begin mutating the host.
CLI_OUT="${TMPDIR:-/tmp}/mem-control-plane-access-cli.$$.out"
if bash "${BOOTSTRAP_ROOT}/install.sh" \
    --control-plane-access trusted-lan \
    --control-plane-bind-address 8.8.8.8 >"${CLI_OUT}" 2>&1; then
    fail_test "public Trusted LAN bind unexpectedly passed bootstrap argument validation"
fi
assert_contains_text "$(cat "${CLI_OUT}")" "Trusted LAN Control Plane binding accepts RFC1918 IPv4 only"

if bash "${BOOTSTRAP_ROOT}/install.sh" \
    --yes \
    --control-plane-access trusted-lan >"${CLI_OUT}" 2>&1; then
    fail_test "--yes Trusted LAN without an explicit address unexpectedly passed validation"
fi
assert_contains_text "$(cat "${CLI_OUT}")" "requires --control-plane-bind-address"

if bash "${BOOTSTRAP_ROOT}/install.sh" \
    --control-plane-access ssh \
    --control-plane-bind-address 192.168.10.20 >"${CLI_OUT}" 2>&1; then
    fail_test "SSH mode unexpectedly accepted a direct bind address"
fi
assert_contains_text "$(cat "${CLI_OUT}")" "valid only with --control-plane-access trusted-lan"

# 4. Non-interactive/--yes operation defaults to the loopback SSH boundary.
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
CONTROL_PLANE_PORT="8443"
ASSUME_YES=true
DRY_RUN=false
resolve_control_plane_access_policy
assert_eq "ssh-tunnel" "${CONTROL_PLANE_ACCESS_MODE}" "non-interactive default access"
assert_eq "127.0.0.1" "${CONTROL_PLANE_BIND_ADDRESS}" "non-interactive default bind"

# 5. Interactive operation requires a deliberate Trusted LAN selection.
bootstrap_input_is_interactive() { return 0; }
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
ASSUME_YES=false
DRY_RUN=false
resolve_control_plane_access_policy <<< "2"
assert_eq "trusted-lan" "${CONTROL_PLANE_ACCESS_MODE}" "interactive Trusted LAN access"
assert_eq "192.168.10.20" "${CONTROL_PLANE_BIND_ADDRESS}" "interactive Trusted LAN bind"

# 6. New container launch publishes only the exact selected host address.
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-control-plane-access-test.XXXXXX")"
trap 'rm -rf -- "${TMP_ROOT}"; rm -f -- "${CLI_OUT}"' EXIT
CONTROL_PLANE_HOST_DATA_ROOT="${TMP_ROOT}/host-data"
validate_control_plane_host_data_root
mkdir -p \
    "${CONTROL_PLANE_MEM_DATA_ROOT}" \
    "${CONTROL_PLANE_INSTANCE_DATA_ROOT}" \
    "${CONTROL_PLANE_COTURN_STORAGE_ROOT}" \
    "${CONTROL_PLANE_SEQ_HOST_DATA_PATH}"
CONTROL_PLANE_IMAGE="mem-control-plane:test"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
CONTROL_PLANE_CERT_PATH="/data/certs/mem-control-plane.crt"
CONTROL_PLANE_KEY_PATH="/data/certs/mem-control-plane.key"
CHANNEL="dev"
DRY_RUN=false
DOCKER_ARGS_FILE="${TMP_ROOT}/docker-run-args"

docker_container_exists() { return 1; }
docker() {
    if [[ "$1" == "run" ]]; then
        printf '%s\n' "$@" > "${DOCKER_ARGS_FILE}"
        return 0
    fi
    return 1
}

CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
run_new_control_plane_container
assert_contains_line "${DOCKER_ARGS_FILE}" "127.0.0.1:8443:8443"
assert_not_contains_line "${DOCKER_ARGS_FILE}" "0.0.0.0:8443:8443"
assert_contains_line "${DOCKER_ARGS_FILE}" "io.message-easy-mode.control-plane-access=ssh-tunnel"
assert_contains_line "${DOCKER_ARGS_FILE}" "io.message-easy-mode.control-plane-bind-address=127.0.0.1"
assert_contains_line "${DOCKER_ARGS_FILE}" "MEM_CONTROL_PLANE_ACCESS_MODE=ssh-tunnel"
assert_contains_line "${DOCKER_ARGS_FILE}" "MEM_CONTROL_PLANE_BIND_ADDRESS=127.0.0.1"
assert_contains_line "${DOCKER_ARGS_FILE}" "App__PublicBaseUrl=https://127.0.0.1:8443"
if grep -Fq -- "MEM_CONTROL_PLANE_HOST_IPV4=" "${DOCKER_ARGS_FILE}"; then
    fail_test "SSH mode exposed a browser host IPv4 environment value"
fi

CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"

# Recreate real bootstrap logging. Publication is captured through command
# substitution by the installer and must remain a single clean Docker scalar.
log_info() { echo "[INFO] $*"; }
PUBLISH_SPEC="$(control_plane_publish_spec)"
assert_eq "192.168.10.20:8443:8443" "${PUBLISH_SPEC}" "Trusted LAN Docker publication scalar"
assert_not_contains_text "${PUBLISH_SPEC}" "[INFO]"
log_info() { :; }

run_new_control_plane_container
assert_contains_line "${DOCKER_ARGS_FILE}" "192.168.10.20:8443:8443"
assert_not_contains_line "${DOCKER_ARGS_FILE}" "0.0.0.0:8443:8443"
assert_contains_line "${DOCKER_ARGS_FILE}" "io.message-easy-mode.control-plane-access=trusted-lan"
assert_contains_line "${DOCKER_ARGS_FILE}" "io.message-easy-mode.control-plane-bind-address=192.168.10.20"
assert_contains_line "${DOCKER_ARGS_FILE}" "MEM_CONTROL_PLANE_HOST_IPV4=192.168.10.20"
assert_contains_line "${DOCKER_ARGS_FILE}" "App__PublicBaseUrl=https://192.168.10.20:8443"
assert_not_contains_line "${DOCKER_ARGS_FILE}" "App__PublicBaseUrl=https://127.0.0.1:8443"

# 7. New certificate SANs never inherit an arbitrary/public primary host IP.
volume_file_exists() { return 1; }
CERT_ARGS_FILE="${TMP_ROOT}/certificate-args"
docker() {
    if [[ "$1" == "run" ]]; then
        printf '%s\n' "$@" > "${CERT_ARGS_FILE}"
        return 0
    fi
    return 1
}

CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
ensure_control_plane_certificate
CERT_ARGS="$(cat "${CERT_ARGS_FILE}")"
assert_contains_text "${CERT_ARGS}" "SAN='DNS:localhost,IP:127.0.0.1'"
assert_not_contains_text "${CERT_ARGS}" "203.0.113.20"
assert_not_contains_text "${CERT_ARGS}" "192.168.10.20"

CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"

# Recreate real bootstrap logging here. The certificate SAN helper is consumed
# through command substitution and must return only the selected IP even when
# Trusted-LAN validation emits its normal informational message.
log_info() { echo "[INFO] $*"; }
EXTRA_SAN_IP="$(control_plane_certificate_extra_san_ip)"
assert_eq "192.168.10.20" "${EXTRA_SAN_IP}" "Trusted LAN certificate SAN scalar"
log_info() { :; }

ensure_control_plane_certificate
CERT_ARGS="$(cat "${CERT_ARGS_FILE}")"
assert_contains_text "${CERT_ARGS}" "SAN='DNS:localhost,IP:127.0.0.1'"
assert_contains_text "${CERT_ARGS}" "SAN=\"\${SAN},IP:192.168.10.20\""
assert_not_contains_text "${CERT_ARGS}" "[INFO]"

# 8. Health and browser authority resolve from the same validated private origin.
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
assert_eq "https://127.0.0.1:8443" "$(control_plane_canonical_private_origin)" "SSH canonical private origin"
assert_eq "https://127.0.0.1:8443" "$(control_plane_health_base_url)" "SSH health URL"
CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"
log_info() { echo "[INFO] $*"; }
CANONICAL_PRIVATE_ORIGIN="$(control_plane_canonical_private_origin)"
HEALTH_BASE_URL="$(control_plane_health_base_url)"
assert_eq "https://192.168.10.20:8443" "${CANONICAL_PRIVATE_ORIGIN}" "Trusted LAN canonical private origin"
assert_eq "https://192.168.10.20:8443" "${HEALTH_BASE_URL}" "Trusted LAN health URL"
assert_not_contains_text "${CANONICAL_PRIVATE_ORIGIN}" "[INFO]"
assert_not_contains_text "${HEALTH_BASE_URL}" "[INFO]"
log_info() { :; }

echo "PASS: private Control Plane access policy, canonical browser authority, exact Docker publication, and certificate SAN contract"
