#!/usr/bin/env bash

# Prompt helpers for Message Easy Mode bootstrap installer.
# This file is sourced by bootstrap/install.sh.

confirm_or_exit() {
    local message="${1:-Continue?}"

    if [[ "${ASSUME_YES:-false}" == true ]]; then
        log_info "Assuming yes: ${message}"
        return 0
    fi

    echo
    echo "${message}"
    read -r -p "Continue? [y/N] " response

    case "${response}" in
        y|Y|yes|YES)
            return 0
            ;;
        *)
            fail "Cancelled by user."
            ;;
    esac
}
