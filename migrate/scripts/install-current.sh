#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

usage() {
  cat <<'USAGE'
Usage:
  sudo ./scripts/install-current.sh \
    --publish-dir <path> \
    --version <version> \
    [--metadata-dir <release-root>]

Installs a complete, self-contained MEM Migrate payload into:
  /opt/mem/migrate/releases/<version>

and atomically updates:
  /opt/mem/migrate/current

Create the combined payload with:
  ./scripts/publish-current.sh \
    --output /tmp/mem-migrate-publish \
    --runtime linux-x64 \
    --configuration Release

The payload must include both:
  mem-migrate
  mem-migrate-web

For a packaged release, --metadata-dir must contain VERSION and BUILD-INFO.json.
Those files are retained in the installed release root.
USAGE
}

fail() { echo "ERROR: $*" >&2; exit 1; }

PUBLISH_DIR=""
VERSION=""
METADATA_DIR=""
INSTALL_ROOT="/opt/mem/migrate"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish-dir) [[ $# -ge 2 ]] || fail "--publish-dir requires a path"; PUBLISH_DIR="$2"; shift 2 ;;
    --version) [[ $# -ge 2 ]] || fail "--version requires a value"; VERSION="$2"; shift 2 ;;
    --metadata-dir) [[ $# -ge 2 ]] || fail "--metadata-dir requires a path"; METADATA_DIR="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Unknown argument: $1" ;;
  esac
done

[[ ${EUID:-$(id -u)} -eq 0 ]] || fail "Run as root (sudo)."
[[ -n "$PUBLISH_DIR" && -d "$PUBLISH_DIR" ]] || fail "A valid --publish-dir is required."
[[ "$VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] || fail "Version contains unsupported characters."
[[ -f "$PUBLISH_DIR/mem-migrate" ]] || fail "Publish payload is missing mem-migrate."
[[ -f "$PUBLISH_DIR/mem-migrate-web" ]] || fail "Publish payload is missing mem-migrate-web."
[[ -f "$PUBLISH_DIR/libe_sqlite3.so" ]] || fail "Publish payload is missing libe_sqlite3.so."
[[ -f "$PUBLISH_DIR/wwwroot/index.html" ]] || fail "Publish payload is missing Source Assistant static assets."

if [[ -n "$METADATA_DIR" ]]; then
  [[ -d "$METADATA_DIR" ]] || fail "Metadata directory does not exist: $METADATA_DIR"
  [[ -f "$METADATA_DIR/VERSION" ]] || fail "Release metadata is missing VERSION."
  [[ -f "$METADATA_DIR/BUILD-INFO.json" ]] || fail "Release metadata is missing BUILD-INFO.json."

  METADATA_VERSION="$(tr -d '[:space:]' < "$METADATA_DIR/VERSION")"
  [[ "$METADATA_VERSION" == "$VERSION" ]] || fail "Release metadata VERSION does not match --version."

  METADATA_BUILD_VERSION="$(sed -nE 's/^[[:space:]]*"releaseVersion"[[:space:]]*:[[:space:]]*"([^"]+)"[[:space:]]*,?[[:space:]]*$/\1/p' "$METADATA_DIR/BUILD-INFO.json" | sed -n '1p')"
  [[ "$METADATA_BUILD_VERSION" == "$VERSION" ]] || fail "Release metadata BUILD-INFO.json does not match --version."
fi

RELEASES_ROOT="$INSTALL_ROOT/releases"
RELEASE_DIR="$RELEASES_ROOT/$VERSION"
STAGING_DIR="$RELEASES_ROOT/.${VERSION}.partial.$$"
CURRENT_LINK="$INSTALL_ROOT/current"
WEB_LOG="$(mktemp /tmp/mem-migrate-web-install.XXXXXX)"
WEB_PID=""

cleanup() {
  if [[ -n "$WEB_PID" ]]; then
    kill "$WEB_PID" >/dev/null 2>&1 || true
    wait "$WEB_PID" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$STAGING_DIR"
  rm -f -- "$WEB_LOG"
}
trap cleanup EXIT

mkdir -p "$RELEASES_ROOT"
[[ ! -e "$RELEASE_DIR" ]] || fail "Release already exists: $RELEASE_DIR"
rm -rf "$STAGING_DIR"
mkdir -m 0755 "$STAGING_DIR"

cp -a "$PUBLISH_DIR/." "$STAGING_DIR/"
if [[ -n "$METADATA_DIR" ]]; then
  cp -a -- "$METADATA_DIR/VERSION" "$STAGING_DIR/VERSION"
  cp -a -- "$METADATA_DIR/BUILD-INFO.json" "$STAGING_DIR/BUILD-INFO.json"
fi
chmod 0755 "$STAGING_DIR/mem-migrate" "$STAGING_DIR/mem-migrate-web"
chmod 0644 "$STAGING_DIR/libe_sqlite3.so"
if [[ -n "$METADATA_DIR" ]]; then
  chmod 0644 "$STAGING_DIR/VERSION" "$STAGING_DIR/BUILD-INFO.json"
fi
chmod 0600 "$WEB_LOG"

# The authoritative self-contained check is execution under a minimal
# environment. This accepts both single-file and multi-file self-contained
# publishes while rejecting framework-dependent or incomplete payloads.
STAGED_CLI_VERSION="$(env -i PATH=/usr/bin:/bin HOME=/root \
  "$STAGING_DIR/mem-migrate" version 2>/dev/null || true)"
[[ "$STAGED_CLI_VERSION" == "$VERSION" ]] ||
  fail "Published CLI reports '$STAGED_CLI_VERSION', expected release '$VERSION'."

STAGED_WEB_VERSION="$(env -i PATH=/usr/bin:/bin HOME=/root \
  "$STAGING_DIR/mem-migrate-web" --version 2>/dev/null || true)"
[[ "$STAGED_WEB_VERSION" == "$VERSION" ]] ||
  fail "Published Source Assistant reports '$STAGED_WEB_VERSION', expected release '$VERSION'."

env -i PATH=/usr/bin:/bin HOME=/root \
  "$STAGING_DIR/mem-migrate" worker convert --help 2>&1 \
  | grep -q "Conversion worker options" \
  || fail "Published payload does not expose the conversion worker contract."

# Start the temporary Web host on loopback and prove that the anonymous health
# endpoint responds before making the release current. Bash /dev/tcp avoids a
# new curl or Python installation dependency.
HEALTH_PORT=$((20000 + RANDOM % 20000))
env -i PATH=/usr/bin:/bin HOME=/root \
  "$STAGING_DIR/mem-migrate-web" \
  --listen-address 127.0.0.1 \
  --port "$HEALTH_PORT" \
  --workspace "$STAGING_DIR/.self-test/work" \
  --artifacts "$STAGING_DIR/.self-test/artifacts" \
  >"$WEB_LOG" 2>&1 &
WEB_PID=$!

HEALTHY=false
HEALTH_VERSION_MISMATCH=false
for _ in $(seq 1 50); do
  if ! kill -0 "$WEB_PID" >/dev/null 2>&1; then
    break
  fi

  if { exec 3<>"/dev/tcp/127.0.0.1/$HEALTH_PORT"; } 2>/dev/null; then
    printf 'GET /api/health HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3
    HEALTH_RESPONSE="$(cat <&3 2>/dev/null || true)"
    exec 3>&- 3<&-
    STATUS_LINE="${HEALTH_RESPONSE%%$'\n'*}"
    if [[ "$STATUS_LINE" == *" 200 "* ]]; then
      if printf '%s' "$HEALTH_RESPONSE" | grep -Fq "\"version\":\"$VERSION\""; then
        HEALTHY=true
        break
      fi
      HEALTH_VERSION_MISMATCH=true
    fi
  fi
  sleep 0.1
done

if [[ "$HEALTHY" != true ]]; then
  echo "Source Assistant self-test log:" >&2
  sed -E 's/^(Access code:).*/\1 [redacted]/' "$WEB_LOG" >&2 || true
  if [[ "$HEALTH_VERSION_MISMATCH" == true ]]; then
    fail "Published Source Assistant health did not report release version $VERSION."
  fi
  fail "Published Source Assistant did not pass its loopback health check."
fi

kill "$WEB_PID" >/dev/null 2>&1 || true
wait "$WEB_PID" >/dev/null 2>&1 || true
WEB_PID=""
rm -rf -- "$STAGING_DIR/.self-test"

mv "$STAGING_DIR" "$RELEASE_DIR"
ln -sfn "$RELEASE_DIR" "$CURRENT_LINK.new"
mv -Tf "$CURRENT_LINK.new" "$CURRENT_LINK"

if [[ -n "$METADATA_DIR" ]]; then
  [[ "$(tr -d '[:space:]' < "$CURRENT_LINK/VERSION")" == "$VERSION" ]] ||
    fail "Installed VERSION does not match the activated release."
  [[ -f "$CURRENT_LINK/BUILD-INFO.json" ]] ||
    fail "Installed release is missing BUILD-INFO.json after activation."
fi

trap - EXIT
rm -f -- "$WEB_LOG"

printf '%s\n' "Installed self-contained MEM Migrate release: $RELEASE_DIR"
printf '%s\n' "Current CLI: $CURRENT_LINK/mem-migrate"
printf '%s\n' "Current Source Assistant: $CURRENT_LINK/mem-migrate-web"
env -i PATH=/usr/bin:/bin HOME=/root "$CURRENT_LINK/mem-migrate" version
printf '%s\n' ""
printf '%s\n' "Start the temporary Source Assistant with:"
printf '%s\n' "  sudo $CURRENT_LINK/mem-migrate-web"
