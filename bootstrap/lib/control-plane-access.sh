#!/usr/bin/env bash

# Private administration binding policy for the MEM Control Plane.
# This file is sourced by bootstrap/lib/installer.sh.

# Private Control Plane administration boundary. 0.2.0 supports a loopback-only
# SSH-tunnel baseline and an explicitly selected RFC1918 Trusted LAN address.
# These values describe the desired host binding for any new canonical
# Control Plane container created by the current bootstrap operation. Existing
# runtime exposure is inspected separately before it is ever treated as safe.
CONTROL_PLANE_ACCESS_MODE="${CONTROL_PLANE_ACCESS_MODE:-ssh-tunnel}"
CONTROL_PLANE_ACCESS_EXPLICIT="${CONTROL_PLANE_ACCESS_EXPLICIT:-false}"
CONTROL_PLANE_BIND_ADDRESS="${CONTROL_PLANE_BIND_ADDRESS:-127.0.0.1}"
CONTROL_PLANE_BIND_ADDRESS_EXPLICIT="${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT:-false}"

normalize_control_plane_access_mode() {
    local requested_mode="${1:-}"

    case "${requested_mode}" in
        ssh|ssh-tunnel|local|loopback)
            printf '%s\n' "ssh-tunnel"
            ;;
        trusted-lan)
            printf '%s\n' "trusted-lan"
            ;;
        *)
            return 1
            ;;
    esac
}

is_valid_ipv4_address() {
    local address="${1:-}"
    local first second third fourth extra

    IFS='.' read -r first second third fourth extra <<< "${address}"
    [[ -z "${extra:-}" && -n "${first:-}" && -n "${second:-}" && \
       -n "${third:-}" && -n "${fourth:-}" ]] || return 1

    local octet
    for octet in "${first}" "${second}" "${third}" "${fourth}"; do
        [[ "${octet}" =~ ^[0-9]{1,3}$ ]] || return 1
        (( 10#${octet} <= 255 )) || return 1
    done
}

is_rfc1918_ipv4() {
    local address="${1:-}"
    is_valid_ipv4_address "${address}" || return 1

    local first second third fourth
    IFS='.' read -r first second third fourth <<< "${address}"
    first=$((10#${first}))
    second=$((10#${second}))

    (( first == 10 )) && return 0
    (( first == 172 && second >= 16 && second <= 31 )) && return 0
    (( first == 192 && second == 168 )) && return 0
    return 1
}

classify_control_plane_host_bind_address() {
    local address="${1:-}"

    case "${address}" in
        ""|0.0.0.0|::)
            printf '%s\n' "wildcard"
            return 0
            ;;
        127.0.0.1)
            printf '%s\n' "loopback"
            return 0
            ;;
        ::1)
            # MEM 0.2.0 canonicalizes SSH/local-only administration on IPv4
            # loopback so an inherited IPv6-only binding is private but still
            # requires reviewed recreation.
            printf '%s\n' "loopback-noncanonical"
            return 0
            ;;
    esac

    if is_rfc1918_ipv4 "${address}"; then
        if host_ipv4_interface_for_address "${address}" >/dev/null 2>&1; then
            printf '%s\n' "trusted-lan"
        else
            printf '%s\n' "trusted-lan-unassigned"
        fi
        return 0
    fi

    if is_valid_ipv4_address "${address}"; then
        printf '%s\n' "nonprivate-ipv4"
        return 0
    fi

    printf '%s\n' "unsupported"
}

is_obvious_non_lan_interface() {
    local interface_name="${1%%@*}"

    case "${interface_name}" in
        lo|docker*|br-*|veth*|virbr*|lxcbr*|podman*|cni*|flannel*|wg*|tun*|tap*|tailscale*|zt*|nebula*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

host_ipv4_assignments() {
    command -v ip >/dev/null 2>&1 || return 0

    local interface_name cidr address
    while read -r interface_name cidr; do
        [[ -n "${interface_name:-}" && -n "${cidr:-}" ]] || continue
        interface_name="${interface_name%%@*}"
        address="${cidr%%/*}"
        is_valid_ipv4_address "${address}" || continue
        printf '%s|%s\n' "${interface_name}" "${address}"
    done < <(ip -o -4 addr show scope global 2>/dev/null | awk '{print $2, $4}')
}

eligible_trusted_lan_ipv4s() {
    local interface_name address
    while IFS='|' read -r interface_name address; do
        [[ -n "${interface_name:-}" && -n "${address:-}" ]] || continue
        is_obvious_non_lan_interface "${interface_name}" && continue
        is_rfc1918_ipv4 "${address}" || continue
        printf '%s|%s\n' "${interface_name}" "${address}"
    done < <(host_ipv4_assignments) | awk '!seen[$0]++'
}

host_ipv4_interface_for_address() {
    local requested_address="${1:-}"
    local interface_name address

    while IFS='|' read -r interface_name address; do
        [[ "${address}" == "${requested_address}" ]] || continue
        is_obvious_non_lan_interface "${interface_name}" && continue
        printf '%s\n' "${interface_name}"
        return 0
    done < <(host_ipv4_assignments)

    return 1
}

validate_trusted_lan_bind_address() {
    local address="${1:-}"

    if ! is_rfc1918_ipv4 "${address}"; then
        fail "Trusted LAN Control Plane binding requires a concrete RFC1918 IPv4 address assigned to this host. Rejected: ${address:-<empty>}"
    fi

    local interface_name
    if ! interface_name="$(host_ipv4_interface_for_address "${address}")"; then
        fail "Trusted LAN address '${address}' is not assigned to an eligible host interface. Docker/veth/VPN-only interfaces are not supported for Trusted LAN mode in MEM 0.2.0."
    fi

    log_info "Trusted LAN Control Plane address validated: ${interface_name} · ${address}"
}

validate_control_plane_access_configuration() {
    local normalized_mode
    if ! normalized_mode="$(normalize_control_plane_access_mode "${CONTROL_PLANE_ACCESS_MODE:-}")"; then
        fail "Invalid Control Plane access mode '${CONTROL_PLANE_ACCESS_MODE:-}'. Expected ssh or trusted-lan."
    fi
    CONTROL_PLANE_ACCESS_MODE="${normalized_mode}"

    case "${CONTROL_PLANE_ACCESS_MODE}" in
        ssh-tunnel)
            if [[ "${CONTROL_PLANE_BIND_ADDRESS:-127.0.0.1}" != "127.0.0.1" ]]; then
                fail "SSH-tunnel Control Plane mode must bind to 127.0.0.1, not '${CONTROL_PLANE_BIND_ADDRESS}'."
            fi
            CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
            ;;
        trusted-lan)
            validate_trusted_lan_bind_address "${CONTROL_PLANE_BIND_ADDRESS:-}"
            ;;
    esac
}

bootstrap_input_is_interactive() {
    [[ -t 0 ]]
}

select_trusted_lan_address_interactively() {
    local -a candidates=()
    local candidate
    while IFS= read -r candidate; do
        [[ -n "${candidate}" ]] && candidates+=("${candidate}")
    done < <(eligible_trusted_lan_ipv4s)

    if (( ${#candidates[@]} == 0 )); then
        fail "Trusted LAN mode was requested, but MEM found no eligible RFC1918 address on a supported host interface."
    fi

    echo
    echo "Trusted LAN addresses"
    echo "---------------------"
    local index interface_name address
    for index in "${!candidates[@]}"; do
        IFS='|' read -r interface_name address <<< "${candidates[$index]}"
        printf '  %d. %s · %s\n' "$((index + 1))" "${interface_name}" "${address}"
    done
    echo
    echo "Choose only a private network your administration device can reach and trust."

    local response=""
    read -r -p "Trusted LAN address [1-${#candidates[@]}]: " response
    [[ "${response}" =~ ^[0-9]+$ ]] || fail "A Trusted LAN address selection is required."
    (( response >= 1 && response <= ${#candidates[@]} )) || fail "Trusted LAN selection '${response}' is out of range."

    IFS='|' read -r interface_name address <<< "${candidates[$((response - 1))]}"
    CONTROL_PLANE_BIND_ADDRESS="${address}"
    CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=true
    validate_trusted_lan_bind_address "${CONTROL_PLANE_BIND_ADDRESS}"
}

select_control_plane_access_interactively() {
    local -a candidates=()
    local candidate
    while IFS= read -r candidate; do
        [[ -n "${candidate}" ]] && candidates+=("${candidate}")
    done < <(eligible_trusted_lan_ipv4s)

    if (( ${#candidates[@]} == 0 )); then
        CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
        CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
        log_info "No eligible Trusted LAN address was detected. Control Plane administration will use SSH tunnel / local-only mode."
        return 0
    fi

    echo
    echo "MEM Control Plane access"
    echo "------------------------"
    echo
    echo "MEM does not expose the Control Plane on publicly routable interfaces."
    echo
    echo "  1. SSH tunnel / local-only (recommended default)"
    echo "     Bind only to 127.0.0.1."
    echo
    echo "  Trusted LAN candidates:"

    local index interface_name address
    for index in "${!candidates[@]}"; do
        IFS='|' read -r interface_name address <<< "${candidates[$index]}"
        printf '  %d. Trusted LAN · %s · %s\n' "$((index + 2))" "${interface_name}" "${address}"
    done

    echo
    echo "Choose a Trusted LAN only when devices on that private network are trusted for administration."

    local response=""
    read -r -p "Selection [1]: " response
    response="${response:-1}"

    if [[ "${response}" == "1" ]]; then
        CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
        CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
        return 0
    fi

    [[ "${response}" =~ ^[0-9]+$ ]] || fail "Invalid Control Plane access selection '${response}'."
    local candidate_index=$((response - 2))
    (( candidate_index >= 0 && candidate_index < ${#candidates[@]} )) || fail "Control Plane access selection '${response}' is out of range."

    IFS='|' read -r interface_name address <<< "${candidates[$candidate_index]}"
    CONTROL_PLANE_ACCESS_MODE="trusted-lan"
    CONTROL_PLANE_BIND_ADDRESS="${address}"
    CONTROL_PLANE_BIND_ADDRESS_EXPLICIT=true
    validate_trusted_lan_bind_address "${CONTROL_PLANE_BIND_ADDRESS}"
}

resolve_control_plane_access_policy() {
    local normalized_mode
    if ! normalized_mode="$(normalize_control_plane_access_mode "${CONTROL_PLANE_ACCESS_MODE:-ssh-tunnel}")"; then
        fail "Invalid Control Plane access mode '${CONTROL_PLANE_ACCESS_MODE:-}'. Expected ssh or trusted-lan."
    fi
    CONTROL_PLANE_ACCESS_MODE="${normalized_mode}"

    if [[ "${CONTROL_PLANE_ACCESS_MODE}" == "ssh-tunnel" && \
          "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT:-false}" == true ]]; then
        fail "--control-plane-bind-address is valid only with --control-plane-access trusted-lan."
    fi

    if [[ "${CONTROL_PLANE_ACCESS_EXPLICIT:-false}" != true ]]; then
        CONTROL_PLANE_ACCESS_MODE="ssh-tunnel"
        CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"

        if [[ "${ASSUME_YES:-false}" != true && "${DRY_RUN:-false}" != true ]] && \
           bootstrap_input_is_interactive; then
            select_control_plane_access_interactively
        fi
    elif [[ "${CONTROL_PLANE_ACCESS_MODE}" == "ssh-tunnel" ]]; then
        CONTROL_PLANE_BIND_ADDRESS="127.0.0.1"
    elif [[ "${CONTROL_PLANE_BIND_ADDRESS_EXPLICIT:-false}" != true ]]; then
        if [[ "${ASSUME_YES:-false}" == true || "${DRY_RUN:-false}" == true ]] || \
           ! bootstrap_input_is_interactive; then
            fail "Trusted LAN mode requires --control-plane-bind-address in non-interactive, --yes, or dry-run operation."
        fi
        select_trusted_lan_address_interactively
    fi

    validate_control_plane_access_configuration

    if [[ "${CONTROL_PLANE_ACCESS_MODE}" == "ssh-tunnel" ]]; then
        log_info "Control Plane administration: SSH tunnel / local-only (${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT})."
    else
        log_info "Control Plane administration: Trusted LAN (${CONTROL_PLANE_BIND_ADDRESS}:${CONTROL_PLANE_PORT})."
    fi
}

control_plane_handoff_ssh_user() {
    local candidate="${SUDO_USER:-${USER:-}}"

    if [[ ! "${candidate}" =~ ^[A-Za-z0-9._-]+$ ]]; then
        candidate="<ssh-user>"
    fi

    printf '%s\n' "${candidate:-<ssh-user>}"
}

control_plane_handoff_ssh_host() {
    local candidate=""

    # When bootstrap itself is running through SSH, SSH_CONNECTION is the
    # strongest available handoff evidence because it identifies the exact
    # server-side address the operator has already proven reachable.
    if [[ -n "${SSH_CONNECTION:-}" ]]; then
        local client_ip client_port server_ip server_port extra
        read -r client_ip client_port server_ip server_port extra <<< "${SSH_CONNECTION}"
        if [[ -z "${extra:-}" && -n "${server_ip:-}" && \
              "${server_ip}" =~ ^[A-Za-z0-9._:-]+$ && \
              "${server_ip}" != "127.0.0.1" && "${server_ip}" != "::1" ]]; then
            candidate="${server_ip}"
        fi
    fi

    # On a deliberately selected Trusted LAN, the bind address itself is a
    # safe SSH destination fallback for an operator already on that LAN. Do
    # not make the same guess in SSH/local-only mode: a cloud host may have
    # an unreachable RFC1918 VPC address as well as its public SSH endpoint.
    if [[ -z "${candidate}" && "${CONTROL_PLANE_ACCESS_MODE:-}" == "trusted-lan" ]]; then
        candidate="${CONTROL_PLANE_BIND_ADDRESS:-}"
    fi

    if [[ -z "${candidate}" ]]; then
        candidate="<server-address>"
    fi

    printf '%s\n' "${candidate}"
}

control_plane_ssh_tunnel_command() {
    local ssh_user ssh_host tunnel_destination
    ssh_user="$(control_plane_handoff_ssh_user)"
    ssh_host="$(control_plane_handoff_ssh_host)"
    tunnel_destination="${CONTROL_PLANE_BIND_ADDRESS:-127.0.0.1}"

    # Bind the workstation side to loopback too. The tunnel must not turn a
    # private MEM Control Plane into a service exposed on the operator's LAN.
    printf 'ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:%s:%s:%s %s@%s\n' \
        "${CONTROL_PLANE_PORT}" \
        "${tunnel_destination}" \
        "${CONTROL_PLANE_PORT}" \
        "${ssh_user}" \
        "${ssh_host}"
}

control_plane_ssh_browser_url() {
    printf 'https://127.0.0.1:%s\n' "${CONTROL_PLANE_PORT}"
}

control_plane_publish_spec() {
    # This helper is consumed through command substitution by container launch.
    # Validation may emit informational stdout in Trusted-LAN mode, so suppress
    # that chatter here and return only the Docker publication scalar.
    validate_control_plane_access_configuration >/dev/null
    printf '%s:%s:%s\n' \
        "${CONTROL_PLANE_BIND_ADDRESS}" \
        "${CONTROL_PLANE_PORT}" \
        "${CONTROL_PLANE_CONTAINER_HTTPS_PORT}"
}

control_plane_canonical_private_origin() {
    # Browser and health callers capture this helper through command
    # substitution. Keep validation fail-closed while returning only the
    # server-owned private Control Plane authority selected by the reviewed
    # access policy.
    validate_control_plane_access_configuration >/dev/null
    printf 'https://%s:%s\n' "${CONTROL_PLANE_BIND_ADDRESS}" "${CONTROL_PLANE_PORT}"
}

control_plane_health_base_url() {
    control_plane_canonical_private_origin
}

control_plane_certificate_extra_san_ip() {
    if [[ "${CONTROL_PLANE_ACCESS_MODE:-ssh-tunnel}" == "trusted-lan" ]]; then
        # This helper is consumed through command substitution by certificate
        # preparation, so validation chatter must never contaminate its stdout.
        # Validation failures still reach stderr via fail(), while the normal
        # informational message remains recorded by the bootstrap transcript.
        validate_trusted_lan_bind_address "${CONTROL_PLANE_BIND_ADDRESS:-}" >/dev/null
        printf '%s\n' "${CONTROL_PLANE_BIND_ADDRESS}"
    fi
}
