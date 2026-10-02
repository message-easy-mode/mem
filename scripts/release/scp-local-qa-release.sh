#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

BUNDLE=""
TARGET=""
REMOTE_ROOT="/tmp/mem-local-qa"
REPLACE=false
DRY_RUN=false

usage() {
  cat <<'USAGE'
Copy a packaged MEM local-QA release to an Ubuntu test server over SSH/SCP.

Usage:
  ./scripts/release/scp-local-qa-release.sh \
    --bundle ./artifacts/local-qa/mem-local-qa-0.2.0-rc.1.tar.gz \
    --target user@ubuntu-vm \
    [--remote-root /tmp/mem-local-qa] \
    [--replace] \
    [--dry-run]

The script:
  * validates the local archive shape before network activity;
  * SCPs one transport archive to a temporary remote filename;
  * verifies the archive SHA-256 on the remote host;
  * extracts into a temporary directory;
  * verifies QA-SHA256SUMS and the public release SHA256SUMS remotely; and
  * atomically renames the verified kit to:

      <remote-root>/mem-local-qa-<version>/

It never runs the MEM installer automatically.

SSH authentication/configuration is deliberately left to normal OpenSSH config,
ssh-agent or explicit operator setup. No password, token or private key is
accepted by this script.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

validate_target() {
  [[ -n "$TARGET" ]] || fail "--target is required."
  [[ "$TARGET" =~ ^[A-Za-z0-9_.@:-]+$ ]] || \
    fail "--target contains unsupported characters. Prefer an SSH config host or user@host."
}

validate_remote_root() {
  [[ "$REMOTE_ROOT" == /* ]] || fail "--remote-root must be an absolute path."
  [[ "$REMOTE_ROOT" =~ ^/[A-Za-z0-9._/-]+$ ]] || fail "--remote-root contains unsupported characters."
  [[ "$REMOTE_ROOT" != *"//"* ]] || fail "--remote-root may not contain empty path components."
  case "/$REMOTE_ROOT/" in
    */../*|*/./*) fail "--remote-root may not contain . or .. path components." ;;
  esac
}

shell_quote() {
  printf '%q' "$1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bundle)
      [[ $# -ge 2 ]] || fail "--bundle requires a path."
      BUNDLE="$2"
      shift 2
      ;;
    --target)
      [[ $# -ge 2 ]] || fail "--target requires user@host or an SSH config host."
      TARGET="$2"
      shift 2
      ;;
    --remote-root)
      [[ $# -ge 2 ]] || fail "--remote-root requires an absolute path."
      REMOTE_ROOT="$2"
      shift 2
      ;;
    --replace)
      REPLACE=true
      shift
      ;;
    --dry-run)
      DRY_RUN=true
      shift
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

[[ -n "$BUNDLE" ]] || fail "--bundle is required."
validate_target
validate_remote_root

for command_name in python3 realpath sha256sum basename dirname awk; do
  require_command "$command_name"
done
if [[ "$DRY_RUN" != true ]]; then
  require_command ssh
  require_command scp
fi

BUNDLE="$(realpath -e -- "$BUNDLE")" || fail "QA bundle does not exist."
[[ -f "$BUNDLE" && ! -L "$BUNDLE" ]] || fail "QA bundle must be a regular non-symlink file."
BASENAME="$(basename -- "$BUNDLE")"
[[ "$BASENAME" =~ ^mem-local-qa-(.+)\.tar\.gz$ ]] || \
  fail "QA bundle filename must be mem-local-qa-<version>.tar.gz."
VERSION="${BASH_REMATCH[1]}"
[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] || \
  fail "QA bundle filename contains an invalid release version: $VERSION"
KIT_NAME="mem-local-qa-${VERSION}"

python3 - "$BUNDLE" "$KIT_NAME" <<'PY'
import pathlib, sys, tarfile
archive_path, root = sys.argv[1:]
required = {
    f"{root}/QA-SHA256SUMS",
    f"{root}/STAGING-CONTROL-PLANE-IMAGE",
    f"{root}/install-local.sh",
    f"{root}/release/release.json",
    f"{root}/release/SHA256SUMS",
    f"{root}/release/install-mem-{root.removeprefix('mem-local-qa-')}.sh",
}
seen = set()
try:
    archive = tarfile.open(archive_path, "r:gz")
except (OSError, tarfile.TarError) as exc:
    raise SystemExit(f"ERROR: QA bundle cannot be opened safely: {exc}")
with archive:
    for member in archive.getmembers():
        name = member.name.rstrip("/")
        path = pathlib.PurePosixPath(name)
        if (
            not name
            or path.is_absolute()
            or any(part in ("", ".", "..") for part in path.parts)
            or not path.parts
            or path.parts[0] != root
        ):
            raise SystemExit(f"ERROR: unsafe QA archive path: {member.name}")
        if name in seen:
            raise SystemExit(f"ERROR: duplicate QA archive path: {member.name}")
        seen.add(name)
        if not (member.isfile() or member.isdir()):
            raise SystemExit(f"ERROR: QA archive may contain only regular files/directories: {member.name}")
    missing = required - seen
    if missing:
        raise SystemExit("ERROR: QA archive is missing required paths: " + ", ".join(sorted(missing)))
PY

BUNDLE_SHA="$(sha256sum "$BUNDLE" | awk '{print $1}')"
REMOTE_INCOMING="${REMOTE_ROOT}/.incoming-${KIT_NAME}-${BUNDLE_SHA:0:12}.tar.gz"
REMOTE_INCOMING_BASENAME="$(basename -- "$REMOTE_INCOMING")"
REMOTE_FINAL="${REMOTE_ROOT}/${KIT_NAME}"
REMOTE_TMP="${REMOTE_ROOT}/.incoming-${KIT_NAME}-${BUNDLE_SHA:0:12}"

REMOTE_ROOT_Q="$(shell_quote "$REMOTE_ROOT")"
REMOTE_INCOMING_Q="$(shell_quote "$REMOTE_INCOMING")"
REMOTE_FINAL_Q="$(shell_quote "$REMOTE_FINAL")"
REMOTE_TMP_Q="$(shell_quote "$REMOTE_TMP")"
KIT_NAME_Q="$(shell_quote "$KIT_NAME")"
REMOTE_INCOMING_BASENAME_Q="$(shell_quote "$REMOTE_INCOMING_BASENAME")"
BUNDLE_SHA_Q="$(shell_quote "$BUNDLE_SHA")"
REPLACE_VALUE=false
[[ "$REPLACE" == true ]] && REPLACE_VALUE=true

REMOTE_SCRIPT=$(cat <<EOF_REMOTE
set -Eeuo pipefail
umask 077
mkdir -p -- $REMOTE_ROOT_Q
if [[ -e $REMOTE_FINAL_Q && "$REPLACE_VALUE" != true ]]; then
  echo "ERROR: verified QA kit already exists: $REMOTE_FINAL" >&2
  exit 21
fi
printf '%s  %s\\n' $BUNDLE_SHA_Q $REMOTE_INCOMING_BASENAME_Q | (cd $REMOTE_ROOT_Q && sha256sum -c - >/dev/null)
rm -rf -- $REMOTE_TMP_Q
mkdir -p -- $REMOTE_TMP_Q
tar -xzf $REMOTE_INCOMING_Q -C $REMOTE_TMP_Q
[[ -d $REMOTE_TMP_Q/$KIT_NAME_Q ]] || {
  echo "ERROR: extracted QA kit root is missing." >&2
  exit 22
}
(
  cd $REMOTE_TMP_Q/$KIT_NAME_Q
  sha256sum -c QA-SHA256SUMS >/dev/null
  cd release
  sha256sum -c SHA256SUMS >/dev/null
)
if [[ -e $REMOTE_FINAL_Q ]]; then
  rm -rf -- $REMOTE_FINAL_Q
fi
mv -- $REMOTE_TMP_Q/$KIT_NAME_Q $REMOTE_FINAL_Q
rm -rf -- $REMOTE_TMP_Q
rm -f -- $REMOTE_INCOMING_Q
printf 'PASS: staged MEM QA kit at %s\\n' $REMOTE_FINAL_Q
EOF_REMOTE
)

printf 'MEM QA copy plan\n'
printf 'Bundle:      %s\n' "$BUNDLE"
printf 'SHA-256:     %s\n' "$BUNDLE_SHA"
printf 'Target:      %s\n' "$TARGET"
printf 'Remote path: %s\n' "$REMOTE_FINAL"

if [[ "$DRY_RUN" == true ]]; then
  printf '\n[dry-run] scp %q %q\n' "$BUNDLE" "${TARGET}:${REMOTE_INCOMING}"
  printf '[dry-run] ssh %q <remote verification/extract script>\n' "$TARGET"
  exit 0
fi

printf '[qa] Preparing remote staging directory...\n'
ssh -- "$TARGET" "mkdir -p -- $REMOTE_ROOT_Q"
printf '[qa] Copying transport archive...\n'
scp -- "$BUNDLE" "${TARGET}:${REMOTE_INCOMING}"
printf '[qa] Verifying and publishing QA kit on remote host...\n'
ssh -- "$TARGET" "bash -s" <<< "$REMOTE_SCRIPT"

printf '\nMEM QA kit copied successfully.\n'
printf 'On the test VM run:\n\n'
printf '  cd %q\n' "$REMOTE_FINAL"
printf '  sudo ./install-local.sh\n'
