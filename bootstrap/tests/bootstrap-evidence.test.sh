#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

fail_test() {
    echo "FAIL: $*" >&2
    exit 1
}

assert_contains() {
    local file="$1"
    local expected="$2"
    grep -Fq -- "${expected}" "${file}" || fail_test "${file} did not contain: ${expected}"
}

assert_not_contains() {
    local file="$1"
    local unexpected="$2"
    if grep -Fq -- "${unexpected}" "${file}"; then
        fail_test "${file} leaked forbidden text: ${unexpected}"
    fi
}

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-bootstrap-evidence-test.XXXXXX")"
trap 'rm -rf -- "${TMP_ROOT}"' EXIT

# 1. A real-run failure must leave a root-style bounded transcript and failure report,
# with phase/operation metadata and secret-like material redacted.
FAILURE_DIR="${TMP_ROOT}/failure"
FIXTURE="${TMP_ROOT}/failure-fixture.sh"
cat > "${FIXTURE}" <<EOF_FIXTURE
#!/usr/bin/env bash
set -Eeuo pipefail
source "${BOOTSTRAP_ROOT}/lib/logging.sh"
DRY_RUN=false
MEM_BOOTSTRAP_LOG_DIR="${FAILURE_DIR}"
CONTROL_PLANE_CONTAINER_NAME="mem-control-plane"
CONTROL_PLANE_IMAGE="mem-control-plane:test"
CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
SETUP_TOKEN="unkeyed-bootstrap-authority-value"
docker() {
    if [[ "\${1:-}" == "inspect" && "\${2:-}" == "mem-control-plane" ]]; then
        return 0
    fi
    return 1
}
bootstrap_reporting_init
bootstrap_install_failure_traps
bootstrap_set_phase "control-plane-ready-health"
bootstrap_set_operation "verify Control Plane readiness"
bootstrap_mark_changes_begun
log_info "safe fixture token=supersecret-token-value"
printf '%s\n' 'container password=hunter2' 'Authorization: Bearer abc.def.ghi' 'raw authority unkeyed-bootstrap-authority-value' | bootstrap_append_evidence
false
EOF_FIXTURE
chmod +x "${FIXTURE}"

set +e
bash "${FIXTURE}" >"${TMP_ROOT}/failure.stdout" 2>"${TMP_ROOT}/failure.stderr"
fixture_status=$?
set -e
[[ ${fixture_status} -ne 0 ]] || fail_test "failure fixture unexpectedly succeeded"

TRANSCRIPT="$(find "${FAILURE_DIR}" -maxdepth 1 -type f -name 'mem-bootstrap-*.log' ! -name 'latest.log' | head -n 1)"
FAILURE_REPORT="$(find "${FAILURE_DIR}" -maxdepth 1 -type f -name 'mem-bootstrap-*-failure.txt' | head -n 1)"
[[ -n "${TRANSCRIPT}" && -f "${TRANSCRIPT}" ]] || fail_test "transcript was not created"
[[ -n "${FAILURE_REPORT}" && -f "${FAILURE_REPORT}" ]] || fail_test "failure report was not created"
[[ -f "${FAILURE_DIR}/latest.log" ]] || fail_test "latest.log was not created"

[[ "$(stat -c '%a' "${FAILURE_DIR}")" == "700" ]] || fail_test "report directory mode was not 0700"
[[ "$(stat -c '%a' "${TRANSCRIPT}")" == "600" ]] || fail_test "transcript mode was not 0600"
[[ "$(stat -c '%a' "${FAILURE_REPORT}")" == "600" ]] || fail_test "failure report mode was not 0600"

assert_contains "${TRANSCRIPT}" "[INFO] [control-plane-ready-health]"
assert_contains "${FAILURE_REPORT}" "Phase: control-plane-ready-health"
assert_contains "${FAILURE_REPORT}" "Safe operation: verify Control Plane readiness"
assert_contains "${FAILURE_REPORT}" "Changes begun: true"
assert_contains "${FAILURE_REPORT}" "Suggested next actions:"
assert_contains "${FAILURE_REPORT}" "docker logs --timestamps --tail 500 mem-control-plane"
assert_not_contains "${TRANSCRIPT}" "supersecret-token-value"
assert_not_contains "${FAILURE_REPORT}" "supersecret-token-value"
assert_not_contains "${FAILURE_REPORT}" "hunter2"
assert_not_contains "${FAILURE_REPORT}" "abc.def.ghi"
assert_not_contains "${FAILURE_REPORT}" "unkeyed-bootstrap-authority-value"
assert_contains "${TRANSCRIPT}" "token=[REDACTED]"
assert_contains "${FAILURE_REPORT}" "password=[REDACTED]"

# 2. An early bootstrap failure must not suggest Control Plane Docker logs when
# the container does not exist yet.
EARLY_FAILURE_DIR="${TMP_ROOT}/early-failure"
EARLY_FIXTURE="${TMP_ROOT}/early-failure-fixture.sh"
cat > "${EARLY_FIXTURE}" <<EOF_EARLY_FIXTURE
#!/usr/bin/env bash
set -Eeuo pipefail
source "${BOOTSTRAP_ROOT}/lib/logging.sh"
DRY_RUN=false
MEM_BOOTSTRAP_LOG_DIR="${EARLY_FAILURE_DIR}"
CONTROL_PLANE_CONTAINER_NAME="mem-control-plane"
docker() { return 1; }
bootstrap_reporting_init
bootstrap_install_failure_traps
bootstrap_set_phase "docker-checks"
bootstrap_set_operation "install Docker from official apt repository"
bootstrap_mark_changes_begun
false
EOF_EARLY_FIXTURE
chmod +x "${EARLY_FIXTURE}"

set +e
bash "${EARLY_FIXTURE}" >"${TMP_ROOT}/early-failure.stdout" 2>"${TMP_ROOT}/early-failure.stderr"
early_fixture_status=$?
set -e
[[ ${early_fixture_status} -ne 0 ]] || fail_test "early failure fixture unexpectedly succeeded"

EARLY_FAILURE_REPORT="$(find "${EARLY_FAILURE_DIR}" -maxdepth 1 -type f -name 'mem-bootstrap-*-failure.txt' | head -n 1)"
[[ -n "${EARLY_FAILURE_REPORT}" && -f "${EARLY_FAILURE_REPORT}" ]] || fail_test "early failure report was not created"
assert_not_contains "${EARLY_FAILURE_REPORT}" "docker logs --timestamps --tail 500 mem-control-plane"
assert_not_contains "${TMP_ROOT}/early-failure.stderr" "docker logs --timestamps --tail 500 mem-control-plane"
assert_contains "${EARLY_FAILURE_REPORT}" "Control Plane logs are not available because container 'mem-control-plane' is not available for inspection."
assert_contains "${EARLY_FAILURE_REPORT}" "Failure occurred during bootstrap phase 'docker-checks'."
assert_contains "${EARLY_FAILURE_REPORT}" "Resolve or wait for the reported host/package prerequisite, then rerun the same MEM bootstrap command."
assert_contains "${TMP_ROOT}/early-failure.stderr" "Control Plane logs are not available because container 'mem-control-plane' is not available for inspection."

# 3. Dry-run reporting must stay non-mutating: no persistent report directory is created.
DRY_DIR="${TMP_ROOT}/dry-run"
(
    source "${BOOTSTRAP_ROOT}/lib/logging.sh"
    DRY_RUN=true
    MEM_BOOTSTRAP_LOG_DIR="${DRY_DIR}"
    bootstrap_reporting_init
    bootstrap_install_failure_traps
    bootstrap_set_phase "host-checks"
    log_info "dry-run fixture"
)
[[ ! -e "${DRY_DIR}" ]] || fail_test "dry-run created persistent bootstrap evidence"

# 4. Rotation keeps the configured number of transcript runs and failure reports.
ROTATE_DIR="${TMP_ROOT}/rotate"
mkdir -p "${ROTATE_DIR}"
for i in 1 2 3 4 5; do
    printf 'log %s\n' "${i}" > "${ROTATE_DIR}/mem-bootstrap-20260813T00000${i}Z-${i}.log"
    printf 'failure %s\n' "${i}" > "${ROTATE_DIR}/mem-bootstrap-20260813T00000${i}Z-${i}-failure.txt"
    touch -d "2026-08-13 00:00:0${i} UTC" \
        "${ROTATE_DIR}/mem-bootstrap-20260813T00000${i}Z-${i}.log" \
        "${ROTATE_DIR}/mem-bootstrap-20260813T00000${i}Z-${i}-failure.txt"
done
(
    source "${BOOTSTRAP_ROOT}/lib/logging.sh"
    BOOTSTRAP_REPORTING_PERSISTENT=true
    BOOTSTRAP_REPORT_DIR="${ROTATE_DIR}"
    BOOTSTRAP_REPORT_KEEP_COUNT=3
    bootstrap_rotate_reports
)
log_count="$(find "${ROTATE_DIR}" -maxdepth 1 -type f -name 'mem-bootstrap-*.log' | wc -l)"
failure_count="$(find "${ROTATE_DIR}" -maxdepth 1 -type f -name 'mem-bootstrap-*-failure.txt' | wc -l)"
[[ "${log_count}" -eq 3 ]] || fail_test "rotation retained ${log_count} transcripts instead of 3"
[[ "${failure_count}" -eq 3 ]] || fail_test "rotation retained ${failure_count} failure reports instead of 3"


# 5. Container failure capture keeps only bounded safe inspect/log evidence and redacts
# the persisted setup authority if it ever appears in container output.
CAPTURE_DIR="${TMP_ROOT}/capture"
CAPTURE_OUT="${TMP_ROOT}/capture.out"
(
    source "${BOOTSTRAP_ROOT}/lib/logging.sh"
    source "${BOOTSTRAP_ROOT}/lib/installer.sh"
    DRY_RUN=false
    MEM_BOOTSTRAP_LOG_DIR="${CAPTURE_DIR}"
    CONTROL_PLANE_PORT=8443
    CONTROL_PLANE_IMAGE="mem-control-plane:test"
    CONTROL_PLANE_VOLUME_NAME="mem-control-plane-data"
    SETUP_TOKEN="mem_capture_secret_12345678901234567890"
    bootstrap_reporting_init

    docker() {
        if [[ "$1" == "version" ]]; then
            echo "29.4.2"
            return 0
        fi
        if [[ "$1" == "inspect" && "${2:-}" == "mem-control-plane" ]]; then
            return 0
        fi
        if [[ "$1" == "inspect" && "${2:-}" == "--format" ]]; then
            case "$3" in
                *RestartCount*) echo "image=mem-control-plane:test state=running health=unhealthy restartCount=2" ;;
                *Mounts*) echo "volume:mem-control-plane-data:/data;bind::/var/run/docker.sock;" ;;
                *PortBindings*) echo '{"8443/tcp":[{"HostPort":"8443"}]}' ;;
                *NetworkSettings.Networks*) echo "mem-runtime;" ;;
                *) echo "unknown" ;;
            esac
            return 0
        fi
        if [[ "$1" == "logs" ]]; then
            echo "ordinary startup line"
            echo "accidental authority ${SETUP_TOKEN}"
            return 0
        fi
        return 1
    }
    curl() { echo "503"; }

    capture_control_plane_failure_evidence "mem-control-plane"
    cat "${BOOTSTRAP_EVIDENCE_PATH}" > "${CAPTURE_OUT}"
)
assert_contains "${CAPTURE_OUT}" "Docker server version: 29.4.2"
assert_contains "${CAPTURE_OUT}" "image=mem-control-plane:test state=running health=unhealthy restartCount=2"
assert_contains "${CAPTURE_OUT}" "volume:mem-control-plane-data:/data"
assert_contains "${CAPTURE_OUT}" "Container log tail (bounded to 32768 characters):"
assert_not_contains "${CAPTURE_OUT}" "mem_capture_secret_12345678901234567890"
assert_contains "${CAPTURE_OUT}" "accidental authority [REDACTED]"

echo "PASS: bootstrap transcript, failure evidence, redaction, dry-run, and rotation contract"
