#!/usr/bin/env bash
set -Eeuo pipefail

# Message Easy Mode bootstrap.
#
# Responsibilities:
#   - check host compatibility
#   - install required packages
#   - install Docker if required
#   - start or migrate the permanent MEM Control Plane runtime
#
# This script is intentionally the pre-Docker bootstrap layer.
# The browser-based setup workflow continues inside the Control Plane.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# A release bundle may provide non-secret immutable release metadata here.
# Source it before the helper libraries so release-aware defaults (for example
# the CLI version) can inherit the packaged product identity.
MEM_RELEASE_MODE="development"
MEM_RELEASE_VERSION=""
MEM_RELEASE_CHANNEL=""
MEM_RELEASE_CONTROL_PLANE_IMAGE=""
MEM_RELEASE_PLATFORM_OS=""
MEM_RELEASE_PLATFORM_VERSION=""
MEM_RELEASE_PLATFORM_ARCH=""
RELEASE_ENV_PATH="${SCRIPT_DIR}/release.env"
if [[ -f "${RELEASE_ENV_PATH}" ]]; then
    # shellcheck disable=SC1090
    source "${RELEASE_ENV_PATH}"
fi

# shellcheck source=bootstrap/lib/logging.sh
source "${SCRIPT_DIR}/lib/logging.sh"
# shellcheck source=bootstrap/lib/release.sh
source "${SCRIPT_DIR}/lib/release.sh"
# shellcheck source=bootstrap/lib/os.sh
source "${SCRIPT_DIR}/lib/os.sh"
# shellcheck source=bootstrap/lib/system.sh
source "${SCRIPT_DIR}/lib/system.sh"
# shellcheck source=bootstrap/lib/packages.sh
source "${SCRIPT_DIR}/lib/packages.sh"
# shellcheck source=bootstrap/lib/docker.sh
source "${SCRIPT_DIR}/lib/docker.sh"
# shellcheck source=bootstrap/lib/prompts.sh
source "${SCRIPT_DIR}/lib/prompts.sh"
# shellcheck source=bootstrap/lib/cli.sh
source "${SCRIPT_DIR}/lib/cli.sh"
# shellcheck source=bootstrap/lib/installer.sh
source "${SCRIPT_DIR}/lib/installer.sh"

DRY_RUN=false
ASSUME_YES=false
CHANNEL="stable"
CHANNEL_EXPLICIT=false
CONTROL_PLANE_PORT="8443"
CONTROL_PLANE_PORT_EXPLICIT=false
CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
CONTROL_PLANE_ACCESS_EXPLICIT=false
CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=false
CONTROL_PLANE_IMAGE_OVERRIDE=""
SKIP_DOCKER_INSTALL=false
USE_DOCKER_CONVENIENCE_SCRIPT=false
ALLOW_LOW_DISK=false
SHOW_SETUP_TOKEN=false
MISSING_PACKAGES=()

print_help() {
    cat <<EOF_HELP
Message Easy Mode Bootstrap

Usage:
  sudo ./install.sh [options]

Options:
  --help, -h
      Show this help message.

  --dry-run
      Run checks and print the Control Plane plan. Do not change the system.

  --yes, -y
      Assume yes for prompts.

  --allow-low-disk
      Development only. Continue even if disk space is below production minimum.

  --channel <stable|dev>
      MEM Control Plane image channel to use.
      Default: stable

  --control-plane-port <port>
      Host HTTPS port for the private MEM Control Plane.
      Default: 8443

  --control-plane-access <ssh|trusted-lan>
      Administration boundary for the MEM Control Plane.
      ssh: loopback-only; remote access uses an SSH tunnel.
      trusted-lan: bind only to an explicitly selected RFC1918 host address.
      Default: ssh unless an interactive operator deliberately selects Trusted LAN.

  --control-plane-bind-address <ipv4>
      Exact host IPv4 address for --control-plane-access trusted-lan.
      Must be RFC1918 and currently assigned to an eligible host interface.

  --control-plane-image <image>
      Override the MEM Control Plane image.
      Useful for local/release testing, e.g. mem-control-plane:local.

  --installer-port <port>
      Compatibility alias for --control-plane-port.

  --installer-image <image>
      Compatibility alias for --control-plane-image.

  --mem-cli-binary <path>
      Development/release-packaging override for a prebuilt MEM CLI binary.
      When supplied, bootstrap stages it and installs the host mem command.

  --mem-cli-version <version>
      Version label used under /opt/mem/cli/<version>/mem.
      Default: dev

  --skip-mem-cli-host-command
      Skip MEM CLI host command staging and installation.

  --show-setup-token
      Show the existing Control Plane setup token and exit.

  --skip-docker-install
      Do not install Docker if missing. Fail instead.

  --use-docker-convenience-script
      Use Docker's convenience script instead of the official apt repository method.
      Intended for development/testing only.

Examples:
  sudo ./install.sh --dry-run
  sudo ./install.sh --channel dev
  sudo ./install.sh --control-plane-port 9443
  sudo ./install.sh --control-plane-access ssh
  sudo ./install.sh --control-plane-access trusted-lan --control-plane-bind-address 192.168.10.20
  sudo ./install.sh --control-plane-image mem-control-plane:local
  sudo ./install.sh --show-setup-token
EOF_HELP
}

require_root() {
    if [[ "${EUID}" -ne 0 ]]; then
        fail "This bootstrap must be run as root. Use: sudo ./install.sh"
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
            --yes|-y)
                ASSUME_YES=true
                shift
                ;;
            --channel)
                [[ $# -ge 2 ]] || fail "--channel requires a value: stable or dev"
                CHANNEL="$2"
                CHANNEL_EXPLICIT=true
                shift 2
                ;;
            --control-plane-port|--installer-port)
                [[ $# -ge 2 ]] || fail "$1 requires a port number"
                CONTROL_PLANE_PORT="$2"
                CONTROL_PLANE_PORT_EXPLICIT=true
                shift 2
                ;;
            --control-plane-access)
                [[ $# -ge 2 ]] || fail "--control-plane-access requires ssh or trusted-lan"
                if ! CONTROL_PLANE_ACCESS_MODE="$(normalize_control_plane_access_mode "$2")"; then
                    fail "Invalid Control Plane access mode '$2'. Expected ssh or trusted-lan."
                fi
                CONTROL_PLANE_ACCESS_EXPLICIT=true
                shift 2
                ;;
            --control-plane-bind-address)
                [[ $# -ge 2 ]] || fail "--control-plane-bind-address requires an IPv4 address"
                CONTROL_PLANE_BIND_ADDRESS="$2"
                CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=true
                shift 2
                ;;
            --control-plane-image|--installer-image)
                [[ $# -ge 2 ]] || fail "$1 requires an image name"
                CONTROL_PLANE_IMAGE_OVERRIDE="$2"
                shift 2
                ;;
            --mem-cli-binary)
                [[ $# -ge 2 ]] || fail "--mem-cli-binary requires a path"
                MEM_CLI_BINARY_OVERRIDE="$2"
                shift 2
                ;;
            --mem-cli-version)
                [[ $# -ge 2 ]] || fail "--mem-cli-version requires a version value"
                MEM_CLI_VERSION="$2"
                shift 2
                ;;
            --skip-mem-cli-host-command)
                SKIP_MEM_CLI_HOST_COMMAND=true
                shift
                ;;
            --show-setup-token)
                SHOW_SETUP_TOKEN=true
                shift
                ;;
            --skip-docker-install)
                SKIP_DOCKER_INSTALL=true
                shift
                ;;
            --allow-low-disk)
                ALLOW_LOW_DISK=true
                shift
                ;;
            --use-docker-convenience-script)
                USE_DOCKER_CONVENIENCE_SCRIPT=true
                shift
                ;;
            *)
                fail "Unknown option: $1"
                ;;
        esac
    done

    case "${CHANNEL}" in
        stable|dev)
            ;;
        *)
            fail "Invalid channel '${CHANNEL}'. Expected: stable or dev"
            ;;
    esac

    if ! [[ "${CONTROL_PLANE_PORT}" =~ ^[0-9]+$ ]]; then
        fail "Invalid Control Plane port '${CONTROL_PLANE_PORT}'. Must be numeric."
    fi

    if (( CONTROL_PLANE_PORT < 1 || CONTROL_PLANE_PORT > 65535 )); then
        fail "Invalid Control Plane port '${CONTROL_PLANE_PORT}'. Must be between 1 and 65535."
    fi

    if [[ "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT}" == true && \
          "${CONTROL_PLANE_ACCESS_MODE}" != "trusted-lan" ]]; then
        fail "--control-plane-bind-address is valid only with --control-plane-access trusted-lan."
    fi

    if [[ "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT}" == true ]] && \
       ! is_valid_ipv4_address "${CONTROL_PLANE_BIND_ADDRESS}"; then
        fail "Invalid Control Plane bind address '${CONTROL_PLANE_BIND_ADDRESS}'. Expected a concrete IPv4 address."
    fi

    if [[ "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT}" == true ]] && \
       ! is_rfc1918_ipv4 "${CONTROL_PLANE_BIND_ADDRESS}"; then
        fail "Trusted LAN Control Plane binding accepts RFC1918 IPv4 only. Public, wildcard, loopback, and link-local addresses are not supported."
    fi

    if [[ "${CONTROL_PLANE_ACCESS_EXPLICIT}" == true && \
          "${CONTROL_PLANE_ACCESS_MODE}" == "trusted-lan" && \
          "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT}" != true && \
          ( "${ASSUME_YES}" == true || "${DRY_RUN}" == true ) ]]; then
        fail "--control-plane-access trusted-lan requires --control-plane-bind-address when --yes or --dry-run is used."
    fi
}

validate_packaged_release_metadata() {
    if [[ "${MEM_RELEASE_MODE:-development}" != "release" ]]; then
        return 0
    fi

    [[ "${MEM_RELEASE_VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]] || \
        fail "Packaged MEM release version is invalid: '${MEM_RELEASE_VERSION:-missing}'."

    if ! mem_release_channel_contract_is_valid "${MEM_RELEASE_VERSION}" "${MEM_RELEASE_CHANNEL}"; then
        expected_release_channel="$(mem_release_channel_for_version "${MEM_RELEASE_VERSION}")"
        fail "Packaged MEM release channel '${MEM_RELEASE_CHANNEL:-missing}' does not match release version '${MEM_RELEASE_VERSION}'. Expected: ${expected_release_channel}."
    fi

    if [[ "${MEM_RELEASE_PLATFORM_OS}" != "ubuntu" || \
          "${MEM_RELEASE_PLATFORM_VERSION}" != "24.04" || \
          "${MEM_RELEASE_PLATFORM_ARCH}" != "amd64" ]]; then
        fail "Packaged MEM 0.2.0 release host policy must be Ubuntu 24.04 amd64."
    fi

    if [[ ! "${MEM_RELEASE_CONTROL_PLANE_IMAGE}" =~ ^ghcr\.io/message-easy-mode/mem-control-plane:${MEM_RELEASE_VERSION}@sha256:[0-9a-f]{64}$ ]]; then
        fail "Packaged Control Plane image must use the exact MEM release version and immutable sha256 digest."
    fi

    if [[ "${CHANNEL_EXPLICIT}" == true && "${CHANNEL}" != "stable" && -z "${CONTROL_PLANE_IMAGE_OVERRIDE}" ]]; then
        fail "--channel ${CHANNEL} is not valid for a packaged MEM release. Use the packaged image identity or an explicit --control-plane-image development override."
    fi

    if [[ "${MEM_CLI_VERSION}" != "${MEM_RELEASE_VERSION}" ]]; then
        fail "MEM CLI version '${MEM_CLI_VERSION}' does not match packaged MEM release '${MEM_RELEASE_VERSION}'."
    fi
}

main() {
    parse_args "$@"
    validate_packaged_release_metadata
    require_root

    bootstrap_reporting_init
    bootstrap_install_failure_traps
    bootstrap_set_phase "start"
    bootstrap_set_operation "initialize bootstrap"

    if [[ "${SHOW_SETUP_TOKEN}" == true ]]; then
        bootstrap_set_operation "show persisted setup token"
        print_existing_setup_token
        exit 0
    fi

    log_info "Message Easy Mode bootstrap"
    log_info "Repo root: ${REPO_ROOT}"
    if [[ "${MEM_RELEASE_MODE:-development}" == "release" ]]; then
        log_info "MEM release: ${MEM_RELEASE_VERSION}"
        log_info "Packaged Control Plane image: ${MEM_RELEASE_CONTROL_PLANE_IMAGE}"
        log_info "Certified host: ${MEM_RELEASE_PLATFORM_OS} ${MEM_RELEASE_PLATFORM_VERSION} ${MEM_RELEASE_PLATFORM_ARCH}"
    fi
    log_info "Channel: $(bootstrap_operator_channel)"
    log_info "Control Plane HTTPS port: ${CONTROL_PLANE_PORT}"
    log_info "Requested Control Plane access: ${CONTROL_PLANE_ACCESS_MODE}"
    log_info "Requested Control Plane bind address: ${CONTROL_PLANE_BIND_ADDRESS:-none}"
    log_info "Control Plane image override: ${CONTROL_PLANE_IMAGE_OVERRIDE:-none}"
    log_info "Dry run: ${DRY_RUN}"
    log_info "Skip Docker install: ${SKIP_DOCKER_INSTALL}"
    log_info "Use Docker convenience script: ${USE_DOCKER_CONVENIENCE_SCRIPT}"
    log_info "MEM CLI host command skip: ${SKIP_MEM_CLI_HOST_COMMAND}"
    log_info "MEM CLI binary override: ${MEM_CLI_BINARY_OVERRIDE:-none}"
    log_info "MEM CLI version: ${MEM_CLI_VERSION}"

    bootstrap_set_phase "host-checks"
    bootstrap_set_operation "validate host operating system"
    run_os_checks

    bootstrap_set_phase "system-checks"
    bootstrap_set_operation "validate host capacity and connectivity"
    run_system_checks

    bootstrap_set_phase "package-checks"
    bootstrap_set_operation "validate and install prerequisite packages"
    run_package_checks

    bootstrap_set_phase "mem-cli"
    bootstrap_set_operation "stage and install MEM CLI host command"
    run_mem_cli_host_command_stage

    bootstrap_set_phase "docker-checks"
    bootstrap_set_operation "validate and install Docker runtime"
    run_docker_checks

    # The private-administration boundary is resolved only after Docker runtime
    # classification so a safe existing canonical binding can be preserved and
    # an unsafe legacy/wildcard binding can be migrated deliberately.
    bootstrap_set_operation "prepare and start MEM Control Plane"
    run_control_plane_stage

    bootstrap_set_phase "complete"
    bootstrap_set_operation "complete bootstrap"

    if [[ "${DRY_RUN}" == true ]]; then
        log_info "Dry-run mode enabled. No system changes were made."
    fi

    print_summary
    log_info "Message Easy Mode bootstrap complete."
}

main "$@"
