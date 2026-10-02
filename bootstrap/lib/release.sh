#!/usr/bin/env bash

# Release identity helpers shared by the bootstrap and its tests.
#
# Important: MEM release channel (stable|prerelease) is product/release
# provenance. It is intentionally distinct from the bootstrap's mutable
# Control Plane image selector (stable|dev), which remains a development
# override surface and is not release provenance.

mem_release_channel_for_version() {
    local version="${1:-}"

    if [[ "${version}" == *-* ]]; then
        printf '%s\n' "prerelease"
    else
        printf '%s\n' "stable"
    fi
}

mem_release_channel_contract_is_valid() {
    local version="${1:-}"
    local channel="${2:-}"
    local expected_channel

    case "${channel}" in
        stable|prerelease)
            ;;
        *)
            return 1
            ;;
    esac

    expected_channel="$(mem_release_channel_for_version "${version}")"
    [[ "${channel}" == "${expected_channel}" ]]
}

bootstrap_operator_channel() {
    if [[ "${MEM_RELEASE_MODE:-development}" == "release" ]]; then
        printf '%s\n' "${MEM_RELEASE_CHANNEL:-}"
    else
        printf '%s\n' "${CHANNEL:-stable}"
    fi
}
