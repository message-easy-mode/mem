#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MIGRATE_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
BUNDLE_SCRIPT="$MIGRATE_ROOT/scripts/create-release-bundle.sh"
BOOTSTRAP_INSTALLER="$MIGRATE_ROOT/bootstrap/install.sh"
PUBLISH_CURRENT="$MIGRATE_ROOT/scripts/publish-current.sh"
PUBLISH_TO_DEPLOY_REPO="$MIGRATE_ROOT/scripts/publish-to-deploy-repo.sh"
INSTALL_CURRENT="$MIGRATE_ROOT/scripts/install-current.sh"
SOURCE_ASSISTANT_PROGRAM="$MIGRATE_ROOT/src/Mem.Migrate.Web/Program.cs"
SOURCE_ASSISTANT_COMMAND_LINE="$MIGRATE_ROOT/src/Mem.Migrate.Web/Hosting/SourceAssistantCommandLine.cs"
SOURCE_ASSISTANT_APP="$MIGRATE_ROOT/src/Mem.Migrate.Web/ClientApp/src/App.tsx"
SOURCE_ASSISTANT_VITE="$MIGRATE_ROOT/src/Mem.Migrate.Web/ClientApp/vite.config.ts"
SUITE_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-release-bundle-tests.XXXXXX")"
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
  [[ -z "${2:-}" ]] || printf '%s\n' "$2" >&2
}

new_deployment_fixture() {
  local name="$1"
  local root="$SUITE_ROOT/$name/deployment"
  mkdir -p "$root/payload/wwwroot" "$root/scripts"

  printf '0.2.0+fixture.20260731T000000Z\n' > "$root/VERSION"
  cat > "$root/BUILD-INFO.json" <<'JSON'
{
  "schemaVersion": 1,
  "product": "MEM Migrate",
  "productVersion": "0.2.0",
  "sourceAssistantVersion": "0.2.0+fixture.20260731T000000Z",
  "releaseVersion": "0.2.0+fixture.20260731T000000Z",
  "publishedAtUtc": "2026-07-31T00:00:00Z",
  "runtime": "linux-x64",
  "configuration": "Release",
  "selfContained": true,
  "sourceCommit": "0123456789abcdef0123456789abcdef01234567",
  "sourceBranch": "main",
  "sourceTreeDirty": false,
  "releaseChannel": "dev",
  "releaseEligible": true
}
JSON

  for executable in mem-migrate mem-migrate-web; do
    cat > "$root/payload/$executable" <<'SH'
#!/usr/bin/env bash
exit 0
SH
    chmod 0755 "$root/payload/$executable"
  done

  printf 'sqlite fixture\n' > "$root/payload/libe_sqlite3.so"
  printf '<!doctype html><html><body>fixture</body></html>\n' > "$root/payload/wwwroot/index.html"

  for helper in install.sh run-web.sh stop-web.sh status.sh; do
    cat > "$root/$helper" <<'SH'
#!/usr/bin/env bash
exit 0
SH
    chmod 0755 "$root/$helper"
  done

  cat > "$root/scripts/install-current.sh" <<'SH'
#!/usr/bin/env bash
exit 0
SH
  chmod 0755 "$root/scripts/install-current.sh"

  (
    cd "$root"
    : > SHA256SUMS
    while IFS= read -r -d '' path; do
      sha256sum "$path" >> SHA256SUMS
    done < <(
      {
        find payload scripts -type f -print0
        printf '%s\0' VERSION BUILD-INFO.json install.sh run-web.sh stop-web.sh status.sh
      } | sort -z
    )
  )

  printf '%s\n' "$root"
}

refresh_fixture_checksums() {
  local root="$1"

  (
    cd "$root"
    : > SHA256SUMS
    while IFS= read -r -d '' path; do
      sha256sum "$path" >> SHA256SUMS
    done < <(
      {
        find payload scripts -type f -print0
        printf '%s\0' VERSION BUILD-INFO.json install.sh run-web.sh stop-web.sh status.sh
      } | sort -z
    )
  )
}

run_test_bundle_contract() {
  local deployment output archive listing manifest
  deployment="$(new_deployment_fixture bundle-contract)"
  output="$SUITE_ROOT/bundle-contract/output"

  if "$BUNDLE_SCRIPT" \
      --deployment-root "$deployment" \
      --output-dir "$output" \
      --channel dev \
      --runtime linux-x64 \
      --installer-source "$BOOTSTRAP_INSTALLER" >/dev/null; then
    archive="$(find "$output" -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
    manifest="$(cat "$output/release.json")"
    listing="$(tar -tzf "$archive")"

    if [[ -n "$archive" ]] &&
       [[ "$listing" == mem-migrate-release/* ]] &&
       [[ "$manifest" == *'"schemaVersion": 1'* ]] &&
       [[ "$manifest" == *'"channel": "dev"'* ]] &&
       [[ "$manifest" == *'"runtime": "linux-x64"'* ]] &&
       [[ -x "$output/install-mem-migrate.sh" ]]; then
      pass "publisher helper creates the fixed-root release contract"
      return
    fi
  fi

  fail_test "publisher helper creates the fixed-root release contract"
}

run_test_outer_and_inner_checksums() {
  local deployment output archive verify_root archive_name
  deployment="$(new_deployment_fixture checksum-contract)"
  output="$SUITE_ROOT/checksum-contract/output"
  verify_root="$SUITE_ROOT/checksum-contract/verify"

  "$BUNDLE_SCRIPT" \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --installer-source "$BOOTSTRAP_INSTALLER" >/dev/null

  archive="$(find "$output" -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
  archive_name="$(basename -- "$archive")"
  mkdir -p "$verify_root"

  if (
      cd "$output"
      sha256sum -c "$archive_name.sha256" >/dev/null
    ) &&
    tar -xzf "$archive" -C "$verify_root" &&
    (
      cd "$verify_root/mem-migrate-release"
      sha256sum -c SHA256SUMS >/dev/null
    ); then
    pass "generated outer and inner checksums both verify"
  else
    fail_test "generated outer and inner checksums both verify"
  fi
}

run_test_invalid_inner_checksum_rejected() {
  local deployment output result status
  deployment="$(new_deployment_fixture invalid-inner-checksum)"
  output="$SUITE_ROOT/invalid-inner-checksum/output"
  printf 'corruption\n' >> "$deployment/payload/libe_sqlite3.so"

  set +e
  result="$($BUNDLE_SCRIPT \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --installer-source "$BOOTSTRAP_INSTALLER" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 && "$result" == *"inner SHA256SUMS verification"* && ! -e "$output" ]]; then
    pass "publisher refuses deployment content with invalid inner checksums"
  else
    fail_test "publisher refuses deployment content with invalid inner checksums" "$result"
  fi
}

run_test_channel_and_runtime_validation() {
  local deployment output result status
  deployment="$(new_deployment_fixture invalid-runtime)"
  output="$SUITE_ROOT/invalid-runtime/output"

  set +e
  result="$($BUNDLE_SCRIPT \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --channel nightly \
    --runtime linux-arm64 \
    --installer-source "$BOOTSTRAP_INSTALLER" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 && "$result" == *"Unsupported release channel"* ]]; then
    pass "publisher fails closed for unsupported channel and runtime inputs"
  else
    fail_test "publisher fails closed for unsupported channel and runtime inputs" "$result"
  fi
}


run_test_public_build_info_hygiene() {
  local deployment output archive verify_root embedded_build_info
  deployment="$(new_deployment_fixture public-build-info)"
  output="$SUITE_ROOT/public-build-info/output"
  verify_root="$SUITE_ROOT/public-build-info/verify"

  "$BUNDLE_SCRIPT" \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --channel dev \
    --runtime linux-x64 \
    --installer-source "$BOOTSTRAP_INSTALLER" >/dev/null

  archive="$(find "$output" -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
  mkdir -p "$verify_root"
  tar -xzf "$archive" -C "$verify_root"
  embedded_build_info="$verify_root/mem-migrate-release/BUILD-INFO.json"

  if python3 - "$deployment/BUILD-INFO.json" "$embedded_build_info" <<'PY'
import json
import sys

for path in sys.argv[1:]:
    with open(path, "r", encoding="utf-8") as handle:
        document = json.load(handle)

    if "sourceRepository" in document or "sourceRemote" in document:
        raise SystemExit(1)

    if document.get("sourceCommit") != "0123456789abcdef0123456789abcdef01234567":
        raise SystemExit(1)

    text = json.dumps(document, sort_keys=True).lower()
    for forbidden in ("/home/", "scm.private.invalid", "ssh://", "git@"):
        if forbidden in text:
            raise SystemExit(1)
PY
  then
    pass "public and embedded BUILD-INFO retain source commit without producer-local or private SCM fields"
  else
    fail_test "public and embedded BUILD-INFO retain source commit without producer-local or private SCM fields"
  fi
}

run_test_private_build_info_rejected() {
  local deployment output result status

  deployment="$(new_deployment_fixture private-source-repository)"
  output="$SUITE_ROOT/private-source-repository/output"
  python3 - "$deployment/BUILD-INFO.json" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as handle:
    document = json.load(handle)
document["sourceRepository"] = "/workspace/private-mem-fixture"
with open(path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2)
    handle.write("\n")
PY
  refresh_fixture_checksums "$deployment"

  set +e
  result="$($BUNDLE_SCRIPT \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --installer-source "$BOOTSTRAP_INSTALLER" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 &&
        "$result" == *"unsupported field(s): sourceRepository"* &&
        ! -e "$output" ]]; then
    pass "release bundle rejects producer-local sourceRepository metadata"
  else
    fail_test "release bundle rejects producer-local sourceRepository metadata" "$result"
  fi

  deployment="$(new_deployment_fixture private-source-remote)"
  output="$SUITE_ROOT/private-source-remote/output"
  python3 - "$deployment/BUILD-INFO.json" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as handle:
    document = json.load(handle)
document["sourceRemote"] = "https://scm.private.invalid/mem.git"
with open(path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2)
    handle.write("\n")
PY
  refresh_fixture_checksums "$deployment"

  set +e
  result="$($BUNDLE_SCRIPT \
    --deployment-root "$deployment" \
    --output-dir "$output" \
    --installer-source "$BOOTSTRAP_INSTALLER" 2>&1)"
  status=$?
  set -e

  if [[ $status -ne 0 &&
        "$result" == *"unsupported field(s): sourceRemote"* &&
        ! -e "$output" ]]; then
    pass "release bundle rejects private sourceRemote metadata"
  else
    fail_test "release bundle rejects private sourceRemote metadata" "$result"
  fi

  if ! grep -Fq '"sourceRepository"' "$PUBLISH_TO_DEPLOY_REPO" &&
     ! grep -Fq '"sourceRemote"' "$PUBLISH_TO_DEPLOY_REPO" &&
     grep -Fq '"sourceCommit": source_commit' "$PUBLISH_TO_DEPLOY_REPO"; then
    pass "deployment publisher emits bounded public SCM provenance"
  else
    fail_test "deployment publisher emits bounded public SCM provenance"
  fi
}


run_test_source_date_epoch_is_deterministic() {
  local deployment output_one output_two archive_one archive_two
  deployment="$(new_deployment_fixture deterministic-contract)"
  output_one="$SUITE_ROOT/deterministic-contract/output-one"
  output_two="$SUITE_ROOT/deterministic-contract/output-two"

  SOURCE_DATE_EPOCH=1788554910 "$BUNDLE_SCRIPT" \
    --deployment-root "$deployment" \
    --output-dir "$output_one" \
    --channel stable \
    --runtime linux-x64 \
    --installer-source "$BOOTSTRAP_INSTALLER" >/dev/null

  SOURCE_DATE_EPOCH=1788554910 "$BUNDLE_SCRIPT" \
    --deployment-root "$deployment" \
    --output-dir "$output_two" \
    --channel stable \
    --runtime linux-x64 \
    --installer-source "$BOOTSTRAP_INSTALLER" >/dev/null

  archive_one="$(find "$output_one" -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"
  archive_two="$(find "$output_two" -maxdepth 1 -type f -name '*.tar.gz' -print -quit)"

  if [[ -n "$archive_one" && -n "$archive_two" ]] && cmp -s -- "$archive_one" "$archive_two"; then
    pass "SOURCE_DATE_EPOCH yields deterministic Migrate release archives"
  else
    fail_test "SOURCE_DATE_EPOCH yields deterministic Migrate release archives"
  fi
}


run_test_source_assistant_release_version_contract() {
  if grep -Fq 'MEM_MIGRATE_RELEASE_VERSION="$RELEASE_VERSION" npm run build' "$PUBLISH_CURRENT" &&
     grep -Fq '__MEM_MIGRATE_VERSION__' "$SOURCE_ASSISTANT_VITE" &&
     grep -Fq 'MEM_MIGRATE_RELEASE_VERSION' "$SOURCE_ASSISTANT_VITE" &&
     grep -Fq 'MEM Migrate v{__MEM_MIGRATE_VERSION__}' "$SOURCE_ASSISTANT_APP" &&
     grep -Fq 'SourceAssistantCommandLine.GetApplicationVersion()' "$SOURCE_ASSISTANT_PROGRAM" &&
     grep -Fq 'GetCustomAttribute<AssemblyInformationalVersionAttribute>()' "$SOURCE_ASSISTANT_COMMAND_LINE" &&
     grep -Fq 'HEALTH_VERSION' "$PUBLISH_TO_DEPLOY_REPO" &&
     ! grep -Fq 'MEM Migrate v0.2.0-alpha.1' "$SOURCE_ASSISTANT_APP"; then
    pass "release version is shared by Source Assistant CLI, health endpoint and client identity"
  else
    fail_test "release version is shared by Source Assistant CLI, health endpoint and client identity"
  fi
}

run_test_installed_release_retains_provenance() {
  if grep -Fq -- '--metadata-dir "$REPO_ROOT"' "$PUBLISH_TO_DEPLOY_REPO" &&
     grep -Fq 'cp -a -- "$METADATA_DIR/VERSION" "$STAGING_DIR/VERSION"' "$INSTALL_CURRENT" &&
     grep -Fq 'cp -a -- "$METADATA_DIR/BUILD-INFO.json" "$STAGING_DIR/BUILD-INFO.json"' "$INSTALL_CURRENT" &&
     grep -Fq 'Published Source Assistant health did not report release version' "$INSTALL_CURRENT"; then
    pass "packaged installation retains release provenance and self-tests exact health identity"
  else
    fail_test "packaged installation retains release provenance and self-tests exact health identity"
  fi
}

main() {
  [[ -x "$BUNDLE_SCRIPT" ]] || {
    printf 'Bundle script is missing or not executable: %s\n' "$BUNDLE_SCRIPT" >&2
    exit 1
  }

  run_test_bundle_contract
  run_test_outer_and_inner_checksums
  run_test_invalid_inner_checksum_rejected
  run_test_channel_and_runtime_validation
  run_test_public_build_info_hygiene
  run_test_private_build_info_rejected
  run_test_source_date_epoch_is_deterministic
  run_test_source_assistant_release_version_contract
  run_test_installed_release_retains_provenance

  printf '\nRelease bundle tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
  [[ $FAILED -eq 0 ]]
}

main "$@"
