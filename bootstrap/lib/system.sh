#!/usr/bin/env bash

# Host system checks for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.

MIN_MEMORY_MB=3500
RECOMMENDED_MEMORY_MB=7800
MIN_DISK_FREE_MB=20000
RECOMMENDED_DISK_FREE_MB=50000
MIN_CPU_CORES=2

get_memory_mb() {
    awk '/MemTotal/ { printf "%d", $2 / 1024 }' /proc/meminfo
}

get_cpu_cores() {
    nproc
}

get_root_disk_free_mb() {
    df -Pm / | awk 'NR==2 { print $4 }'
}

check_cpu() {
    local cores
    cores="$(get_cpu_cores)"

    log_info "Detected CPU cores: ${cores}"

    if (( cores < MIN_CPU_CORES )); then
        log_warn "CPU core count is below recommended minimum (${MIN_CPU_CORES}). MEM may run poorly."
    fi
}

check_memory() {
    local memory_mb
    memory_mb="$(get_memory_mb)"

    log_info "Detected memory: ${memory_mb} MB"

    if (( memory_mb < MIN_MEMORY_MB )); then
        fail "Insufficient memory. Minimum required: ${MIN_MEMORY_MB} MB. Detected: ${memory_mb} MB."
    fi

    if (( memory_mb < RECOMMENDED_MEMORY_MB )); then
        log_warn "Memory is below recommended level (${RECOMMENDED_MEMORY_MB} MB). Matrix/Synapse may be constrained."
    fi
}

check_disk() {
    local free_mb
    free_mb="$(get_root_disk_free_mb)"

    log_info "Detected free disk on /: ${free_mb} MB"

    if (( free_mb < MIN_DISK_FREE_MB )); then
        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Free disk space is below the production minimum (${MIN_DISK_FREE_MB} MB). Detected: ${free_mb} MB."
            log_warn "Dry-run mode will continue, but a real install would fail unless more disk space is available."
            return 0
        fi

        if [[ "${ALLOW_LOW_DISK:-false}" == true ]]; then
            log_warn "Free disk space is below the production minimum (${MIN_DISK_FREE_MB} MB). Detected: ${free_mb} MB."
            log_warn "--allow-low-disk was provided. Continuing for development/testing only."
            return 0
        fi

        fail "Insufficient free disk space on /. Minimum required: ${MIN_DISK_FREE_MB} MB. Detected: ${free_mb} MB."
    fi

    if (( free_mb < RECOMMENDED_DISK_FREE_MB )); then
        log_warn "Free disk space is below recommended level (${RECOMMENDED_DISK_FREE_MB} MB)."
    fi
}


check_basic_network() {
    log_info "Checking basic network connectivity..."

    if command -v getent >/dev/null 2>&1; then
        if getent hosts github.com >/dev/null 2>&1; then
            log_info "DNS resolution check passed: github.com"
        else
            log_warn "DNS resolution check failed for github.com."
        fi
    else
        log_warn "getent not available; skipping DNS resolution check."
    fi

    if command -v curl >/dev/null 2>&1; then
        if curl -fsSL --connect-timeout 5 https://github.com >/dev/null 2>&1; then
            log_info "HTTPS connectivity check passed: github.com"
        else
            log_warn "HTTPS connectivity check failed for github.com."
        fi
    else
        log_warn "curl not available; HTTPS connectivity check will be available after prerequisites are installed."
    fi
}

run_system_checks() {
    check_cpu
    check_memory
    check_disk
    check_basic_network
}
