#!/usr/bin/env bash

# Package prerequisite checks/install for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.

APT_LOCK_RETRY_ATTEMPTS="${APT_LOCK_RETRY_ATTEMPTS:-30}"
APT_LOCK_RETRY_DELAY_SECONDS="${APT_LOCK_RETRY_DELAY_SECONDS:-10}"

apt_output_indicates_lock_contention() {
    local output_file="$1"

    grep -Eq \
        'Could not get lock|Unable to acquire the .* lock|Unable to lock directory' \
        "${output_file}"
}

run_apt_get_with_lock_retry() {
    local attempt=1
    local max_attempts="${APT_LOCK_RETRY_ATTEMPTS}"
    local retry_delay="${APT_LOCK_RETRY_DELAY_SECONDS}"

    if ! [[ "${max_attempts}" =~ ^[0-9]+$ ]] || (( max_attempts < 1 )); then
        max_attempts=30
    fi
    if ! [[ "${retry_delay}" =~ ^[0-9]+$ ]]; then
        retry_delay=10
    fi

    while (( attempt <= max_attempts )); do
        local output_file
        local apt_status
        output_file="$(mktemp "${TMPDIR:-/tmp}/mem-apt-lock.XXXXXX")"

        # Keep normal apt output visible while retaining enough text to distinguish
        # transient package-manager contention from a real apt failure.
        if DEBIAN_FRONTEND=noninteractive apt-get "$@" 2>&1 | tee "${output_file}"; then
            apt_status="${PIPESTATUS[0]}"
        else
            apt_status="${PIPESTATUS[0]}"
        fi

        if (( apt_status == 0 )); then
            rm -f -- "${output_file}"
            return 0
        fi

        if ! apt_output_indicates_lock_contention "${output_file}"; then
            rm -f -- "${output_file}"
            return "${apt_status}"
        fi

        rm -f -- "${output_file}"

        if (( attempt >= max_attempts )); then
            log_error "Ubuntu package manager remained busy after ${max_attempts} attempts. Wait for the active apt/dpkg operation to finish, then rerun MEM."
            return "${apt_status}"
        fi

        if (( attempt == 1 )); then
            log_info "Ubuntu package manager is busy (for example, unattended upgrades may still be running). MEM will wait and retry automatically."
        else
            log_info "Ubuntu package manager is still busy; retrying (${attempt}/${max_attempts})."
        fi

        sleep "${retry_delay}"
        attempt=$((attempt + 1))
    done
}

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
    run_apt_get_with_lock_retry update

    log_info "Installing required packages: ${MISSING_PACKAGES[*]}"
    run_apt_get_with_lock_retry install -y "${MISSING_PACKAGES[@]}"
}
run_package_checks() {
    check_required_packages

    if [[ "${DRY_RUN:-false}" != true ]]; then
        install_required_packages
    fi
}
