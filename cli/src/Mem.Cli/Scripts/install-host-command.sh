#!/usr/bin/env bash
set -Eeuo pipefail

# Install the MEM CLI as the host-level `mem` command.
#
# Default contract:
#   /opt/mem/cli/<version>/mem   root:root, 0755
#   /usr/local/bin/mem           symlink to the selected version
#
# The script can either publish from this source checkout or install an already
# published single-file binary supplied by release/bootstrap packaging.

INSTALL_ROOT="${MEM_CLI_INSTALL_ROOT:-/opt/mem/cli}"
LINK_PATH="${MEM_CLI_LINK_PATH:-/usr/local/bin/mem}"
VERSION="dev"
RUNTIME="linux-x64"
CONFIGURATION="Release"
BINARY_PATH=""
PROJECT_PATH=""
DRY_RUN=false
SKIP_SECRET_TOOL_CHECK=false

print_help() {
    cat <<'EOF'
Install MEM CLI host command

Usage:
  sudo ./install-host-command.sh [options]

Options:
  --help, -h
      Show this help message.

  --dry-run
      Print the actions that would be taken without modifying the host.

  --binary <path>
      Install an already-published MEM CLI binary instead of publishing from source.

  --project <path>
      MEM CLI project to publish when --binary is not supplied.
      Defaults to ../Mem.Cli.csproj relative to this script.

  --version <version>
      Version directory under /opt/mem/cli. Use release versions such as 0.2.0
      or the development value "dev". Default: dev

  --runtime <rid>
      .NET runtime identifier used when publishing from source. Default: linux-x64

  --configuration <name>
      .NET publish configuration. Default: Release

  --install-root <path>
      Root directory for versioned CLI installs. Default: /opt/mem/cli

  --link-path <path>
      Stable command symlink. Default: /usr/local/bin/mem

  --skip-secret-tool-check
      Skip checking that secret-tool is installed. Intended only for tests or
      release packaging probes; normal Linux hosts should have libsecret-tools.

Examples:
  sudo ./install-host-command.sh --version dev
  sudo ./install-host-command.sh --binary ./publish/mem --version 0.2.0
EOF
}

log_info() {
    echo "[INFO] $*"
}

log_warn() {
    echo "[WARN] $*" >&2
}

fail() {
    echo "[ERROR] $*" >&2
    exit 1
}

script_dir() {
    cd "$(dirname "${BASH_SOURCE[0]}")" && pwd
}

validate_version() {
    if [[ ! "${VERSION}" =~ ^[A-Za-z0-9._-]{1,64}$ ]]; then
        fail "Invalid CLI version '${VERSION}'. Use 1-64 letters, numbers, dots, underscores, or hyphens."
    fi
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --help|-h)
                print_help
                exit 0
                ;;
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            --binary)
                [[ $# -ge 2 ]] || fail "--binary requires a path"
                BINARY_PATH="$2"
                shift 2
                ;;
            --project)
                [[ $# -ge 2 ]] || fail "--project requires a path"
                PROJECT_PATH="$2"
                shift 2
                ;;
            --version)
                [[ $# -ge 2 ]] || fail "--version requires a value"
                VERSION="$2"
                shift 2
                ;;
            --runtime)
                [[ $# -ge 2 ]] || fail "--runtime requires a value"
                RUNTIME="$2"
                shift 2
                ;;
            --configuration)
                [[ $# -ge 2 ]] || fail "--configuration requires a value"
                CONFIGURATION="$2"
                shift 2
                ;;
            --install-root)
                [[ $# -ge 2 ]] || fail "--install-root requires a path"
                INSTALL_ROOT="$2"
                shift 2
                ;;
            --link-path)
                [[ $# -ge 2 ]] || fail "--link-path requires a path"
                LINK_PATH="$2"
                shift 2
                ;;
            --skip-secret-tool-check)
                SKIP_SECRET_TOOL_CHECK=true
                shift
                ;;
            *)
                fail "Unknown option: $1"
                ;;
        esac
    done
}

require_secret_tool_binary() {
    if [[ "${SKIP_SECRET_TOOL_CHECK}" == true ]]; then
        log_warn "Skipping secret-tool check. Do not use this for normal host installs."
        return 0
    fi

    if ! command -v secret-tool >/dev/null 2>&1; then
        fail "secret-tool was not found. Install libsecret-tools before installing MEM CLI."
    fi
}

require_root_for_real_install() {
    if [[ "${DRY_RUN}" == true ]]; then
        return 0
    fi

    if [[ "${EUID}" -ne 0 ]]; then
        fail "Installing the host mem command requires root. Use sudo, or run --dry-run first."
    fi
}

resolve_project_path() {
    if [[ -n "${PROJECT_PATH}" ]]; then
        echo "${PROJECT_PATH}"
        return 0
    fi

    echo "$(script_dir)/../Mem.Cli.csproj"
}

publish_from_source() {
    local project="$1"
    local output_dir="$2"

    if [[ ! -f "${project}" ]]; then
        fail "MEM CLI project was not found: ${project}"
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        fail "dotnet SDK was not found. Supply --binary <path> or install the SDK before publishing from source."
    fi

    log_info "Publishing MEM CLI from source: ${project}"

    dotnet publish "${project}" \
        -c "${CONFIGURATION}" \
        -r "${RUNTIME}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "${output_dir}"

    BINARY_PATH="${output_dir}/mem"
}

install_binary() {
    local binary="$1"
    local target_dir="${INSTALL_ROOT}/${VERSION}"
    local target_path="${target_dir}/mem"
    local install_parent
    local link_parent

    install_parent="$(dirname "${INSTALL_ROOT}")"
    link_parent="$(dirname "${LINK_PATH}")"

    if [[ ! -f "${binary}" ]]; then
        fail "MEM CLI binary was not found: ${binary}"
    fi

    log_info "MEM CLI binary: ${binary}"
    log_info "Install target:  ${target_path}"
    log_info "Stable command:  ${LINK_PATH} -> ${target_path}"

    if [[ "${DRY_RUN}" == true ]]; then
        log_info "Dry-run mode: would create ${install_parent}, ${INSTALL_ROOT}, ${target_dir}, and ${link_parent}."
        log_info "Dry-run mode: would install ${binary} as ${target_path} with root:root 0755."
        log_info "Dry-run mode: would update ${LINK_PATH} to point at ${target_path}."
        return 0
    fi

    install -d -o root -g root -m 0755 "${install_parent}" "${INSTALL_ROOT}" "${target_dir}" "${link_parent}"
    install -o root -g root -m 0755 "${binary}" "${target_path}"
    ln -sfn "${target_path}" "${LINK_PATH}"
    chmod 0755 "${install_parent}" "${INSTALL_ROOT}" "${target_dir}" "${target_path}"

    log_info "Installed MEM CLI host command."
    log_info "Run: mem --help"
}

main() {
    parse_args "$@"
    validate_version
    require_secret_tool_binary
    require_root_for_real_install

    local temp_dir=""

    if [[ -z "${BINARY_PATH}" ]]; then
        if [[ "${DRY_RUN}" == true ]]; then
            local project
            project="$(resolve_project_path)"
            log_info "Dry-run mode: would publish MEM CLI project ${project} for ${RUNTIME}."
            BINARY_PATH="${project%/*}/bin/${CONFIGURATION}/net8.0/${RUNTIME}/publish/mem"
        else
            temp_dir="$(mktemp -d)"
            trap '[[ -n "${temp_dir}" ]] && rm -rf "${temp_dir}"' EXIT
            publish_from_source "$(resolve_project_path)" "${temp_dir}"
        fi
    fi

    install_binary "${BINARY_PATH}"
}

main "$@"
