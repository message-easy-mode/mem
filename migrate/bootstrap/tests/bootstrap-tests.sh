#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BOOTSTRAP_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
INSTALLER="$BOOTSTRAP_DIR/install.sh"
SUITE_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-bootstrap-tests.XXXXXX")"
PASSED=0
FAILED=0

cleanup() {
  rm -rf -- "$SUITE_ROOT"
}
trap cleanup EXIT INT TERM

pass() {
  PASSED=$((PASSED + 1))
  printf 'PASS: %s\n' "$1"
}

fail_test() {
  FAILED=$((FAILED + 1))
  printf 'FAIL: %s\n' "$1" >&2
  if [[ -n "${2:-}" ]]; then
    printf '%s\n' "$2" >&2
  fi
}

assert_contains() {
  local text="$1"
  local expected="$2"
  [[ "$text" == *"$expected"* ]]
}

assert_not_contains() {
  local text="$1"
  local unexpected="$2"
  [[ "$text" != *"$unexpected"* ]]
}

refresh_fixture_bundle_metadata() {
  local root="$1"
  local archive="$root/release-bundle/mem-migrate-0.2.0+fixture-linux-x64.tar.gz"
  local sha

  sha="$(sha256sum "$archive" | awk '{print $1}')"
  printf '%s  %s\n' "$sha" "$(basename -- "$archive")" > "$archive.sha256"

  cat > "$root/release-bundle/release.json" <<JSON
{
  "schemaVersion": 1,
  "channel": "dev",
  "releaseVersion": "0.2.0+fixture",
  "runtime": "linux-x64",
  "archiveFileName": "$(basename -- "$archive")",
  "archiveSha256": "$sha",
  "publishedAtUtc": "2026-07-31T00:00:00Z"
}
JSON
}

create_fixture_bundle() {
  local root="$1"
  local release_root="$root/bundle-source/mem-migrate-release"
  local archive="$root/release-bundle/mem-migrate-0.2.0+fixture-linux-x64.tar.gz"

  mkdir -p \
    "$release_root/payload/wwwroot" \
    "$release_root/scripts" \
    "$root/release-bundle"

  printf '0.2.0+fixture\n' > "$release_root/VERSION"
  cat > "$release_root/BUILD-INFO.json" <<'JSON'
{
  "schemaVersion": 1,
  "releaseVersion": "0.2.0+fixture",
  "publishedAtUtc": "2026-07-31T00:00:00Z",
  "runtime": "linux-x64"
}
JSON

  cat > "$release_root/payload/mem-migrate" <<'SH'
#!/usr/bin/env bash
printf '0.2.0.0 fixture\n'
SH

  cat > "$release_root/payload/mem-migrate-web" <<'SH'
#!/usr/bin/env bash
printf '0.2.0.0 fixture\n'
SH

  printf 'fixture sqlite\n' > "$release_root/payload/libe_sqlite3.so"
  printf '<!doctype html><html><body>fixture</body></html>\n' > "$release_root/payload/wwwroot/index.html"

  cat > "$release_root/scripts/install-current.sh" <<'SH'
#!/usr/bin/env bash
exit 0
SH

  cat > "$release_root/install.sh" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
INSTALL_ROOT="$MEM_MIGRATE_BOOTSTRAP_TEST_INSTALL_ROOT"
CURRENT_LINK="$MEM_MIGRATE_BOOTSTRAP_CURRENT_LINK"
COUNT_FILE="$MEM_MIGRATE_BOOTSTRAP_TEST_INSTALL_COUNT_FILE"
RELEASE_DIR="$INSTALL_ROOT/releases/$VERSION"
mkdir -p "$RELEASE_DIR"
cp -a "$ROOT/payload/." "$RELEASE_DIR/"
cp -a "$ROOT/VERSION" "$RELEASE_DIR/VERSION"
cp -a "$ROOT/BUILD-INFO.json" "$RELEASE_DIR/BUILD-INFO.json"
ln -sfn "$RELEASE_DIR" "$CURRENT_LINK"
count=0
[[ ! -f "$COUNT_FILE" ]] || count="$(cat "$COUNT_FILE")"
printf '%s\n' "$((count + 1))" > "$COUNT_FILE"
printf 'Fixture release installed: %s\n' "$VERSION"
SH

  for helper in run-web.sh stop-web.sh status.sh; do
    cat > "$release_root/$helper" <<'SH'
#!/usr/bin/env bash
exit 0
SH
  done

  chmod 0755 \
    "$release_root/payload/mem-migrate" \
    "$release_root/payload/mem-migrate-web" \
    "$release_root/scripts/install-current.sh" \
    "$release_root/install.sh" \
    "$release_root/run-web.sh" \
    "$release_root/stop-web.sh" \
    "$release_root/status.sh"

  (
    cd "$release_root"
    : > SHA256SUMS
    while IFS= read -r -d '' file; do
      sha256sum "$file" >> SHA256SUMS
    done < <(find . -type f ! -name SHA256SUMS -print0 | sort -z)
  )

  tar -C "$root/bundle-source" -czf "$archive" mem-migrate-release
  refresh_fixture_bundle_metadata "$root"
}

new_fixture() {
  local name="$1"
  local root="$SUITE_ROOT/$name"
  mkdir -p "$root/bin" "$root/state" "$root/disk" "$root/current" "$root/install-root"

  cat > "$root/os-release" <<'OS'
ID=ubuntu
VERSION_ID="24.04"
PRETTY_NAME="Ubuntu 24.04 LTS"
OS

  printf 'present\n' > "$root/state/age"
  printf '0\n' > "$root/state/install-count"
  : > "$root/state/commands.log"
  printf 'fixture certificate\n' > "$root/ca-certificates.crt"

  cat > "$root/bin/docker" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
{ printf 'docker'; printf ' %q' "$@"; printf '\n'; } >> "$MEM_MIGRATE_TEST_COMMAND_LOG"
case "${1:-}" in
  --version)
    printf 'Docker version 28.0.0, build fixture\n'
    ;;
  info)
    if [[ "${MEM_MIGRATE_TEST_DOCKER_DAEMON:-ok}" == "unavailable" ]]; then
      exit 1
    fi
    printf 'fixture docker info\n'
    ;;
  *)
    exit 2
    ;;
esac
SH

  cat > "$root/bin/age" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "$(cat "$MEM_MIGRATE_BOOTSTRAP_TEST_AGE_STATE_FILE")" != "present" ]]; then
  exit 127
fi
if [[ "${MEM_MIGRATE_TEST_AGE_UNUSABLE:-0}" == "1" ]]; then
  printf 'fixture age failure\n' >&2
  exit 1
fi
printf 'age v1.2.0 fixture\n'
SH

  cat > "$root/bin/apt-cache" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
{ printf 'apt-cache'; printf ' %q' "$@"; printf '\n'; } >> "$MEM_MIGRATE_TEST_COMMAND_LOG"
if [[ "${1:-}" == "show" && "${2:-}" == "age" ]]; then
  if [[ "${MEM_MIGRATE_TEST_AGE_PACKAGE_AVAILABLE:-1}" == "1" || -f "$MEM_MIGRATE_TEST_UNIVERSE_MARKER" ]]; then
    printf 'Package: age\n'
    exit 0
  fi
  exit 1
fi
exit 0
SH

  cat > "$root/bin/add-apt-repository" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
{ printf 'add-apt-repository'; printf ' %q' "$@"; printf '\n'; } >> "$MEM_MIGRATE_TEST_COMMAND_LOG"
touch "$MEM_MIGRATE_TEST_UNIVERSE_MARKER"
SH

  cat > "$root/bin/apt-get" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
{ printf 'apt-get'; printf ' %q' "$@"; printf '\n'; } >> "$MEM_MIGRATE_TEST_COMMAND_LOG"
operation="${1:-}"
contains_age=false
for argument in "$@"; do
  if [[ "$argument" == "age" ]]; then
    contains_age=true
  fi
done
if [[ "$operation" == "install" && "$contains_age" == "true" ]]; then
  printf 'present\n' > "$MEM_MIGRATE_BOOTSTRAP_TEST_AGE_STATE_FILE"
fi
exit 0
SH

  cat > "$root/bin/curl" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
{ printf 'curl'; printf ' %q' "$@"; printf '\n'; } >> "$MEM_MIGRATE_TEST_COMMAND_LOG"
output=""
url=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      output="$2"
      shift 2
      ;;
    http://*|https://*)
      url="$1"
      shift
      ;;
    *)
      shift
      ;;
  esac
done
[[ -n "$output" && -n "$url" ]]
name="${url##*/}"
cp "$MEM_MIGRATE_BOOTSTRAP_TEST_REMOTE_ROOT/$name" "$output"
SH

  chmod 0755 "$root/bin/"*
  create_fixture_bundle "$root"
  printf '%s\n' "$root"
}

fixture_archive() {
  printf '%s/release-bundle/mem-migrate-0.2.0+fixture-linux-x64.tar.gz\n' "$1"
}

run_fixture() {
  local root="$1"
  shift

  env \
    PATH="$root/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
    MEM_MIGRATE_BOOTSTRAP_TEST_MODE=1 \
    MEM_MIGRATE_BOOTSTRAP_EUID_OVERRIDE="${MEM_MIGRATE_TEST_EUID:-0}" \
    MEM_MIGRATE_BOOTSTRAP_OS_RELEASE_FILE="$root/os-release" \
    MEM_MIGRATE_BOOTSTRAP_ARCH_OVERRIDE="${MEM_MIGRATE_TEST_ARCH:-x86_64}" \
    MEM_MIGRATE_BOOTSTRAP_KERNEL_OVERRIDE="${MEM_MIGRATE_TEST_KERNEL:-Linux}" \
    MEM_MIGRATE_BOOTSTRAP_DISK_PROBE_PATH="$root/disk" \
    MEM_MIGRATE_BOOTSTRAP_CA_CERT_FILE="$root/ca-certificates.crt" \
    MEM_MIGRATE_BOOTSTRAP_CURRENT_LINK="$root/current-link" \
    MEM_MIGRATE_BOOTSTRAP_TEST_INSTALL_ROOT="$root/install-root" \
    MEM_MIGRATE_BOOTSTRAP_TEST_INSTALL_COUNT_FILE="$root/state/install-count" \
    MEM_MIGRATE_BOOTSTRAP_TEST_AGE_STATE_FILE="$root/state/age" \
    MEM_MIGRATE_BOOTSTRAP_TEST_REMOTE_ROOT="$root/release-bundle" \
    MEM_MIGRATE_TEST_COMMAND_LOG="$root/state/commands.log" \
    MEM_MIGRATE_TEST_UNIVERSE_MARKER="$root/state/universe-enabled" \
    MEM_MIGRATE_TEST_DOCKER_DAEMON="${MEM_MIGRATE_TEST_DOCKER_DAEMON:-ok}" \
    MEM_MIGRATE_TEST_AGE_PACKAGE_AVAILABLE="${MEM_MIGRATE_TEST_AGE_PACKAGE_AVAILABLE:-1}" \
    MEM_MIGRATE_TEST_AGE_UNUSABLE="${MEM_MIGRATE_TEST_AGE_UNUSABLE:-0}" \
    /bin/bash "$INSTALLER" "$@"
}

run_local_fixture() {
  local root="$1"
  shift
  run_fixture "$root" --bundle "$(fixture_archive "$root")" "$@"
}

run_test_help() {
  local output
  if output="$(/bin/bash "$INSTALLER" --help 2>&1)" &&
     assert_contains "$output" "MM-BOOT-01B" &&
     assert_contains "$output" "--bundle" &&
     assert_contains "$output" "--release-base-url"; then
    pass "help describes verified local and HTTPS release installation"
  else
    fail_test "help describes verified local and HTTPS release installation" "$output"
  fi
}

run_test_existing_age() {
  local root output log
  root="$(new_fixture existing-age)"

  if output="$(run_local_fixture "$root" --dry-run 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$output" "Existing age encryption command is usable" &&
       assert_contains "$output" "Docker version 28.0.0" &&
       assert_contains "$output" "Resolved release" &&
       assert_not_contains "$log" "apt-get"; then
      pass "working age is preserved while the release plan is resolved"
      return
    fi
  fi

  fail_test "working age is preserved while the release plan is resolved" "${output:-no output}"
}

run_test_missing_age_dry_run() {
  local root output log
  root="$(new_fixture missing-age-dry-run)"
  printf 'missing\n' > "$root/state/age"

  if output="$(run_local_fixture "$root" --dry-run 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$output" "- age" &&
       assert_contains "$output" "would install the listed packages" &&
       assert_contains "$output" "No release payload was downloaded" &&
       assert_not_contains "$log" "apt-get"; then
      pass "missing age and release install are planned without modification in dry-run"
      return
    fi
  fi

  fail_test "missing age and release install are planned without modification in dry-run" "${output:-no output}"
}

run_test_missing_age_install() {
  local root output log
  root="$(new_fixture missing-age-install)"
  printf 'missing\n' > "$root/state/age"

  if output="$(run_local_fixture "$root" --yes 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$output" "MEM Migrate installed successfully" &&
       assert_contains "$output" "age v1.2.0 fixture" &&
       assert_contains "$log" "apt-get update" &&
       assert_contains "$log" "apt-get install" &&
       [[ "$(readlink -f "$root/current-link")" == "$root/install-root/releases/0.2.0+fixture" ]]; then
      pass "missing age is installed before the verified release is activated"
      return
    fi
  fi

  fail_test "missing age is installed before the verified release is activated" "${output:-no output}"
}

run_test_universe_enablement() {
  local root output log
  root="$(new_fixture universe-enablement)"
  printf 'missing\n' > "$root/state/age"

  if output="$(MEM_MIGRATE_TEST_AGE_PACKAGE_AVAILABLE=0 run_local_fixture "$root" --yes 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$output" "enabling the Ubuntu universe component" &&
       assert_contains "$log" "add-apt-repository -y universe" &&
       assert_contains "$output" "MEM Migrate installed successfully"; then
      pass "Ubuntu universe is enabled only when age is unavailable"
      return
    fi
  fi

  fail_test "Ubuntu universe is enabled only when age is unavailable" "${output:-no output}"
}

run_test_skip_age_install() {
  local root output status
  root="$(new_fixture skip-age)"
  printf 'missing\n' > "$root/state/age"

  set +e
  output="$(run_local_fixture "$root" --skip-age-install 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "--skip-age-install was supplied"; then
    pass "skip-age-install fails closed when age is missing"
  else
    fail_test "skip-age-install fails closed when age is missing" "$output"
  fi
}

run_test_docker_missing() {
  local root output status
  root="$(new_fixture docker-missing)"

  set +e
  output="$(MEM_MIGRATE_BOOTSTRAP_FORCE_DOCKER_MISSING=1 run_local_fixture "$root" --dry-run 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "will not install or modify Docker"; then
    pass "missing Docker fails closed without release mutation"
  else
    fail_test "missing Docker fails closed without release mutation" "$output"
  fi
}

run_test_docker_daemon_unavailable() {
  local root output status
  root="$(new_fixture docker-daemon)"

  set +e
  output="$(MEM_MIGRATE_TEST_DOCKER_DAEMON=unavailable run_local_fixture "$root" --dry-run 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "daemon is not reachable"; then
    pass "unreachable Docker daemon fails closed"
  else
    fail_test "unreachable Docker daemon fails closed" "$output"
  fi
}

run_test_unsupported_os() {
  local root output status
  root="$(new_fixture unsupported-os)"
  cat > "$root/os-release" <<'OS'
ID=debian
VERSION_ID="12"
PRETTY_NAME="Debian GNU/Linux 12"
OS

  set +e
  output="$(run_local_fixture "$root" --dry-run 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "Unsupported distribution" &&
     [[ ! -s "$root/state/commands.log" ]]; then
    pass "unsupported distribution exits before dependency or release changes"
  else
    fail_test "unsupported distribution exits before dependency or release changes" "$output"
  fi
}

run_test_unsupported_architecture() {
  local root output status
  root="$(new_fixture unsupported-arch)"

  set +e
  output="$(MEM_MIGRATE_TEST_ARCH=aarch64 run_local_fixture "$root" --dry-run 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "Unsupported architecture" &&
     [[ ! -s "$root/state/commands.log" ]]; then
    pass "unsupported architecture exits before dependency or release changes"
  else
    fail_test "unsupported architecture exits before dependency or release changes" "$output"
  fi
}

run_test_broken_age() {
  local root output status
  root="$(new_fixture broken-age)"

  set +e
  output="$(MEM_MIGRATE_TEST_AGE_UNUSABLE=1 run_local_fixture "$root" --dry-run 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "age command exists" &&
     assert_contains "$output" "not usable"; then
    pass "an unusable age command fails clearly"
  else
    fail_test "an unusable age command fails clearly" "$output"
  fi
}

run_test_non_root_apply() {
  local root output status
  root="$(new_fixture non-root)"

  set +e
  output="$(MEM_MIGRATE_TEST_EUID=1000 run_local_fixture "$root" --yes 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "Run the bootstrap as root"; then
    pass "a modifying run requires root"
  else
    fail_test "a modifying run requires root" "$output"
  fi
}

run_test_local_bundle_install() {
  local root output
  root="$(new_fixture local-bundle-install)"

  if output="$(run_local_fixture "$root" --version 0.2.0+fixture --channel dev 2>&1)" &&
     assert_contains "$output" "Release archive SHA-256 verified" &&
     assert_contains "$output" "inner payload checksums verified" &&
     [[ "$(cat "$root/state/install-count")" == "1" ]] &&
     [[ "$(cat "$root/current-link/VERSION")" == "0.2.0+fixture" ]] &&
     grep -Fq '"releaseVersion": "0.2.0+fixture"' "$root/current-link/BUILD-INFO.json"; then
    pass "local bundle is verified and installed without Git"
  else
    fail_test "local bundle is verified and installed without Git" "$output"
  fi
}

run_test_published_local_manifest_name() {
  local root output archive versioned_manifest
  root="$(new_fixture published-local-manifest)"
  archive="$(fixture_archive "$root")"
  versioned_manifest="$root/release-bundle/mem-migrate-0.2.0+fixture-release.json"
  mv -- "$root/release-bundle/release.json" "$versioned_manifest"

  if output="$(run_fixture "$root" --bundle "$archive" --version 0.2.0+fixture --channel dev 2>&1)" &&
     assert_contains "$output" "MEM Migrate installed successfully" &&
     [[ -f "$root/current-link/VERSION" ]] &&
     [[ -f "$root/current-link/BUILD-INFO.json" ]]; then
    pass "local bundle consumes the exact published versioned manifest filename"
  else
    fail_test "local bundle consumes the exact published versioned manifest filename" "$output"
  fi
}

run_test_remote_release_install() {
  local root output log
  root="$(new_fixture remote-release-install)"

  if output="$(run_fixture "$root" --release-base-url https://fixture.example --channel dev 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$log" "https://fixture.example/release.json" &&
       assert_contains "$log" "mem-migrate-0.2.0+fixture-linux-x64.tar.gz" &&
       assert_contains "$log" ".tar.gz.sha256" &&
       assert_contains "$output" "MEM Migrate installed successfully"; then
      pass "HTTPS release path resolves, downloads, verifies, and installs without Git"
      return
    fi
  fi

  fail_test "HTTPS release path resolves, downloads, verifies, and installs without Git" "$output"
}

run_test_prerelease_channel_install() {
  local root output manifest
  root="$(new_fixture prerelease-channel-install)"
  manifest="$root/release-bundle/release.json"
  sed -i 's/"channel": "dev"/"channel": "prerelease"/' "$manifest"

  if output="$(run_local_fixture "$root" --channel prerelease 2>&1)" &&
     assert_contains "$output" "Channel:                prerelease" &&
     assert_contains "$output" "MEM Migrate installed successfully" &&
     [[ "$(cat "$root/state/install-count")" == "1" ]]; then
    pass "prerelease release manifests are accepted and can be explicitly pinned"
  else
    fail_test "prerelease release manifests are accepted and can be explicitly pinned" "$output"
  fi
}

run_test_remote_dry_run_manifest_only() {
  local root output log
  root="$(new_fixture remote-dry-run)"

  if output="$(run_fixture "$root" --release-base-url https://fixture.example --dry-run 2>&1)"; then
    log="$(cat "$root/state/commands.log")"
    if assert_contains "$log" "https://fixture.example/release.json" &&
       assert_not_contains "$log" ".tar.gz " &&
       assert_not_contains "$log" ".tar.gz.sha256" &&
       assert_contains "$output" "No release payload was downloaded"; then
      pass "remote dry-run fetches only the manifest and performs no install"
      return
    fi
  fi

  fail_test "remote dry-run fetches only the manifest and performs no install" "$output"
}

run_test_checksum_mismatch() {
  local root output status archive
  root="$(new_fixture checksum-mismatch)"
  archive="$(fixture_archive "$root")"
  printf 'corruption\n' >> "$archive"

  set +e
  output="$(run_local_fixture "$root" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "SHA-256 verification failed" &&
     [[ "$(cat "$root/state/install-count")" == "0" ]]; then
    pass "archive checksum mismatch fails before installation"
  else
    fail_test "archive checksum mismatch fails before installation" "$output"
  fi
}

run_test_manifest_runtime_mismatch() {
  local root output status manifest
  root="$(new_fixture runtime-mismatch)"
  manifest="$root/release-bundle/release.json"
  sed -i 's/"runtime": "linux-x64"/"runtime": "linux-arm64"/' "$manifest"

  set +e
  output="$(run_local_fixture "$root" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "Unsupported release runtime"; then
    pass "unsupported release runtime is rejected from the manifest"
  else
    fail_test "unsupported release runtime is rejected from the manifest" "$output"
  fi
}

run_test_pinned_version_mismatch() {
  local root output status
  root="$(new_fixture pinned-version-mismatch)"

  set +e
  output="$(run_local_fixture "$root" --version 0.2.0+other 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "not requested version"; then
    pass "version-pinned install never substitutes another release"
  else
    fail_test "version-pinned install never substitutes another release" "$output"
  fi
}

run_test_unsafe_archive_link() {
  local root output status source_root archive
  root="$(new_fixture unsafe-archive-link)"
  source_root="$root/bundle-source/mem-migrate-release"
  archive="$(fixture_archive "$root")"
  ln -s /etc/passwd "$source_root/unsafe-link"
  tar -C "$root/bundle-source" -czf "$archive" mem-migrate-release
  refresh_fixture_bundle_metadata "$root"

  set +e
  output="$(run_local_fixture "$root" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 ]] && assert_contains "$output" "unsafe non-file entry type" &&
     [[ "$(cat "$root/state/install-count")" == "0" ]]; then
    pass "unsafe archive links are rejected before extraction and installation"
  else
    fail_test "unsafe archive links are rejected before extraction and installation" "$output"
  fi
}

run_test_idempotent_rerun() {
  local root first second
  root="$(new_fixture idempotent-rerun)"

  if first="$(run_local_fixture "$root" 2>&1)" &&
     second="$(run_local_fixture "$root" 2>&1)" &&
     assert_contains "$second" "already the current release" &&
     [[ "$(cat "$root/state/install-count")" == "1" ]]; then
    pass "same-release rerun is idempotent and preserves the active release"
  else
    fail_test "same-release rerun is idempotent and preserves the active release" "${second:-${first:-no output}}"
  fi
}

main() {
  [[ -x "$INSTALLER" ]] || {
    printf 'Installer is missing or not executable: %s\n' "$INSTALLER" >&2
    exit 1
  }

  run_test_help
  run_test_existing_age
  run_test_missing_age_dry_run
  run_test_missing_age_install
  run_test_universe_enablement
  run_test_skip_age_install
  run_test_docker_missing
  run_test_docker_daemon_unavailable
  run_test_unsupported_os
  run_test_unsupported_architecture
  run_test_broken_age
  run_test_non_root_apply
  run_test_local_bundle_install
  run_test_published_local_manifest_name
  run_test_remote_release_install
  run_test_prerelease_channel_install
  run_test_remote_dry_run_manifest_only
  run_test_checksum_mismatch
  run_test_manifest_runtime_mismatch
  run_test_pinned_version_mismatch
  run_test_unsafe_archive_link
  run_test_idempotent_rerun

  printf '\nBootstrap tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
  [[ $FAILED -eq 0 ]]
}

main "$@"
