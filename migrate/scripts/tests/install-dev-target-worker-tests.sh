#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
INSTALLER="$(cd -- "$SCRIPT_DIR/.." && pwd -P)/install-dev-target-worker.sh"
TEST_ROOT="$(mktemp -d /tmp/mem-migrate-dev-worker-tests.XXXXXX)"
PASS_COUNT=0
FAIL_COUNT=0

cleanup() {
  rm -rf -- "$TEST_ROOT"
}
trap cleanup EXIT

pass() {
  PASS_COUNT=$((PASS_COUNT + 1))
  echo "PASS: $1"
}

fail_test() {
  FAIL_COUNT=$((FAIL_COUNT + 1))
  echo "FAIL: $1" >&2
}

assert_contains() {
  local text="$1"
  local expected="$2"
  grep -Fq -- "$expected" <<<"$text"
}

make_host_tools() {
  local root="$1"
  local age_mode="${2:-ok}"
  local docker_mode="${3:-ok}"
  mkdir -p "$root/bin"

  cat > "$root/bin/age" <<EOF_AGE
#!/usr/bin/env bash
if [[ "$age_mode" == "ok" ]]; then
  echo "1.2.1"
  exit 0
fi
echo "age unavailable" >&2
exit 1
EOF_AGE

  cat > "$root/bin/docker" <<EOF_DOCKER
#!/usr/bin/env bash
if [[ "$docker_mode" == "ok" ]]; then
  echo "28.0.2"
  exit 0
fi
echo "daemon unavailable" >&2
exit 1
EOF_DOCKER

  chmod +x "$root/bin/age" "$root/bin/docker"
}

make_deployment() {
  local root="$1"
  local release="$2"
  local product="$3"
  local contract_mode="${4:-valid}"
  local source_commit="${5:-0123456789abcdef0123456789abcdef01234567}"

  mkdir -p "$root/payload"
  cat > "$root/payload/mem-migrate" <<EOF_CLI
#!/usr/bin/env bash
set -e
if [[ "\${1:-}" == "version" ]]; then
  echo "$product"
  exit 0
fi
if [[ "\${1:-}" == "worker" && "\${2:-}" == "convert" && "\${3:-}" == "--help" ]]; then
  echo "Conversion worker options:"
  echo "  mem-migrate worker convert --request <absolute-request.json>"
  if [[ "$contract_mode" == "valid" ]]; then
    echo "  mem-conversion-worker-request version 2"
  else
    echo "  mem-conversion-worker-request version 1"
  fi
  exit 0
fi
exit 2
EOF_CLI
  chmod +x "$root/payload/mem-migrate"
  printf 'sqlite\n' > "$root/payload/libe_sqlite3.so"
  printf 'web\n' > "$root/payload/mem-migrate-web"
  chmod +x "$root/payload/mem-migrate-web"
  mkdir -p "$root/payload/wwwroot"
  printf '<html></html>\n' > "$root/payload/wwwroot/index.html"

  printf '%s\n' "$release" > "$root/VERSION"
  python3 - "$root/BUILD-INFO.json" "$release" "$source_commit" <<'PY'
import json
import sys
path, release, source_commit = sys.argv[1:]
with open(path, "w", encoding="utf-8") as handle:
    json.dump({
        "schemaVersion": 1,
        "releaseVersion": release,
        "runtime": "linux-x64",
        "sourceCommit": source_commit,
        "sourceTreeDirty": False,
    }, handle)
    handle.write("\n")
PY
}

run_installer() {
  local tools_root="$1"
  shift
  PATH="$tools_root/bin:$PATH" "$INSTALLER" "$@"
}

# 1. Help contract.
if output="$($INSTALLER --help 2>&1)" &&
   assert_contains "$output" "Control Plane development" &&
   assert_contains "$output" "/opt/mem/migrate/dev-releases/<version>"; then
  pass "help describes the target development worker role"
else
  fail_test "help describes the target development worker role"
fi

# Shared valid fixture.
TOOLS="$TEST_ROOT/tools"
DEPLOY="$TEST_ROOT/deploy"
INSTALL_ROOT="$TEST_ROOT/install"
make_host_tools "$TOOLS"
make_deployment "$DEPLOY" "0.2.0-alpha.1+gabc.20260731T000000Z" "0.2.0-alpha.1+abc"

# 2. Dry-run is non-modifying.
if output="$(run_installer "$TOOLS" --payload "$DEPLOY/payload" --install-root "$INSTALL_ROOT" --dry-run 2>&1)" &&
   assert_contains "$output" "Dry-run complete" &&
   [[ ! -e "$INSTALL_ROOT" ]]; then
  pass "dry-run validates without modifying the installation root"
else
  fail_test "dry-run validates without modifying the installation root"
fi

# 3. Install creates versioned release and active symlink.
if output="$(run_installer "$TOOLS" --payload "$DEPLOY/payload" --install-root "$INSTALL_ROOT" 2>&1)" &&
   [[ -L "$INSTALL_ROOT/dev" ]] &&
   [[ -x "$INSTALL_ROOT/dev/mem-migrate" ]] &&
   [[ -f "$INSTALL_ROOT/dev/libe_sqlite3.so" ]] &&
   [[ -f "$INSTALL_ROOT/dev/TARGET-WORKER-INFO.json" ]] &&
   assert_contains "$output" "installed successfully"; then
  pass "valid payload is installed and activated through the stable dev path"
else
  fail_test "valid payload is installed and activated through the stable dev path"
fi

# 4. Same release is idempotent.
before_target="$(readlink "$INSTALL_ROOT/dev")"
if output="$(run_installer "$TOOLS" --payload "$DEPLOY/payload" --install-root "$INSTALL_ROOT" 2>&1)" &&
   [[ "$(readlink "$INSTALL_ROOT/dev")" == "$before_target" ]] &&
   assert_contains "$output" "Release directory already exists"; then
  pass "same release rerun validates and preserves the active target"
else
  fail_test "same release rerun validates and preserves the active target"
fi

# 5. Upgrade installs a second release and flips the active link.
DEPLOY2="$TEST_ROOT/deploy2"
make_deployment "$DEPLOY2" "0.2.0-alpha.1+gdef.20260731T010000Z" "0.2.0-alpha.1+def" "valid" "fedcba9876543210fedcba9876543210fedcba98"
if output="$(run_installer "$TOOLS" --payload "$DEPLOY2/payload" --install-root "$INSTALL_ROOT" 2>&1)" &&
   [[ "$(readlink "$INSTALL_ROOT/dev")" == "dev-releases/0.2.0-alpha.1+gdef.20260731T010000Z" ]] &&
   [[ -d "$INSTALL_ROOT/dev-releases/0.2.0-alpha.1+gabc.20260731T000000Z" ]] &&
   [[ "$($INSTALL_ROOT/dev/mem-migrate version)" == "0.2.0-alpha.1+def" ]]; then
  pass "new publication installs side-by-side and atomically becomes active"
else
  fail_test "new publication installs side-by-side and atomically becomes active"
fi

# 6. Legacy directory is preserved during first versioned activation.
LEGACY_ROOT="$TEST_ROOT/legacy-install"
mkdir -p "$LEGACY_ROOT/dev"
printf 'legacy\n' > "$LEGACY_ROOT/dev/marker.txt"
if output="$(run_installer "$TOOLS" --payload "$DEPLOY/payload" --install-root "$LEGACY_ROOT" 2>&1)" &&
   [[ -L "$LEGACY_ROOT/dev" ]] &&
   find "$LEGACY_ROOT" -maxdepth 1 -type d -name 'dev-legacy-*' -exec test -f '{}/marker.txt' ';' -quit &&
   assert_contains "$output" "Preserving the legacy unversioned"; then
  pass "legacy unversioned worker is preserved when versioned activation begins"
else
  fail_test "legacy unversioned worker is preserved when versioned activation begins"
fi

# 7. Missing SQLite companion fails before mutation.
BAD_DEPLOY="$TEST_ROOT/bad-deploy"
make_deployment "$BAD_DEPLOY" "0.2.0-alpha.1+gbad.20260731T020000Z" "0.2.0-alpha.1+bad"
rm -f "$BAD_DEPLOY/payload/libe_sqlite3.so"
BAD_ROOT="$TEST_ROOT/bad-install"
if ! output="$(run_installer "$TOOLS" --payload "$BAD_DEPLOY/payload" --install-root "$BAD_ROOT" 2>&1)" &&
   assert_contains "$output" "missing libe_sqlite3.so" &&
   [[ ! -e "$BAD_ROOT" ]]; then
  pass "missing SQLite companion fails closed before installation"
else
  fail_test "missing SQLite companion fails closed before installation"
fi

# 8. Stale worker protocol fails before mutation.
OLD_DEPLOY="$TEST_ROOT/old-deploy"
make_deployment "$OLD_DEPLOY" "0.2.0-alpha.1+gold.20260731T030000Z" "0.2.0-alpha.1+old" "old"
OLD_ROOT="$TEST_ROOT/old-install"
if ! output="$(run_installer "$TOOLS" --payload "$OLD_DEPLOY/payload" --install-root "$OLD_ROOT" 2>&1)" &&
   assert_contains "$output" "schema version 2" &&
   [[ ! -e "$OLD_ROOT" ]]; then
  pass "stale conversion-worker protocol is rejected before activation"
else
  fail_test "stale conversion-worker protocol is rejected before activation"
fi

# 9. age failure blocks target-worker installation.
AGE_BAD_TOOLS="$TEST_ROOT/age-bad-tools"
make_host_tools "$AGE_BAD_TOOLS" fail ok
AGE_BAD_ROOT="$TEST_ROOT/age-bad-install"
if ! output="$(run_installer "$AGE_BAD_TOOLS" --payload "$DEPLOY/payload" --install-root "$AGE_BAD_ROOT" 2>&1)" &&
   assert_contains "$output" "age unavailable" &&
   [[ ! -e "$AGE_BAD_ROOT" ]]; then
  pass "unusable target age command blocks installation"
else
  fail_test "unusable target age command blocks installation"
fi

# 10. Docker daemon failure blocks target-worker installation.
DOCKER_BAD_TOOLS="$TEST_ROOT/docker-bad-tools"
make_host_tools "$DOCKER_BAD_TOOLS" ok fail
DOCKER_BAD_ROOT="$TEST_ROOT/docker-bad-install"
if ! output="$(run_installer "$DOCKER_BAD_TOOLS" --payload "$DEPLOY/payload" --install-root "$DOCKER_BAD_ROOT" 2>&1)" &&
   assert_contains "$output" "Docker is installed but the daemon is not reachable" &&
   [[ ! -e "$DOCKER_BAD_ROOT" ]]; then
  pass "unreachable Docker daemon blocks target-worker installation"
else
  fail_test "unreachable Docker daemon blocks target-worker installation"
fi

echo
echo "Target development worker tests: $PASS_COUNT passed, $FAIL_COUNT failed"
[[ "$FAIL_COUNT" -eq 0 ]]
