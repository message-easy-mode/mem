#!/usr/bin/env bash

# MEM CLI host command staging/install helpers.
# This file is sourced by bootstrap/install.sh.

MEM_CLI_BOOTSTRAP_STAGING_DIR="/opt/mem/bootstrap/cli"
MEM_CLI_INSTALL_ROOT="/opt/mem/cli"
MEM_CLI_LINK_PATH="/usr/local/bin/mem"
MEM_CLI_BINARY_OVERRIDE=""
MEM_CLI_VERSION="${MEM_RELEASE_VERSION:-dev}"
SKIP_MEM_CLI_HOST_COMMAND=false

resolve_mem_cli_install_script_source() {
    local packaged_script="${SCRIPT_DIR}/cli/install-host-command.sh"
    local source_tree_script="${REPO_ROOT}/cli/src/Mem.Cli/Scripts/install-host-command.sh"

    if [[ -f "${packaged_script}" ]]; then
        echo "${packaged_script}"
        return 0
    fi

    if [[ -f "${source_tree_script}" ]]; then
        echo "${source_tree_script}"
        return 0
    fi

    echo ""
}

resolve_mem_cli_binary_source() {
    local packaged_binary="${SCRIPT_DIR}/cli/mem"

    if [[ -n "${MEM_CLI_BINARY_OVERRIDE:-}" ]]; then
        echo "${MEM_CLI_BINARY_OVERRIDE}"
        return 0
    fi

    if [[ -f "${packaged_binary}" ]]; then
        echo "${packaged_binary}"
        return 0
    fi

    echo ""
}

validate_mem_cli_version() {
    if [[ ! "${MEM_CLI_VERSION}" =~ ^[A-Za-z0-9._-]{1,64}$ ]]; then
        fail "Invalid MEM CLI version '${MEM_CLI_VERSION}'. Use 1-64 letters, numbers, dots, underscores, or hyphens."
    fi
}

stage_mem_cli_payload() {
    local script_source="$1"
    local binary_source="$2"

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would stage MEM CLI install script from ${script_source} to ${MEM_CLI_BOOTSTRAP_STAGING_DIR}/install-host-command.sh."
        log_info "Dry-run mode: would stage MEM CLI binary from ${binary_source} to ${MEM_CLI_BOOTSTRAP_STAGING_DIR}/mem."
        return 0
    fi

    bootstrap_set_operation "stage MEM CLI host-command payload"
    bootstrap_mark_changes_begun
    log_info "Staging MEM CLI host-command payload under ${MEM_CLI_BOOTSTRAP_STAGING_DIR}."
    install -d -o root -g root -m 0755 "${MEM_CLI_BOOTSTRAP_STAGING_DIR}"
    install -o root -g root -m 0755 "${script_source}" "${MEM_CLI_BOOTSTRAP_STAGING_DIR}/install-host-command.sh"
    install -o root -g root -m 0755 "${binary_source}" "${MEM_CLI_BOOTSTRAP_STAGING_DIR}/mem"
}

install_mem_cli_host_command() {
    local staged_script="${MEM_CLI_BOOTSTRAP_STAGING_DIR}/install-host-command.sh"
    local staged_binary="${MEM_CLI_BOOTSTRAP_STAGING_DIR}/mem"

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would install MEM CLI host command using ${staged_script}."
        log_info "Dry-run mode: would link ${MEM_CLI_LINK_PATH} to ${MEM_CLI_INSTALL_ROOT}/${MEM_CLI_VERSION}/mem."
        return 0
    fi

    bootstrap_set_operation "install MEM CLI host command"
    bootstrap_mark_changes_begun
    log_info "Installing MEM CLI host command."
    "${staged_script}" \
        --binary "${staged_binary}" \
        --version "${MEM_CLI_VERSION}" \
        --install-root "${MEM_CLI_INSTALL_ROOT}" \
        --link-path "${MEM_CLI_LINK_PATH}"
}

run_mem_cli_host_command_stage() {
    if [[ "${SKIP_MEM_CLI_HOST_COMMAND:-false}" == true ]]; then
        log_info "Skipping MEM CLI host command installation by request."
        return 0
    fi

    validate_mem_cli_version

    local script_source
    local binary_source

    script_source="$(resolve_mem_cli_install_script_source)"
    binary_source="$(resolve_mem_cli_binary_source)"

    if [[ -z "${script_source}" || -z "${binary_source}" ]]; then
        log_warn "MEM CLI host-command payload is not available in this bootstrap package."
        log_warn "Skipping MEM CLI host command installation. Release packaging should stage bootstrap/cli/install-host-command.sh and bootstrap/cli/mem, or pass --mem-cli-binary for a development proof."
        return 0
    fi

    if [[ ! -f "${script_source}" ]]; then
        fail "MEM CLI install script was not found: ${script_source}"
    fi

    if [[ ! -f "${binary_source}" ]]; then
        fail "MEM CLI binary was not found: ${binary_source}"
    fi

    stage_mem_cli_payload "${script_source}" "${binary_source}"
    install_mem_cli_host_command
}
