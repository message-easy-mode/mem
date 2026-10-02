#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# shellcheck source=bootstrap/lib/installer.sh
source "${BOOTSTRAP_ROOT}/lib/installer.sh"
# shellcheck source=bootstrap/lib/packages.sh
source "${BOOTSTRAP_ROOT}/lib/packages.sh"

fail_test() {
    echo "FAIL: $*" >&2
    exit 1
}

assert_contains_text() {
    local value="$1"
    local expected="$2"
    [[ "${value}" == *"${expected}"* ]] || \
        fail_test "expected '${expected}' in output"
}

assert_not_contains_text() {
    local value="$1"
    local unexpected="$2"
    [[ "${value}" != *"${unexpected}"* ]] || \
        fail_test "did not expect '${unexpected}' in output"
}

assert_eq() {
    local expected="$1"
    local actual="$2"
    local label="$3"
    [[ "${actual}" == "${expected}" ]] || \
        fail_test "${label}: expected '${expected}', got '${actual}'"
}

log_info() { :; }
log_warn() { :; }
log_error() { :; }
fail() {
    echo "FAIL-CALLED: $*" >&2
    return 1
}

CONTROL_PLANE_PORT="8443"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
CONTROL_PLANE_CERT_PATH="/data/certs/mem-control-plane.crt"
DRY_RUN=false
SETUP_TOKEN=""

# Keep the handoff output deterministic and verify that it surfaces the
# browser-verifiable certificate identity without exposing private key data.
control_plane_certificate_sha256_fingerprint() {
    printf '%s\n' "AA:BB:CC:DD"
}

# 1. Cloud/remote handoff uses the exact already-proven SSH server endpoint,
# keeps both sides of the forwarding private, and never invents a public MEM
# browser URL.
SUDO_USER="alice"
SSH_CONNECTION="198.51.100.77 52000 203.0.113.20 22"
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"

SSH_OUTPUT="$(print_control_plane_access_details)"
assert_contains_text "${SSH_OUTPUT}" "Administration: SSH tunnel / local-only"
assert_contains_text "${SSH_OUTPUT}" "Exposure:       loopback only"
assert_contains_text "${SSH_OUTPUT}" "ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:8443:127.0.0.1:8443 alice@203.0.113.20"
assert_contains_text "${SSH_OUTPUT}" "https://127.0.0.1:8443"
assert_contains_text "${SSH_OUTPUT}" "There is no supported direct public Control Plane URL."
assert_contains_text "${SSH_OUTPUT}" "SHA-256 fingerprint:"
assert_contains_text "${SSH_OUTPUT}" "AA:BB:CC:DD"
assert_not_contains_text "${SSH_OUTPUT}" "https://203.0.113.20:8443"

# 2. Trusted LAN handoff makes the direct private URL primary while retaining
# an SSH break-glass path. With no current SSH session, the deliberately
# selected LAN address is the safe target to print.
unset SSH_CONNECTION
SUDO_USER="master"
CONTROL_PLANE_ACCESS_MODE="trusted-lan"
CONTROL_PLANE_BIND_ADDRESS="192.168.10.20"

LAN_OUTPUT="$(print_control_plane_access_details)"
assert_contains_text "${LAN_OUTPUT}" "Administration: Trusted LAN"
assert_contains_text "${LAN_OUTPUT}" "Host binding:   192.168.10.20:8443"
assert_contains_text "${LAN_OUTPUT}" "https://192.168.10.20:8443"
assert_contains_text "${LAN_OUTPUT}" "Do not publish this Control Plane port through Internet-facing NAT"
assert_contains_text "${LAN_OUTPUT}" "SSH remains available as a break-glass/private tunnel path:"
assert_contains_text "${LAN_OUTPUT}" "ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:8443:192.168.10.20:8443 master@192.168.10.20"
assert_contains_text "${LAN_OUTPUT}" "https://127.0.0.1:8443"

# 3. If bootstrap cannot prove a reachable SSH endpoint, it prints a clear
# placeholder instead of guessing a cloud/VPC/LAN address.
unset SSH_CONNECTION
SUDO_USER="admin"
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
assert_eq "admin@<server-address>" "$(printf '%s@%s' "$(control_plane_handoff_ssh_user)" "$(control_plane_handoff_ssh_host)")" "safe unknown SSH target"
assert_contains_text "$(control_plane_ssh_tunnel_command)" "admin@<server-address>"

# 4. Shell-controlled/unexpected user strings are never reproduced as a
# copy/pasteable command fragment.
SUDO_USER=$'bad user\nextra'
assert_eq "<ssh-user>" "$(control_plane_handoff_ssh_user)" "unsafe SSH username fallback"

# 5. Fingerprint support is a normal bootstrap prerequisite, not an optional
# best-effort instruction that works only on some supported Ubuntu hosts.
OPENSSL_FOUND=false
for package in "${REQUIRED_PACKAGES[@]}"; do
    [[ "${package}" == "openssl" ]] && OPENSSL_FOUND=true
 done
[[ "${OPENSSL_FOUND}" == true ]] || fail_test "openssl is not part of the supported bootstrap package set"

echo "PASS: SSH and Trusted-LAN Control Plane handoff, exact tunnel guidance, browser URL safety, and certificate fingerprint UX"
