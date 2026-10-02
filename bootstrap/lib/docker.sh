#!/usr/bin/env bash

# Docker detection/checks/install for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.

DOCKER_AVAILABLE=false
DOCKER_DAEMON_AVAILABLE=false
DOCKER_COMPOSE_AVAILABLE=false
DOCKER_INSTALL_ATTEMPTED=false
DOCKER_VERSION="unknown"
DOCKER_COMPOSE_VERSION="unknown"

reset_docker_detection_state() {
    DOCKER_AVAILABLE=false
    DOCKER_DAEMON_AVAILABLE=false
    DOCKER_COMPOSE_AVAILABLE=false
    DOCKER_VERSION="unknown"
    DOCKER_COMPOSE_VERSION="unknown"
}

docker_command_exists() {
    command -v docker >/dev/null 2>&1
}

detect_docker_command() {
    log_info "Checking Docker installation..."

    if docker_command_exists; then
        local docker_path
        docker_path="$(command -v docker)"

        DOCKER_AVAILABLE=true
        log_info "Docker command found: ${docker_path}"
    else
        DOCKER_AVAILABLE=false
        log_warn "Docker command not found."

        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Dry-run mode will continue. A real install would install Docker Engine unless --skip-docker-install is used."
            return 0
        fi

        if [[ "${SKIP_DOCKER_INSTALL:-false}" == true ]]; then
            fail "Docker is not installed and --skip-docker-install was provided."
        fi

        log_info "Docker is missing and will need to be installed."
    fi
}

detect_docker_daemon() {
    if [[ "${DOCKER_AVAILABLE}" != true ]]; then
        log_warn "Skipping Docker daemon check because Docker command is not available."
        return 0
    fi

    log_info "Checking Docker daemon..."

    if docker info >/dev/null 2>&1; then
        DOCKER_DAEMON_AVAILABLE=true
        log_info "Docker daemon is reachable."
    else
        DOCKER_DAEMON_AVAILABLE=false

        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Docker command exists, but the Docker daemon is not reachable."
            log_warn "Dry-run mode will continue. A real install would attempt to start/enable Docker if installed by this script."
            return 0
        fi

        fail "Docker command exists, but the Docker daemon is not reachable. Check: sudo systemctl status docker"
    fi
}

detect_docker_version() {
    if [[ "${DOCKER_AVAILABLE}" != true ]]; then
        return 0
    fi

    if docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then
        DOCKER_VERSION="$(docker version --format '{{.Server.Version}}' 2>/dev/null || true)"
    else
        DOCKER_VERSION="$(docker --version 2>/dev/null || echo "unknown")"
    fi

    log_info "Docker version: ${DOCKER_VERSION}"
}

detect_docker_compose() {
    if [[ "${DOCKER_AVAILABLE}" != true ]]; then
        log_warn "Skipping Docker Compose plugin check because Docker command is not available."
        return 0
    fi

    log_info "Checking Docker Compose plugin..."

    if docker compose version >/dev/null 2>&1; then
        DOCKER_COMPOSE_AVAILABLE=true
        DOCKER_COMPOSE_VERSION="$(docker compose version 2>/dev/null || echo "unknown")"
        log_info "Docker Compose plugin found: ${DOCKER_COMPOSE_VERSION}"
    else
        DOCKER_COMPOSE_AVAILABLE=false

        if [[ "${DRY_RUN:-false}" == true ]]; then
            log_warn "Docker Compose plugin not found."
            log_warn "Dry-run mode will continue. A real install would install docker-compose-plugin."
            return 0
        fi

        if [[ "${SKIP_DOCKER_INSTALL:-false}" == true ]]; then
            fail "Docker Compose plugin is missing and --skip-docker-install was provided."
        fi

        log_info "Docker Compose plugin is missing and will need to be installed."
    fi
}

install_docker_from_official_apt_repo() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would install Docker using the official Docker apt repository."
        return 0
    fi

    DOCKER_INSTALL_ATTEMPTED=true

    confirm_or_exit "Message Easy Mode will modify this host by installing Docker Engine, containerd, Buildx, and the Docker Compose plugin."
    bootstrap_set_operation "install Docker from official apt repository"
    bootstrap_mark_changes_begun

    log_info "Installing Docker using the official Docker apt repository..."

    run_apt_get_with_lock_retry update

    run_apt_get_with_lock_retry install -y \
        ca-certificates \
        curl \
        gnupg

    install -m 0755 -d /etc/apt/keyrings

    curl -fsSL "https://download.docker.com/linux/ubuntu/gpg" \
        -o /etc/apt/keyrings/docker.asc

    chmod a+r /etc/apt/keyrings/docker.asc

    echo \
        "deb [arch=${ARCH} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${OS_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list

    run_apt_get_with_lock_retry update

    run_apt_get_with_lock_retry install -y \
        docker-ce \
        docker-ce-cli \
        containerd.io \
        docker-buildx-plugin \
        docker-compose-plugin

    log_info "Enabling and starting Docker service..."

    systemctl enable docker
    systemctl start docker

    log_info "Docker installation completed."
}

install_docker_from_convenience_script() {
    if [[ "${DRY_RUN:-false}" == true ]]; then
        log_info "Dry-run mode: would install Docker using Docker's convenience script."
        return 0
    fi

    DOCKER_INSTALL_ATTEMPTED=true

    confirm_or_exit "Message Easy Mode will install Docker using Docker's convenience script. This is intended for development/testing."
    bootstrap_set_operation "install Docker from convenience script"
    bootstrap_mark_changes_begun

    log_warn "Using Docker convenience script. The official apt repository method is preferred for normal installs."

    curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
    sh /tmp/get-docker.sh

    systemctl enable docker
    systemctl start docker

    log_info "Docker installation completed using convenience script."
}

install_docker_if_required() {
    if [[ "${DOCKER_AVAILABLE}" == true && "${DOCKER_COMPOSE_AVAILABLE}" == true ]]; then
        log_info "Docker and Docker Compose plugin are already available."
        return 0
    fi

    if [[ "${SKIP_DOCKER_INSTALL:-false}" == true ]]; then
        fail "Docker installation is required, but --skip-docker-install was provided."
    fi

    if [[ "${USE_DOCKER_CONVENIENCE_SCRIPT:-false}" == true ]]; then
        install_docker_from_convenience_script
    else
        install_docker_from_official_apt_repo
    fi
}

run_docker_checks() {
    reset_docker_detection_state

    detect_docker_command
    detect_docker_daemon
    detect_docker_version
    detect_docker_compose

    if [[ "${DRY_RUN:-false}" == true ]]; then
        return 0
    fi

    install_docker_if_required

    if [[ "${DOCKER_INSTALL_ATTEMPTED}" == true ]]; then
        log_info "Re-checking Docker after installation..."

        reset_docker_detection_state

        detect_docker_command
        detect_docker_daemon
        detect_docker_version
        detect_docker_compose
    fi
}