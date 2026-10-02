#!/usr/bin/env bash
set -Eeuo pipefail

# Message Easy Mode root installer entrypoint.
#
# This file intentionally stays small.
# The real bootstrap logic lives in:
#
#   bootstrap/install.sh
#
# This lets users keep running:
#
#   sudo ./install.sh
#
# while the installer implementation evolves underneath.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BOOTSTRAP_SCRIPT="${SCRIPT_DIR}/bootstrap/install.sh"

if [[ ! -f "${BOOTSTRAP_SCRIPT}" ]]; then
    echo "ERROR: bootstrap installer not found at: ${BOOTSTRAP_SCRIPT}" >&2
    exit 1
fi

if [[ ! -x "${BOOTSTRAP_SCRIPT}" ]]; then
    echo "ERROR: bootstrap installer is not executable: ${BOOTSTRAP_SCRIPT}" >&2
    echo "Fix with:" >&2
    echo "  chmod +x ${BOOTSTRAP_SCRIPT}" >&2
    exit 1
fi

exec "${BOOTSTRAP_SCRIPT}" "$@"