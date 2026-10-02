#!/usr/bin/env bash

# Package prerequisite checks/install for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.

REQUIRED_PACKAGES=(
    ca-certificates
    curl
    gnupg
    lsb-release
    jq
    dnsutils
    iproute2
    net-tools
    libsecret-tools
    openssl
)

is_package_installed() {
    local package="$1"
    dpkg -s "${package}" >/dev/null 2>&1
}

check_required_packages() {
    local missing=()

    log_info "Checking required host packages..."

    for package in "${REQUIRED_PACKAGES[@]}"; do
        if is_package_installed "${package}"; then
            log_info "Package installed: ${package}"
        else
            log_warn "Package missing: ${package}"
            missing+=("${package}")
        fi
    done

    if (( ${#missing[@]} > 0 )); then
        MISSING_PACKAGES=("${missing[@]}")
        export MISSING_PACKAGES

        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Missing packages detected: ${missing[*]}"
            log_warn "Dry-run mode will continue. A real install would install these packages."
            return 0
        fi

        log_info "Missing packages will be installed: ${missing[*]}"
    else
        log_info "All required host packages are installed."
    fi
}

install_required_packages() {
    if (( ${#MISSING_PACKAGES[@]} == 0 )); then
        log_info "No missing packages to install."
        return 0
    fi

    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: skipping package installation."
        return 0
    fi

    confirm_or_exit "Message Easy Mode will modify this host by installing required packages: ${MISSING_PACKAGES[*]}"
    bootstrap_set_operation "install prerequisite packages"
    bootstrap_mark_changes_begun

    log_info "Updating apt package index..."
    apt-get update

    log_info "Installing required packages: ${MISSING_PACKAGES[*]}"
    DEBIAN_FRONTEND=noninteractive apt-get install -y "${MISSING_PACKAGES[@]}"
}
run_package_checks() {
    check_required_packages

    if [[ "${DRY_RUN:-false}" != true ]]; then
        install_required_packages
    fi
}
