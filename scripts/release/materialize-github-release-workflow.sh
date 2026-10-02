#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 022

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
DEFAULT_REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd -P)"
TEMPLATE="${SCRIPT_DIR}/templates/github-release-prepare.yml"
TARGET_RELATIVE=".github/workflows/release-prepare.yml"

MODE=""
REPO_ROOT="$DEFAULT_REPO_ROOT"

usage() {
  cat <<'USAGE'
Materialize the reviewed MEM GitHub release-preparation workflow.

Usage:
  ./scripts/release/materialize-github-release-workflow.sh --write [--repo-root <path>]
  ./scripts/release/materialize-github-release-workflow.sh --check [--repo-root <path>]
  ./scripts/release/materialize-github-release-workflow.sh --print

Modes:
  --write
      Create exactly .github/workflows/release-prepare.yml from the reviewed
      template. If an identical file already exists, succeed without changing it.
      If a different file exists, fail closed rather than overwrite it.

  --check
      Require the materialized workflow to exist and be byte-for-byte identical
      to the reviewed template.

  --print
      Print the reviewed template to stdout. --repo-root is not used.

Options:
  --repo-root <path>
      Repository root to write/check. Defaults to the repository containing this
      script. Useful for isolated tests.

The repository slice boundary deliberately does not grant arbitrary .github/
write authority. This helper is the explicit, narrowly scoped bridge for one
reviewed workflow path.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --write|--check|--print)
      [[ -z "$MODE" ]] || fail "Choose exactly one of --write, --check, or --print."
      MODE="$1"
      shift
      ;;
    --repo-root)
      [[ $# -ge 2 ]] || fail "--repo-root requires a path."
      REPO_ROOT="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

[[ -n "$MODE" ]] || {
  usage >&2
  exit 2
}

[[ -f "$TEMPLATE" && ! -L "$TEMPLATE" ]] || fail "Reviewed workflow template is missing or unsafe: $TEMPLATE"

if [[ "$MODE" == "--print" ]]; then
  cat -- "$TEMPLATE"
  exit 0
fi

[[ -d "$REPO_ROOT" ]] || fail "Repository root does not exist: $REPO_ROOT"
[[ ! -L "$REPO_ROOT" ]] || fail "Repository root may not be a symlink: $REPO_ROOT"
REPO_ROOT="$(cd -- "$REPO_ROOT" && pwd -P)"
TARGET="$REPO_ROOT/$TARGET_RELATIVE"
TARGET_DIR="$(dirname -- "$TARGET")"

# Refuse symlink traversal through the only two directories this helper creates.
for component in "$REPO_ROOT/.github" "$REPO_ROOT/.github/workflows"; do
  if [[ -L "$component" ]]; then
    fail "Workflow destination may not traverse a symlink: ${component#$REPO_ROOT/}"
  fi
done
if [[ -L "$TARGET" ]]; then
  fail "Workflow destination may not be a symlink: $TARGET_RELATIVE"
fi

case "$MODE" in
  --check)
    [[ -f "$TARGET" ]] || fail "Materialized workflow is missing: $TARGET_RELATIVE"
    cmp -s -- "$TEMPLATE" "$TARGET" || fail "Materialized workflow differs from reviewed template: $TARGET_RELATIVE"
    printf 'PASS: %s matches reviewed template\n' "$TARGET_RELATIVE"
    ;;
  --write)
    if [[ -e "$TARGET" ]]; then
      [[ -f "$TARGET" ]] || fail "Workflow destination exists but is not a regular file: $TARGET_RELATIVE"
      if cmp -s -- "$TEMPLATE" "$TARGET"; then
        printf 'Already current: %s\n' "$TARGET_RELATIVE"
        exit 0
      fi
      fail "Refusing to overwrite a different $TARGET_RELATIVE. Review/remove it explicitly first."
    fi

    mkdir -p -- "$TARGET_DIR"
    [[ ! -L "$REPO_ROOT/.github" && ! -L "$TARGET_DIR" ]] || \
      fail "Workflow destination became a symlink during materialization."

    TMP_FILE="$(mktemp "$TARGET_DIR/.release-prepare.yml.XXXXXX")"
    cleanup() { rm -f -- "$TMP_FILE"; }
    trap cleanup EXIT
    cp -- "$TEMPLATE" "$TMP_FILE"
    chmod 0644 "$TMP_FILE"
    mv -- "$TMP_FILE" "$TARGET"
    trap - EXIT
    printf 'Materialized: %s\n' "$TARGET_RELATIVE"
    ;;
esac
