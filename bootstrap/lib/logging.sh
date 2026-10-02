#!/usr/bin/env bash

# Logging and bounded failure-evidence helpers for Message Easy Mode bootstrap.
# This file is sourced by bootstrap/install.sh.

BOOTSTRAP_WARNINGS=0
BOOTSTRAP_PHASE="preflight"
BOOTSTRAP_SAFE_OPERATION="bootstrap"
BOOTSTRAP_CHANGES_BEGUN=false
BOOTSTRAP_REPORTING_INITIALIZED=false
BOOTSTRAP_REPORTING_PERSISTENT=false
BOOTSTRAP_FAILURE_RECORDED=false
BOOTSTRAP_FAILURE_STATUS=""
BOOTSTRAP_FAILURE_LINE=""
BOOTSTRAP_FAILURE_FUNCTION=""
BOOTSTRAP_RUN_ID=""
BOOTSTRAP_REPORT_DIR=""
BOOTSTRAP_TRANSCRIPT_PATH=""
BOOTSTRAP_FAILURE_REPORT_PATH=""
BOOTSTRAP_EVIDENCE_PATH=""
BOOTSTRAP_LATEST_PATH=""
BOOTSTRAP_REPORT_KEEP_COUNT="${MEM_BOOTSTRAP_REPORT_KEEP_COUNT:-20}"

bootstrap_utc_now() {
    date -u +%Y-%m-%dT%H:%M:%SZ
}

bootstrap_redact_stream() {
    local line
    while IFS= read -r line || [[ -n "${line}" ]]; do
        if [[ -n "${SETUP_TOKEN:-}" ]]; then
            line="${line//${SETUP_TOKEN}/[REDACTED]}"
        fi
        printf '%s\n' "${line}"
    done | sed -E \
        -e 's/([Aa]uthorization:[[:space:]]*[Bb]earer[[:space:]]+)[^[:space:]]+/\1[REDACTED]/g' \
        -e 's/((password|token|secret|cookie)["'"'"']?[[:space:]]*[:=][[:space:]]*["'"'"']?)[^"'"'"'[:space:],;]+/\1[REDACTED]/Ig' \
        -e 's/(MEM_[A-Z0-9_]*(PASSWORD|TOKEN|SECRET)[A-Z0-9_]*=)[^[:space:]]+/\1[REDACTED]/g' \
        -e 's/(mem_)[A-Za-z0-9_-]{20,}/\1[REDACTED]/g'
}

bootstrap_redact_text() {
    printf '%s\n' "$*" | bootstrap_redact_stream
}

bootstrap_transcript_write() {
    local level="$1"
    shift
    local message
    message="$(bootstrap_redact_text "$*")"

    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true || -z "${BOOTSTRAP_TRANSCRIPT_PATH}" ]]; then
        return 0
    fi

    printf '%s [%s] [%s] %s\n' \
        "$(bootstrap_utc_now)" \
        "${level}" \
        "${BOOTSTRAP_PHASE}" \
        "${message}" >> "${BOOTSTRAP_TRANSCRIPT_PATH}"
}

bootstrap_set_phase() {
    local phase="$1"
    [[ -n "${phase}" ]] || return 0
    BOOTSTRAP_PHASE="${phase}"
    bootstrap_transcript_write "PHASE" "Entered bootstrap phase: ${phase}"
}

bootstrap_set_operation() {
    local operation="$1"
    BOOTSTRAP_SAFE_OPERATION="${operation:-bootstrap}"
}

bootstrap_mark_changes_begun() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        return 0
    fi

    if [[ "${BOOTSTRAP_CHANGES_BEGUN}" != true ]]; then
        BOOTSTRAP_CHANGES_BEGUN=true
        bootstrap_transcript_write "INFO" "Host or runtime mutation has begun."
    fi
}

bootstrap_reporting_init() {
    BOOTSTRAP_REPORTING_INITIALIZED=true

    # Dry-run must remain genuinely non-mutating. It therefore does not create
    # /var/log/mem/bootstrap or any other durable transcript/report artefact.
    if [[ "${DRY_RUN:-false}" == true ]]; then
        BOOTSTRAP_REPORTING_PERSISTENT=false
        return 0
    fi

    BOOTSTRAP_REPORTING_PERSISTENT=true
    BOOTSTRAP_RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
    BOOTSTRAP_REPORT_DIR="${MEM_BOOTSTRAP_LOG_DIR:-/var/log/mem/bootstrap}"
    BOOTSTRAP_TRANSCRIPT_PATH="${BOOTSTRAP_REPORT_DIR}/mem-bootstrap-${BOOTSTRAP_RUN_ID}.log"
    BOOTSTRAP_FAILURE_REPORT_PATH="${BOOTSTRAP_REPORT_DIR}/mem-bootstrap-${BOOTSTRAP_RUN_ID}-failure.txt"
    BOOTSTRAP_EVIDENCE_PATH="${BOOTSTRAP_REPORT_DIR}/.mem-bootstrap-${BOOTSTRAP_RUN_ID}-evidence.tmp"
    BOOTSTRAP_LATEST_PATH="${BOOTSTRAP_REPORT_DIR}/latest.log"

    umask 077
    install -d -m 0700 "${BOOTSTRAP_REPORT_DIR}"
    : > "${BOOTSTRAP_TRANSCRIPT_PATH}"
    : > "${BOOTSTRAP_EVIDENCE_PATH}"
    chmod 0600 "${BOOTSTRAP_TRANSCRIPT_PATH}" "${BOOTSTRAP_EVIDENCE_PATH}"

    bootstrap_transcript_write "INFO" "Persistent bootstrap transcript started."
}

bootstrap_append_evidence() {
    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true || -z "${BOOTSTRAP_EVIDENCE_PATH}" ]]; then
        return 0
    fi

    bootstrap_redact_stream >> "${BOOTSTRAP_EVIDENCE_PATH}"
}

bootstrap_record_error_context() {
    local status="$1"
    local line="${2:-unknown}"
    local function_name="${3:-unknown}"

    if [[ "${BOOTSTRAP_FAILURE_RECORDED}" == true ]]; then
        return 0
    fi

    BOOTSTRAP_FAILURE_RECORDED=true
    BOOTSTRAP_FAILURE_STATUS="${status}"
    BOOTSTRAP_FAILURE_LINE="${line}"
    BOOTSTRAP_FAILURE_FUNCTION="${function_name}"
}

bootstrap_error_trap() {
    local status="$1"
    local line="${2:-unknown}"
    local function_name="${3:-unknown}"
    bootstrap_record_error_context "${status}" "${line}" "${function_name}"
    return 0
}

bootstrap_failure_recovery_container() {
    if [[ -n "${CONTROL_PLANE_CONTAINER_NAME:-}" ]]; then
        printf '%s\n' "${CONTROL_PLANE_CONTAINER_NAME}"
    else
        printf '%s\n' "mem-control-plane"
    fi
}

bootstrap_failure_recovery_container_available() {
    local recovery_container
    recovery_container="$(bootstrap_failure_recovery_container)"

    command -v docker >/dev/null 2>&1 || return 1
    docker inspect "${recovery_container}" >/dev/null 2>&1
}

bootstrap_print_failure_recovery_actions() {
    local recovery_container
    recovery_container="$(bootstrap_failure_recovery_container)"

    if bootstrap_failure_recovery_container_available; then
        echo "  sudo docker logs --timestamps --tail 500 ${recovery_container}"
        echo "  Resolve the reported failure, then rerun the same MEM bootstrap command."
        return 0
    fi

    echo "  Control Plane logs are not available because container '${recovery_container}' is not available for inspection."
    echo "  Failure occurred during bootstrap phase '${BOOTSTRAP_PHASE}'."

    case "${BOOTSTRAP_PHASE}" in
        start|host-checks|system-checks|package-checks|mem-cli|docker-checks)
            echo "  Resolve or wait for the reported host/package prerequisite, then rerun the same MEM bootstrap command."
            ;;
        *)
            echo "  Resolve the reported failure, then rerun the same MEM bootstrap command."
            ;;
    esac
}

bootstrap_write_failure_report() {
    local status="$1"

    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true || -z "${BOOTSTRAP_FAILURE_REPORT_PATH}" ]]; then
        return 0
    fi

    local failure_line="${BOOTSTRAP_FAILURE_LINE:-unknown}"
    local failure_function="${BOOTSTRAP_FAILURE_FUNCTION:-unknown}"

    {
        echo "MEM bootstrap failed"
        echo
        echo "Phase: ${BOOTSTRAP_PHASE}"
        echo "Time: $(bootstrap_utc_now)"
        echo "Exit code: ${status}"
        echo "Safe operation: ${BOOTSTRAP_SAFE_OPERATION}"
        echo "Failure location: ${failure_function}:${failure_line}"
        echo "Changes begun: ${BOOTSTRAP_CHANGES_BEGUN}"
        echo "Control Plane container: ${CONTROL_PLANE_CONTAINER_NAME:-unknown}"
        echo "Selected image: ${CONTROL_PLANE_IMAGE:-${CONTROL_PLANE_IMAGE_OVERRIDE:-unknown}}"
        echo "Data volume retained: ${CONTROL_PLANE_VOLUME_NAME:-unknown}"
        echo
        echo "Detailed transcript:"
        echo "${BOOTSTRAP_TRANSCRIPT_PATH}"
        echo
        echo "Captured safe runtime evidence:"
        if [[ -s "${BOOTSTRAP_EVIDENCE_PATH}" ]]; then
            cat "${BOOTSTRAP_EVIDENCE_PATH}"
        else
            echo "No additional container evidence was available before failure."
        fi
        echo
        echo "Suggested next actions:"
        bootstrap_print_failure_recovery_actions
        echo "Review the generated report before sharing it externally."
    } | bootstrap_redact_stream > "${BOOTSTRAP_FAILURE_REPORT_PATH}"

    chmod 0600 "${BOOTSTRAP_FAILURE_REPORT_PATH}"
}

bootstrap_rotate_reports() {
    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true || ! -d "${BOOTSTRAP_REPORT_DIR}" ]]; then
        return 0
    fi

    local keep_count="${BOOTSTRAP_REPORT_KEEP_COUNT}"
    if ! [[ "${keep_count}" =~ ^[0-9]+$ ]] || (( keep_count < 1 )); then
        keep_count=20
    fi

    local pattern
    for pattern in 'mem-bootstrap-*.log' 'mem-bootstrap-*-failure.txt'; do
        local -a files=()
        local file
        while IFS= read -r file; do
            [[ -n "${file}" ]] && files+=("${file}")
        done < <(find "${BOOTSTRAP_REPORT_DIR}" -maxdepth 1 -type f -name "${pattern}" \
            -printf '%T@\t%p\n' | sort -nr | cut -f2-)

        local index
        for ((index = keep_count; index < ${#files[@]}; index++)); do
            rm -f -- "${files[$index]}"
        done
    done
}

bootstrap_finalize_success() {
    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true ]]; then
        return 0
    fi

    bootstrap_transcript_write "INFO" "Bootstrap completed successfully."
    cp -- "${BOOTSTRAP_TRANSCRIPT_PATH}" "${BOOTSTRAP_LATEST_PATH}"
    chmod 0600 "${BOOTSTRAP_LATEST_PATH}"
    rm -f -- "${BOOTSTRAP_EVIDENCE_PATH}"
    bootstrap_rotate_reports
}

bootstrap_finalize_failure() {
    local status="$1"

    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" != true ]]; then
        return 0
    fi

    bootstrap_write_failure_report "${status}"
    bootstrap_transcript_write "ERROR" "Bootstrap failed with exit code ${status}."
    cp -- "${BOOTSTRAP_TRANSCRIPT_PATH}" "${BOOTSTRAP_LATEST_PATH}"
    chmod 0600 "${BOOTSTRAP_LATEST_PATH}"
    rm -f -- "${BOOTSTRAP_EVIDENCE_PATH}"
    bootstrap_rotate_reports

    echo >&2
    echo "MEM bootstrap evidence" >&2
    echo "----------------------" >&2
    echo "Transcript: ${BOOTSTRAP_TRANSCRIPT_PATH}" >&2
    echo "Failure report: ${BOOTSTRAP_FAILURE_REPORT_PATH}" >&2
    echo "Suggested recovery:" >&2
    bootstrap_print_failure_recovery_actions >&2
}

bootstrap_exit_trap() {
    local status="$1"

    trap - ERR EXIT

    if [[ "${BOOTSTRAP_REPORTING_INITIALIZED}" != true ]]; then
        return 0
    fi

    if (( status == 0 )); then
        bootstrap_finalize_success || true
    else
        bootstrap_record_error_context "${status}" "${BOOTSTRAP_FAILURE_LINE:-unknown}" "${BOOTSTRAP_FAILURE_FUNCTION:-unknown}"
        bootstrap_finalize_failure "${status}" || true
    fi
}

bootstrap_install_failure_traps() {
    trap 'bootstrap_error_trap "$?" "${BASH_LINENO[0]:-unknown}" "${FUNCNAME[1]:-main}"' ERR
    trap 'bootstrap_exit_trap "$?"' EXIT
}

log_info() {
    echo "[INFO] $*"
    bootstrap_transcript_write "INFO" "$*"
}

log_warn() {
    BOOTSTRAP_WARNINGS=$((BOOTSTRAP_WARNINGS + 1))
    echo "[WARN] $*" >&2
    bootstrap_transcript_write "WARN" "$*"
}

log_error() {
    echo "[ERROR] $*" >&2
    bootstrap_transcript_write "ERROR" "$*"
}

fail() {
    bootstrap_record_error_context 1 "${BASH_LINENO[0]:-unknown}" "${FUNCNAME[1]:-unknown}"
    log_error "$*"
    exit 1
}

print_summary() {
    echo
    echo "Message Easy Mode bootstrap summary"
    echo "----------------------------------"

    if (( BOOTSTRAP_WARNINGS == 0 )); then
        echo "Status: OK"
        echo "Warnings: 0"
    else
        echo "Status: Completed with warnings"
        echo "Warnings: ${BOOTSTRAP_WARNINGS}"
    fi

    if [[ "${DRY_RUN:-false}" == true ]]; then
        echo "Mode: dry-run"
        echo "No system changes were made."
    fi

    if [[ "${BOOTSTRAP_REPORTING_PERSISTENT}" == true && -n "${BOOTSTRAP_TRANSCRIPT_PATH}" ]]; then
        echo "Transcript: ${BOOTSTRAP_TRANSCRIPT_PATH}"
    fi

    echo
}
