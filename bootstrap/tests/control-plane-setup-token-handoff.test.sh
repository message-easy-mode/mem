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

log_info() { echo "[INFO] $*"; }
log_warn() { echo "[WARN] $*"; }
log_error() { :; }
fail() {
    echo "FAIL-CALLED: $*" >&2
    return 1
}

DRY_RUN=false
CONTROL_PLANE_PORT="8443"
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
CONTROL_PLANE_CERT_PATH="/data/certs/mem-control-plane.crt"
SETUP_TOKEN="mem_test_setup_authority_1234567890"
SETUP_TOKEN_WAS_CREATED=false

control_plane_certificate_sha256_fingerprint() {
    printf '%s\n' "AA:BB:CC:DD"
}

# 1. The anonymous server-owned auth/session state is the enrollment authority.
SESSION_RESPONSE='{"authenticated":false,"authenticationKind":null,"displayName":null,"roles":[],"requiresFirstOwnerBootstrap":true,"hasCompletedPlatformOwner":false}'
curl() { printf '%s\n' "${SESSION_RESPONSE}"; }
assert_eq "required" "$(control_plane_first_owner_bootstrap_state)" "fresh first-owner state"

SESSION_RESPONSE=' { "authenticated": false, "requiresFirstOwnerBootstrap": false, "hasCompletedPlatformOwner": true } '
assert_eq "completed" "$(control_plane_first_owner_bootstrap_state)" "completed owner state"

SESSION_RESPONSE='{"authenticated":false}'
assert_eq "unknown" "$(control_plane_first_owner_bootstrap_state)" "incomplete auth/session state"

curl() { return 22; }
assert_eq "unknown" "$(control_plane_first_owner_bootstrap_state)" "unreachable auth/session state"

# 2. Recreation still reuses persisted setup authority internally and does
# not generate a replacement token merely because the container is recreated.
REUSE_RESULT="$(
    (
        SETUP_TOKEN=""
        SETUP_TOKEN_WAS_CREATED=false
        PERSIST_CALLS=0
        load_existing_setup_token() {
            SETUP_TOKEN="mem_reused_setup_authority_1234567890"
        }
        generate_setup_token() {
            fail_test "persisted setup authority was unexpectedly replaced"
        }
        persist_setup_token() {
            PERSIST_CALLS=$((PERSIST_CALLS + 1))
        }
        prepare_setup_token
        printf '%s|%s|%s\n' "${SETUP_TOKEN}" "${SETUP_TOKEN_WAS_CREATED}" "${PERSIST_CALLS}"
    )
)"
assert_eq "mem_reused_setup_authority_1234567890|false|1" "${REUSE_RESULT}" "persisted setup authority reuse"

# 3. Fresh/un-enrolled bootstrap may present the token as part of the explicit
# first-owner handoff contract.
control_plane_first_owner_bootstrap_state() { printf '%s\n' "required"; }
FRESH_OUTPUT="$(print_control_plane_setup_token_handoff)"
assert_contains_text "${FRESH_OUTPUT}" "Setup token"
assert_contains_text "${FRESH_OUTPUT}" "${SETUP_TOKEN}"
assert_contains_text "${FRESH_OUTPUT}" "sudo ./install.sh --show-setup-token"

# 4. Once a Platform Owner exists, internal token reuse is preserved but the
# raw persisted authority is never re-surfaced by ordinary bootstrap handoff.
control_plane_first_owner_bootstrap_state() { printf '%s\n' "completed"; }
ENROLLED_OUTPUT="$(print_control_plane_setup_token_handoff)"
assert_contains_text "${ENROLLED_OUTPUT}" "Platform Owner enrollment is complete"
assert_not_contains_text "${ENROLLED_OUTPUT}" "${SETUP_TOKEN}"
assert_not_contains_text "${ENROLLED_OUTPUT}" "Setup token\n-----------"
assert_eq "mem_test_setup_authority_1234567890" "${SETUP_TOKEN}" "retained setup authority"
assert_eq "false" "${SETUP_TOKEN_WAS_CREATED}" "retained token was not replaced"

# 5. Unknown state fails closed: bootstrap does not guess that first-owner
# setup is pending and does not print the token. Explicit recovery remains.
control_plane_first_owner_bootstrap_state() { printf '%s\n' "unknown"; }
UNKNOWN_OUTPUT="$(print_control_plane_setup_token_handoff)"
assert_contains_text "${UNKNOWN_OUTPUT}" "will not be displayed automatically"
assert_contains_text "${UNKNOWN_OUTPUT}" "sudo ./install.sh --show-setup-token"
assert_not_contains_text "${UNKNOWN_OUTPUT}" "${SETUP_TOKEN}"

# 6. The normal access handoff uses the same gated token path rather than
# embedding the raw setup-token print block directly.
control_plane_first_owner_bootstrap_state() { printf '%s\n' "completed"; }
SUDO_USER="admin"
unset SSH_CONNECTION || true
HANDOFF_OUTPUT="$(print_control_plane_access_details)"
assert_contains_text "${HANDOFF_OUTPUT}" "MEM Control Plane is ready"
assert_contains_text "${HANDOFF_OUTPUT}" "Platform Owner enrollment is complete"
assert_not_contains_text "${HANDOFF_OUTPUT}" "${SETUP_TOKEN}"

# 7. The deliberate operator recovery command remains a separate explicit
# action; this correction only changes automatic post-bootstrap handoff.
grep -Fq -- '--show-setup-token' "${BOOTSTRAP_ROOT}/install.sh" || \
    fail_test "explicit --show-setup-token recovery option was removed"
grep -Fq -- 'print_existing_setup_token' "${BOOTSTRAP_ROOT}/lib/installer.sh" || \
    fail_test "explicit setup-token recovery function was removed"

bash -n "${BOOTSTRAP_ROOT}/lib/installer.sh"

echo "PASS: first-owner setup token is shown only for proven unenrolled state and suppressed after enrollment"
