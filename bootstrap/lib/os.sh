#!/usr/bin/env bash

# OS and privilege checks for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.
#
# Important:
#   This file must not execute install.sh.
#   This file must not call main.
#   This file must only define functions.

require_root() {
    if [[ "${EUID}" -ne 0 ]]; then
        fail "This installer must be run as root. Use: sudo ./install.sh"
    fi
}

detect_os() {
    if [[ ! -f /etc/os-release ]]; then
        fail "Cannot detect operating system. /etc/os-release not found."
    fi

    # shellcheck disable=SC1091
    source /etc/os-release

    OS_ID="${ID:-unknown}"
    OS_NAME="${PRETTY_NAME:-unknown}"
    OS_VERSION_ID="${VERSION_ID:-unknown}"
    OS_CODENAME="${VERSION_CODENAME:-unknown}"

    export OS_ID
    export OS_NAME
    export OS_VERSION_ID
    export OS_CODENAME
}

validate_ubuntu() {
    detect_os

    log_info "Detected OS: ${OS_NAME}"

    if [[ "${OS_ID}" != "ubuntu" ]]; then
        fail "Unsupported operating system '${OS_ID}'. Message Easy Mode bootstrap currently supports Ubuntu only."
    fi

    if [[ "${MEM_RELEASE_MODE:-development}" == "release" ]]; then
        if [[ "${OS_VERSION_ID}" != "24.04" ]]; then
            fail "Unsupported Ubuntu version '${OS_VERSION_ID}' for this MEM release. MEM 0.2.0 is certified for Ubuntu 24.04 LTS."
        fi
        log_info "Ubuntu release target supported: ${OS_VERSION_ID}"
        return 0
    fi

    case "${OS_VERSION_ID}" in
        22.04|24.04|26.04)
            log_info "Ubuntu version supported for development/bootstrap compatibility: ${OS_VERSION_ID}"
            ;;
        *)
            fail "Unsupported Ubuntu version '${OS_VERSION_ID}'. Development bootstrap compatibility: 22.04, 24.04, 26.04."
            ;;
    esac
}

validate_architecture() {
    local arch
    arch="$(uname -m)"

    log_info "Detected architecture: ${arch}"

    if [[ "${MEM_RELEASE_MODE:-development}" == "release" ]]; then
        case "${arch}" in
            x86_64|amd64)
                ARCH="amd64"
                ;;
            *)
                fail "Unsupported CPU architecture '${arch}' for this MEM release. MEM 0.2.0 is certified for amd64."
                ;;
        esac
        export ARCH
        return 0
    fi

    case "${arch}" in
        x86_64|amd64)
            ARCH="amd64"
            ;;
        aarch64|arm64)
            ARCH="arm64"
            log_warn "arm64 detected. This is a development/bootstrap compatibility path until MEM release images are certified for arm64."
            ;;
        *)
            fail "Unsupported CPU architecture '${arch}'. Development bootstrap compatibility: amd64, arm64 (image availability required)."
            ;;
    esac

    export ARCH
}

run_os_checks() {
    require_root
    validate_ubuntu
    validate_architecture
}
