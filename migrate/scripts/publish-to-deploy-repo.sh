#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/publish-to-deploy-repo.sh [options]

Builds the current MEM Migrate Linux payload and publishes the generated
deployment repository content into the sibling `mem-migrate` repository by
default. Use --deploy-repo or MEM_MIGRATE_DEPLOY_REPO to override it.

Options:
  --deploy-repo <path>     Override the deployment repository path.
  --runtime <rid>          .NET runtime identifier. Default: linux-x64.
  --configuration <name>   Build configuration. Default: Release.
  --release-version <ver>  Build an exact public release version instead of a timestamped development publication.
  --release-channel <name> Public exact release channel: stable or prerelease. Inferred from the version when omitted.
  --allow-dirty-release    Permit a dirty exact-release development proof. The generated BUILD-INFO marks it release-ineligible.
  -h, --help               Show this help.

The deployment repository must:
  - already exist;
  - be the root of a Git repository;
  - have a clean working tree.

The script does not commit or push.
USAGE
}

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 ||
    fail "Required command not found: $1"
}

DEPLOY_REPO="${MEM_MIGRATE_DEPLOY_REPO:-}"
RUNTIME="linux-x64"
CONFIGURATION="Release"
EXACT_RELEASE_VERSION=""
EXACT_RELEASE_CHANNEL=""
ALLOW_DIRTY_RELEASE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --deploy-repo)
      [[ $# -ge 2 ]] || fail "--deploy-repo requires a path."
      DEPLOY_REPO="$2"
      shift 2
      ;;
    --runtime)
      [[ $# -ge 2 ]] || fail "--runtime requires a value."
      RUNTIME="$2"
      shift 2
      ;;
    --configuration)
      [[ $# -ge 2 ]] || fail "--configuration requires a value."
      CONFIGURATION="$2"
      shift 2
      ;;
    --release-version)
      [[ $# -ge 2 ]] || fail "--release-version requires a semantic version."
      EXACT_RELEASE_VERSION="$2"
      shift 2
      ;;
    --release-channel)
      [[ $# -ge 2 ]] || fail "--release-channel requires stable or prerelease."
      EXACT_RELEASE_CHANNEL="$2"
      shift 2
      ;;
    --allow-dirty-release)
      ALLOW_DIRTY_RELEASE=true
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

if [[ -n "$EXACT_RELEASE_VERSION" ]]; then
  [[ "$EXACT_RELEASE_VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] ||
    fail "--release-version must be an ordinary semantic version or prerelease version."

  if [[ -z "$EXACT_RELEASE_CHANNEL" ]]; then
    if [[ "$EXACT_RELEASE_VERSION" == *-* ]]; then
      EXACT_RELEASE_CHANNEL="prerelease"
    else
      EXACT_RELEASE_CHANNEL="stable"
    fi
  fi

  [[ "$EXACT_RELEASE_CHANNEL" == "stable" || "$EXACT_RELEASE_CHANNEL" == "prerelease" ]] ||
    fail "--release-channel must be stable or prerelease."

  if [[ "$EXACT_RELEASE_CHANNEL" == "stable" && "$EXACT_RELEASE_VERSION" == *-* ]]; then
    fail "stable release channel cannot use a prerelease version."
  fi
  if [[ "$EXACT_RELEASE_CHANNEL" == "prerelease" && "$EXACT_RELEASE_VERSION" != *-* ]]; then
    fail "prerelease channel requires a prerelease version."
  fi
elif [[ -n "$EXACT_RELEASE_CHANNEL" || "$ALLOW_DIRTY_RELEASE" == true ]]; then
  fail "--release-channel and --allow-dirty-release require --release-version."
fi

for command_name in \
  git \
  realpath \
  mktemp \
  cp \
  mv \
  rm \
  find \
  sort \
  sha256sum \
  python3 \
  curl \
  sed \
  tar; do
  require_command "$command_name"
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MIGRATE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
PUBLISH_SCRIPT="$MIGRATE_ROOT/scripts/publish-current.sh"
INSTALL_SCRIPT="$MIGRATE_ROOT/scripts/install-current.sh"
BUNDLE_SCRIPT="$MIGRATE_ROOT/scripts/create-release-bundle.sh"
BOOTSTRAP_INSTALLER="$MIGRATE_ROOT/bootstrap/install.sh"
DEV_TARGET_INSTALLER="$MIGRATE_ROOT/scripts/install-dev-target-worker.sh"

[[ -x "$PUBLISH_SCRIPT" ]] ||
  fail "Publish script is missing or not executable: $PUBLISH_SCRIPT"
[[ -f "$INSTALL_SCRIPT" ]] ||
  fail "Install script is missing: $INSTALL_SCRIPT"
[[ -x "$BUNDLE_SCRIPT" ]] ||
  fail "Release-bundle script is missing or not executable: $BUNDLE_SCRIPT"
[[ -x "$BOOTSTRAP_INSTALLER" ]] ||
  fail "Bootstrap installer is missing or not executable: $BOOTSTRAP_INSTALLER"
[[ -x "$DEV_TARGET_INSTALLER" ]] ||
  fail "Development target-worker installer is missing or not executable: $DEV_TARGET_INSTALLER"

SOURCE_REPO_ROOT="$(git -C "$MIGRATE_ROOT" rev-parse --show-toplevel 2>/dev/null)" ||
  fail "The MEM Migrate source is not inside a Git repository."
SOURCE_REPO_ROOT="$(realpath -m -- "$SOURCE_REPO_ROOT")"

if [[ -z "$DEPLOY_REPO" ]]; then
  DEFAULT_DEPLOY_PARENT="$(cd -- "$SOURCE_REPO_ROOT/.." && pwd -P)"
  DEPLOY_REPO="$DEFAULT_DEPLOY_PARENT/mem-migrate"
fi

DEPLOY_REPO="$(realpath -m -- "$DEPLOY_REPO")"
[[ "$DEPLOY_REPO" != "/" ]] ||
  fail "Refusing to use the filesystem root as the deployment repository."
[[ -d "$DEPLOY_REPO" ]] ||
  fail "Deployment repository does not exist: $DEPLOY_REPO"

DEPLOY_GIT_ROOT="$(git -C "$DEPLOY_REPO" rev-parse --show-toplevel 2>/dev/null)" || {
  cat >&2 <<EOF
ERROR: The deployment directory is not a Git repository:

  $DEPLOY_REPO

Initialise it first:

  cd "$DEPLOY_REPO"
  git init
  git branch -M main
EOF
  exit 1
}
DEPLOY_GIT_ROOT="$(realpath -m -- "$DEPLOY_GIT_ROOT")"

[[ "$DEPLOY_GIT_ROOT" == "$DEPLOY_REPO" ]] ||
  fail "Deployment path must be the Git repository root. Git root: $DEPLOY_GIT_ROOT"

case "$DEPLOY_REPO/" in
  "$SOURCE_REPO_ROOT/"*)
    fail "Deployment repository must not live inside the source repository."
    ;;
esac

DEPLOY_STATUS="$(git -C "$DEPLOY_REPO" status --porcelain --untracked-files=all)"
[[ -z "$DEPLOY_STATUS" ]] || {
  echo "Deployment repository has uncommitted or untracked changes:" >&2
  printf '%s\n' "$DEPLOY_STATUS" >&2
  fail "Commit, discard, or move those changes before publishing."
}

SOURCE_COMMIT="$(git -C "$SOURCE_REPO_ROOT" rev-parse HEAD)"
SOURCE_SHORT_COMMIT="${SOURCE_COMMIT:0:12}"
SOURCE_BRANCH="$(
  git -C "$SOURCE_REPO_ROOT" symbolic-ref --quiet --short HEAD 2>/dev/null ||
    echo "detached"
)"
SOURCE_DIRTY=false
if [[ -n "$(git -C "$SOURCE_REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  SOURCE_DIRTY=true
  echo "WARNING: The source repository has uncommitted changes." >&2
  echo "         This development build will be marked as dirty." >&2
fi

if [[ -n "$EXACT_RELEASE_VERSION" && "$SOURCE_DIRTY" == true && "$ALLOW_DIRTY_RELEASE" != true ]]; then
  fail "Exact public release publication requires a clean source worktree. Use --allow-dirty-release for development proof only."
fi

RELEASE_ELIGIBLE=true
if [[ "$SOURCE_DIRTY" == true || "$ALLOW_DIRTY_RELEASE" == true ]]; then
  RELEASE_ELIGIBLE=false
fi

BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-deploy-publish.XXXXXX")"
PUBLISH_DIR="$BUILD_ROOT/payload"
HEALTH_LOG="$BUILD_ROOT/source-assistant-health.log"
HEALTH_WORKSPACE="$BUILD_ROOT/health/work"
HEALTH_ARTIFACTS="$BUILD_ROOT/health/artifacts"
STAGE_ROOT="$DEPLOY_REPO/.publish-staging.$$"
BACKUP_ROOT="$DEPLOY_REPO/.publish-backup.$$"
WEB_PID=""
UPDATE_STATE="not-started"

MANAGED_PATHS=(
  payload
  scripts
  VERSION
  BUILD-INFO.json
  SHA256SUMS
  install.sh
  run-web.sh
  stop-web.sh
  status.sh
  README.md
  .gitignore
  release-bundle
)

restore_previous_deployment_content() {
  local relative_path

  for relative_path in "${MANAGED_PATHS[@]}"; do
    rm -rf -- "$DEPLOY_REPO/$relative_path"
  done

  if [[ -d "$BACKUP_ROOT" ]]; then
    for relative_path in "${MANAGED_PATHS[@]}"; do
      if [[ -e "$BACKUP_ROOT/$relative_path" ||
            -L "$BACKUP_ROOT/$relative_path" ]]; then
        mkdir -p -- "$(dirname -- "$DEPLOY_REPO/$relative_path")"
        mv -- "$BACKUP_ROOT/$relative_path" "$DEPLOY_REPO/$relative_path"
      fi
    done
  fi
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM

  if [[ -n "$WEB_PID" ]]; then
    kill "$WEB_PID" >/dev/null 2>&1 || true
    wait "$WEB_PID" >/dev/null 2>&1 || true
  fi

  if [[ "$UPDATE_STATE" == "partial" ]]; then
    echo "Publish failed after deployment-repository replacement began; restoring previous content." >&2
    restore_previous_deployment_content
  fi

  rm -rf -- "$BUILD_ROOT" "$STAGE_ROOT" "$BACKUP_ROOT"
  exit "$status"
}
trap cleanup EXIT INT TERM

echo "Publishing MEM Migrate..."
echo "  Source root:      $MIGRATE_ROOT"
echo "  Source commit:    $SOURCE_COMMIT"
echo "  Source dirty:     $SOURCE_DIRTY"
echo "  Deployment repo:  $DEPLOY_REPO"
echo "  Runtime:          $RUNTIME"
echo "  Configuration:    $CONFIGURATION"
echo

PUBLISH_VERSION_ARGS=()
if [[ -n "$EXACT_RELEASE_VERSION" ]]; then
  PUBLISH_VERSION_ARGS=(--version "$EXACT_RELEASE_VERSION")
fi

"$PUBLISH_SCRIPT" \
  --output "$PUBLISH_DIR" \
  --runtime "$RUNTIME" \
  --configuration "$CONFIGURATION" \
  "${PUBLISH_VERSION_ARGS[@]}"

[[ -x "$PUBLISH_DIR/mem-migrate" ]] ||
  fail "Published payload is missing executable mem-migrate."
[[ -x "$PUBLISH_DIR/mem-migrate-web" ]] ||
  fail "Published payload is missing executable mem-migrate-web."
[[ -f "$PUBLISH_DIR/libe_sqlite3.so" ]] ||
  fail "Published payload is missing libe_sqlite3.so."
[[ -f "$PUBLISH_DIR/wwwroot/index.html" ]] ||
  fail "Published payload is missing Source Assistant static assets."

PRODUCT_VERSION="$("$PUBLISH_DIR/mem-migrate" version | head -n 1 | tr -d '\r')"
WEB_VERSION="$("$PUBLISH_DIR/mem-migrate-web" --version | head -n 1 | tr -d '\r')"

[[ -n "$PRODUCT_VERSION" ]] ||
  fail "Published CLI did not report a version."
[[ -n "$WEB_VERSION" ]] ||
  fail "Published Source Assistant did not report a version."

if [[ -n "$EXACT_RELEASE_VERSION" ]]; then
  [[ "$PRODUCT_VERSION" == "$EXACT_RELEASE_VERSION" ]] ||
    fail "Published CLI version '$PRODUCT_VERSION' does not match requested release '$EXACT_RELEASE_VERSION'."
  [[ "$WEB_VERSION" == "$EXACT_RELEASE_VERSION" ]] ||
    fail "Published Source Assistant version '$WEB_VERSION' does not match requested release '$EXACT_RELEASE_VERSION'."
fi

"$PUBLISH_DIR/mem-migrate" worker convert --help 2>&1 |
  grep -q "Conversion worker options" ||
  fail "Published payload does not expose the conversion worker contract."

HEALTH_PORT="$(
  python3 - <<'PY'
import socket
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
)"

mkdir -p -- "$HEALTH_WORKSPACE" "$HEALTH_ARTIFACTS"
chmod 0700 "$BUILD_ROOT/health" "$HEALTH_WORKSPACE" "$HEALTH_ARTIFACTS"

"$PUBLISH_DIR/mem-migrate-web" \
  --listen-address 127.0.0.1 \
  --port "$HEALTH_PORT" \
  --workspace "$HEALTH_WORKSPACE" \
  --artifacts "$HEALTH_ARTIFACTS" \
  >"$HEALTH_LOG" 2>&1 &
WEB_PID=$!

HEALTHY=false
for _ in $(seq 1 80); do
  if ! kill -0 "$WEB_PID" >/dev/null 2>&1; then
    break
  fi

  if curl \
    --fail \
    --silent \
    --show-error \
    --max-time 1 \
    "http://127.0.0.1:$HEALTH_PORT/api/health" \
    >"$BUILD_ROOT/health.json" 2>/dev/null; then
    HEALTHY=true
    break
  fi

  sleep 0.1
done

if [[ "$HEALTHY" != true ]]; then
  echo "Source Assistant health-check log:" >&2
  sed -E 's/^(Access code:).*/\1 [redacted]/' "$HEALTH_LOG" >&2 || true
  fail "Published Source Assistant did not pass its loopback health check."
fi

HEALTH_VERSION="$(python3 - "$BUILD_ROOT/health.json" <<'PY'
import json
import sys
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    document = json.load(handle)
value = document.get("version")
if not isinstance(value, str) or not value:
    raise SystemExit("Source Assistant health response does not contain a version")
print(value)
PY
)" || fail "Published Source Assistant returned an invalid health version."
[[ "$HEALTH_VERSION" == "$WEB_VERSION" ]] ||
  fail "Published Source Assistant health reports '$HEALTH_VERSION', expected '$WEB_VERSION'."

kill "$WEB_PID" >/dev/null 2>&1 || true
wait "$WEB_PID" >/dev/null 2>&1 || true
WEB_PID=""

if [[ -n "$EXACT_RELEASE_VERSION" ]]; then
  RELEASE_VERSION="$EXACT_RELEASE_VERSION"
  RELEASE_CHANNEL="$EXACT_RELEASE_CHANNEL"
  if [[ -n "${SOURCE_DATE_EPOCH:-}" ]]; then
    [[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "SOURCE_DATE_EPOCH must be an integer Unix timestamp."
  else
    SOURCE_DATE_EPOCH="$(git -C "$SOURCE_REPO_ROOT" show -s --format=%ct HEAD)"
  fi
  PUBLISHED_AT_UTC="$(date -u -d "@$SOURCE_DATE_EPOCH" +%Y-%m-%dT%H:%M:%SZ)"
else
  PUBLISHED_AT_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  VERSION_TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
  BASE_VERSION="${PRODUCT_VERSION%%+*}"
  RELEASE_CHANNEL="dev"

  if [[ "$SOURCE_DIRTY" == true ]]; then
    RELEASE_VERSION="${BASE_VERSION}+g${SOURCE_SHORT_COMMIT}.dirty.${VERSION_TIMESTAMP}"
  else
    RELEASE_VERSION="${BASE_VERSION}+g${SOURCE_SHORT_COMMIT}.${VERSION_TIMESTAMP}"
  fi
fi

[[ "$RELEASE_VERSION" =~ ^[0-9A-Za-z][0-9A-Za-z._+-]{0,79}$ ]] ||
  fail "Generated release version is not accepted by install-current.sh: $RELEASE_VERSION"

rm -rf -- "$STAGE_ROOT" "$BACKUP_ROOT"
mkdir -p -- "$STAGE_ROOT/payload" "$STAGE_ROOT/scripts"
cp -a -- "$PUBLISH_DIR/." "$STAGE_ROOT/payload/"
cp -a -- "$INSTALL_SCRIPT" "$STAGE_ROOT/scripts/install-current.sh"
chmod 0755 "$STAGE_ROOT/scripts/install-current.sh"

printf '%s\n' "$RELEASE_VERSION" > "$STAGE_ROOT/VERSION"

python3 - \
  "$STAGE_ROOT/BUILD-INFO.json" \
  "$PRODUCT_VERSION" \
  "$WEB_VERSION" \
  "$RELEASE_VERSION" \
  "$PUBLISHED_AT_UTC" \
  "$RUNTIME" \
  "$CONFIGURATION" \
  "$SOURCE_COMMIT" \
  "$SOURCE_BRANCH" \
  "$SOURCE_DIRTY" \
  "$RELEASE_CHANNEL" \
  "$RELEASE_ELIGIBLE" <<'PY'
import json
import sys

(
    output_path,
    product_version,
    web_version,
    release_version,
    published_at_utc,
    runtime,
    configuration,
    source_commit,
    source_branch,
    source_dirty,
    release_channel,
    release_eligible,
) = sys.argv[1:]

document = {
    "schemaVersion": 1,
    "product": "MEM Migrate",
    "productVersion": product_version,
    "sourceAssistantVersion": web_version,
    "releaseVersion": release_version,
    "publishedAtUtc": published_at_utc,
    "runtime": runtime,
    "configuration": configuration,
    "selfContained": True,
    "sourceCommit": source_commit,
    "sourceBranch": source_branch,
    "sourceTreeDirty": source_dirty.lower() == "true",
    "releaseChannel": release_channel,
    "releaseEligible": release_eligible.lower() == "true",
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2)
    handle.write("\n")
PY

cat > "$STAGE_ROOT/install.sh" <<'INSTALL'
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ ${EUID:-$(id -u)} -eq 0 ]] ||
  fail "Run this installer with sudo."

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
VERSION="$(tr -d '[:space:]' < "$REPO_ROOT/VERSION")"

[[ -n "$VERSION" ]] || fail "VERSION is empty."

(
  cd "$REPO_ROOT"
  sha256sum -c SHA256SUMS
)

exec "$REPO_ROOT/scripts/install-current.sh" \
  --publish-dir "$REPO_ROOT/payload" \
  --version "$VERSION" \
  --metadata-dir "$REPO_ROOT"
INSTALL

cat > "$STAGE_ROOT/run-web.sh" <<'RUN_WEB'
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ ${EUID:-$(id -u)} -eq 0 ]] ||
  fail "Run the Source Assistant with sudo."

INSTALL_ROOT="/opt/mem/migrate/current"
STATE_ROOT="${MEM_MIGRATE_STATE_ROOT:-/var/lib/mem-migrate}"
LISTEN_ADDRESS="${MEM_MIGRATE_WEB_LISTEN_ADDRESS:-127.0.0.1}"
PORT="${MEM_MIGRATE_WEB_PORT:-7391}"
PID_FILE="${MEM_MIGRATE_WEB_PID_FILE:-/run/mem-migrate-web.pid}"

[[ -x "$INSTALL_ROOT/mem-migrate-web" ]] ||
  fail "Installed Source Assistant was not found. Run sudo ./install.sh first."

if [[ -f "$PID_FILE" ]]; then
  EXISTING_PID="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ "$EXISTING_PID" =~ ^[0-9]+$ ]] &&
     kill -0 "$EXISTING_PID" >/dev/null 2>&1; then
    fail "Source Assistant is already running with PID $EXISTING_PID."
  fi
  rm -f -- "$PID_FILE"
fi

install -d -m 0700 \
  "$STATE_ROOT" \
  "$STATE_ROOT/work" \
  "$STATE_ROOT/artifacts"

WEB_PID=""

cleanup() {
  local status=$?
  trap - EXIT INT TERM

  if [[ -n "$WEB_PID" ]] &&
     kill -0 "$WEB_PID" >/dev/null 2>&1; then
    kill -TERM "$WEB_PID" >/dev/null 2>&1 || true
    wait "$WEB_PID" >/dev/null 2>&1 || true
  fi

  rm -f -- "$PID_FILE"
  exit "$status"
}
trap cleanup EXIT INT TERM

"$INSTALL_ROOT/mem-migrate-web" \
  --listen-address "$LISTEN_ADDRESS" \
  --port "$PORT" \
  --workspace "$STATE_ROOT/work" \
  --artifacts "$STATE_ROOT/artifacts" &
WEB_PID=$!

printf '%s\n' "$WEB_PID" > "$PID_FILE"
chmod 0600 "$PID_FILE"

set +e
wait "$WEB_PID"
STATUS=$?
set -e

WEB_PID=""
rm -f -- "$PID_FILE"
trap - EXIT INT TERM
exit "$STATUS"
RUN_WEB

cat > "$STAGE_ROOT/stop-web.sh" <<'STOP_WEB'
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ ${EUID:-$(id -u)} -eq 0 ]] ||
  fail "Run this command with sudo."

PID_FILE="${MEM_MIGRATE_WEB_PID_FILE:-/run/mem-migrate-web.pid}"

if [[ ! -f "$PID_FILE" ]]; then
  echo "Source Assistant is not running."
  exit 0
fi

PID="$(cat "$PID_FILE" 2>/dev/null || true)"
[[ "$PID" =~ ^[0-9]+$ ]] || {
  rm -f -- "$PID_FILE"
  fail "Removed an invalid Source Assistant PID file."
}

if ! kill -0 "$PID" >/dev/null 2>&1; then
  rm -f -- "$PID_FILE"
  echo "Removed stale Source Assistant PID file."
  exit 0
fi

EXECUTABLE="$(readlink -f "/proc/$PID/exe" 2>/dev/null || true)"
[[ "$(basename -- "$EXECUTABLE")" == "mem-migrate-web" ]] ||
  fail "PID $PID is not mem-migrate-web; refusing to stop it."

kill -TERM "$PID"

for _ in $(seq 1 50); do
  if ! kill -0 "$PID" >/dev/null 2>&1; then
    rm -f -- "$PID_FILE"
    echo "Source Assistant stopped."
    exit 0
  fi
  sleep 0.1
done

fail "Source Assistant did not stop after SIGTERM. Inspect PID $PID manually."
STOP_WEB

cat > "$STAGE_ROOT/status.sh" <<'STATUS'
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
PID_FILE="${MEM_MIGRATE_WEB_PID_FILE:-/run/mem-migrate-web.pid}"
PORT="${MEM_MIGRATE_WEB_PORT:-7391}"

echo "Deployment repository version:"
cat "$REPO_ROOT/VERSION"

echo
echo "Installed release:"
readlink -f /opt/mem/migrate/current 2>/dev/null || echo "Not installed"

echo
echo "Installed CLI version:"
if [[ -x /opt/mem/migrate/current/mem-migrate ]]; then
  /opt/mem/migrate/current/mem-migrate version
else
  echo "Not installed"
fi

echo
echo "Source Assistant process:"
if [[ -f "$PID_FILE" ]]; then
  PID="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ "$PID" =~ ^[0-9]+$ ]] &&
     kill -0 "$PID" >/dev/null 2>&1; then
    echo "Running with PID $PID"
  else
    echo "Stale PID file: $PID_FILE"
  fi
else
  echo "Not running through run-web.sh"
fi

echo
echo "Loopback health:"
if command -v curl >/dev/null 2>&1 &&
   curl --fail --silent --max-time 2 \
     "http://127.0.0.1:$PORT/api/health" >/dev/null; then
  echo "Healthy on http://127.0.0.1:$PORT"
else
  echo "Not responding on http://127.0.0.1:$PORT"
fi
STATUS

cat > "$STAGE_ROOT/README.md" <<EOF
# MEM Migrate deployment repository

This repository contains the latest published, self-contained Linux x64 MEM
Migrate payload. The canonical source remains in:

\`$MIGRATE_ROOT\`

## Published release

- Release: \`$RELEASE_VERSION\`
- Product: \`$PRODUCT_VERSION\`
- Source commit: \`$SOURCE_COMMIT\`
- Source tree dirty: \`$SOURCE_DIRTY\`
- Published UTC: \`$PUBLISHED_AT_UTC\`

## One-line release bundle

The generated \`release-bundle/\` directory contains the standalone bootstrap,
\`release.json\`, the versioned Linux x64 archive, and its outer SHA-256 file.
It can be served from an HTTPS directory without requiring Git on the source
server.

Development or offline proof:

\`\`\`bash
sudo ./release-bundle/install-mem-migrate.sh \
  --bundle "\$(find release-bundle -maxdepth 1 -name '*.tar.gz' -print -quit)"
\`\`\`

Hosted proof shape:

\`\`\`bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- --release-base-url https://<release-host>
\`\`\`

## Development machine

The deployment content is generated by:

\`\`\`bash
cd "$SOURCE_REPO_ROOT"
./migrate/scripts/publish-to-deploy-repo.sh
\`\`\`

Review and publish it manually:

\`\`\`bash
cd "$DEPLOY_REPO"
git status --short
git diff --stat
cat VERSION
cat BUILD-INFO.json
sha256sum -c SHA256SUMS
cat release-bundle/release.json
(
  cd release-bundle
  sha256sum -c ./*.tar.gz.sha256
)

git add .
git commit -m "Publish MEM Migrate \$(cat VERSION)"
git push
\`\`\`

The publisher never commits or pushes automatically.

## MEM 0.1.0 test server

\`\`\`bash
cd ~/mem-migrate
git pull --ff-only
sha256sum -c SHA256SUMS
sudo ./install.sh
sudo ./run-web.sh
\`\`\`

The Source Assistant listens on loopback by default. From the operator desktop:

\`\`\`bash
ssh -L 7391:127.0.0.1:7391 <operator>@<source-host>
\`\`\`

Then open:

\`\`\`text
http://localhost:7391
\`\`\`

Use the access code printed in the source server terminal.

Useful commands:

\`\`\`bash
./status.sh
sudo ./stop-web.sh
\`\`\`

Do not run the application directly from \`payload/\`. Installation copies the
validated payload into a versioned release beneath \`/opt/mem/migrate/releases\`
and atomically updates \`/opt/mem/migrate/current\`.
EOF

cat > "$STAGE_ROOT/.gitignore" <<'GITIGNORE'
.publish-staging.*
.publish-backup.*
*.log
GITIGNORE

chmod 0755 \
  "$STAGE_ROOT/install.sh" \
  "$STAGE_ROOT/run-web.sh" \
  "$STAGE_ROOT/stop-web.sh" \
  "$STAGE_ROOT/status.sh"

(
  cd "$STAGE_ROOT"
  : > SHA256SUMS

  while IFS= read -r -d '' checksum_path; do
    sha256sum "$checksum_path" >> SHA256SUMS
  done < <(
    {
      find payload scripts -type f -print0
      printf '%s\0' \
        VERSION \
        BUILD-INFO.json \
        install.sh \
        run-web.sh \
        stop-web.sh \
        status.sh
    } | sort -z
  )

  sha256sum -c SHA256SUMS >/dev/null
)

"$BUNDLE_SCRIPT" \
  --deployment-root "$STAGE_ROOT" \
  --output-dir "$STAGE_ROOT/release-bundle" \
  --channel "$RELEASE_CHANNEL" \
  --runtime "$RUNTIME" \
  --installer-source "$BOOTSTRAP_INSTALLER"

mkdir -p -- "$BACKUP_ROOT"
UPDATE_STATE="partial"

for relative_path in "${MANAGED_PATHS[@]}"; do
  if [[ -e "$DEPLOY_REPO/$relative_path" ||
        -L "$DEPLOY_REPO/$relative_path" ]]; then
    mkdir -p -- "$(dirname -- "$BACKUP_ROOT/$relative_path")"
    mv -- "$DEPLOY_REPO/$relative_path" "$BACKUP_ROOT/$relative_path"
  fi
done

for relative_path in "${MANAGED_PATHS[@]}"; do
  [[ -e "$STAGE_ROOT/$relative_path" ||
     -L "$STAGE_ROOT/$relative_path" ]] ||
    fail "Generated deployment content is missing: $relative_path"

  mv -- "$STAGE_ROOT/$relative_path" "$DEPLOY_REPO/$relative_path"
done

UPDATE_STATE="complete"
rm -rf -- "$BACKUP_ROOT" "$STAGE_ROOT"

echo
echo "Deployment repository updated successfully."
echo
echo "Release:"
echo "  $RELEASE_VERSION"
echo
echo "Deployment repository:"
echo "  $DEPLOY_REPO"
echo
echo "Review:"
echo "  cd \"$DEPLOY_REPO\""
echo "  git status --short"
echo "  git diff --stat"
echo "  cat VERSION"
echo "  cat BUILD-INFO.json"
echo "  sha256sum -c SHA256SUMS"
echo "  cat release-bundle/release.json"
echo "  (cd release-bundle && sha256sum -c ./*.tar.gz.sha256)"
echo
echo "Required before Control Plane migration testing:"
printf '  sudo "%s" \\\n' "$DEV_TARGET_INSTALLER"
printf '    --payload "%s"\n' "$DEPLOY_REPO/payload"
echo
echo "This synchronises /opt/mem/migrate/dev with the same publication used by the source server."
echo "Restart the Control Plane development API after installing the worker."
echo
echo "The script did not install the target worker, commit, or push."
echo

git -C "$DEPLOY_REPO" status --short

trap - EXIT INT TERM
rm -rf -- "$BUILD_ROOT"
