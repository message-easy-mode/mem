#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
export LC_ALL=C
umask 022

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST_VALIDATOR="$SCRIPT_DIR/validate-public-source-manifest.py"
AUDITOR="$SCRIPT_DIR/audit-public-source.py"
DEFAULT_ALLOWLIST="$SCRIPT_DIR/public-source-content-allowlist.tsv"
ALLOWLIST="$DEFAULT_ALLOWLIST"

PREPARED_DIR=""
OUTPUT_DIR=""
MODE=""
CONTAINER_BUILD=false
KEEP_WORKSPACE=false

usage() {
  cat <<'USAGE'
Verify a prepared Message Easy Mode public-source snapshot.

Usage:
  ./scripts/public-source/verify-public-source.sh \
    --prepared-dir <PUBLIC-SOURCE-01A-output> \
    --output-dir <new-verification-directory> \
    (--scan-only | --full) \
    [--allowlist <path>] \
    [--container-build] \
    [--keep-workspace]

Modes:
  --scan-only
      Re-validate the prepared artifact and exact source tree, then run the
      mandatory exposure/high-confidence secret scan. No dependency restore,
      build, test, Docker, Git remote, or publication operation is performed.

  --full
      Run the scan gate first. Only after a clean scan, copy source/ into a
      disposable isolated workspace and run the public build/test qualification:
        * public-source producer fixture regression;
        * MEM installer/backend/CLI solution restore, build and tests;
        * MEM Web npm ci, tests and production build;
        * MEM Migrate solution restore, build and tests;
        * MEM Migrate Web client npm ci, tests and production build.

Options:
  --allowlist <path>
      Use an alternate exact rule/path content allowlist. Defaults to the
      reviewed repository allowlist beside this verifier.

  --container-build
      With --full, also build the production-shaped Control Plane image using
      only the isolated exported installer/ and migrate/ trees as Docker
      contexts. The temporary verification image is removed afterwards.

  --keep-workspace
      Preserve the disposable build workspace under the verification output.
      This is intended only for diagnosing a failed qualification run.

The verifier never commits, tags, pushes, writes Git remotes, or publishes.
Full mode may access public package/container registries for dependency restore
and base-image acquisition.
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prepared-dir)
      [[ $# -ge 2 ]] || fail "--prepared-dir requires a path."
      PREPARED_DIR="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --scan-only)
      [[ -z "$MODE" ]] || fail "Choose exactly one verification mode."
      MODE="scan"
      shift
      ;;
    --full)
      [[ -z "$MODE" ]] || fail "Choose exactly one verification mode."
      MODE="full"
      shift
      ;;
    --allowlist)
      [[ $# -ge 2 ]] || fail "--allowlist requires a path."
      ALLOWLIST="$2"
      shift 2
      ;;
    --container-build)
      CONTAINER_BUILD=true
      shift
      ;;
    --keep-workspace)
      KEEP_WORKSPACE=true
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

[[ -n "$PREPARED_DIR" ]] || fail "--prepared-dir is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."
[[ -n "$MODE" ]] || fail "Choose --scan-only or --full."
[[ "$CONTAINER_BUILD" == false || "$MODE" == "full" ]] || fail "--container-build requires --full."

for command_name in python3 sha256sum realpath mkdir rm mktemp cp stat mv tail; do
  require_command "$command_name"
done

[[ -d "$PREPARED_DIR" && ! -L "$PREPARED_DIR" ]] || fail "Prepared directory is missing or unsafe: $PREPARED_DIR"
PREPARED_DIR="$(cd -- "$PREPARED_DIR" && pwd -P)"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
ALLOWLIST="$(realpath -e -- "$ALLOWLIST")" || fail "Content allowlist does not exist: $ALLOWLIST"
[[ "$OUTPUT_DIR" != "/" && "$OUTPUT_DIR" != "$PREPARED_DIR" ]] || fail "Unsafe verification output path: $OUTPUT_DIR"
[[ ! -e "$OUTPUT_DIR" ]] || fail "Verification output already exists; refusing to overwrite it: $OUTPUT_DIR"
case "$OUTPUT_DIR/" in
  "$PREPARED_DIR/"*) fail "Verification output may not be written inside the prepared artifact: $OUTPUT_DIR" ;;
esac
mkdir -p -- "$OUTPUT_DIR/logs"

MANIFEST="$PREPARED_DIR/public-source.json"
SOURCE_DIR="$PREPARED_DIR/source"
FILE_MANIFEST="$PREPARED_DIR/evidence/file-manifest.tsv"
CHECKSUMS="$PREPARED_DIR/SHA256SUMS"
SCAN_REPORT="$OUTPUT_DIR/exposure-findings.tsv"
SCAN_SUMMARY="$OUTPUT_DIR/scan-summary.json"
BUILD_STEPS="$OUTPUT_DIR/build-steps.tsv"
VERIFICATION_JSON="$OUTPUT_DIR/verification.json"

[[ -f "$MANIFEST" && ! -L "$MANIFEST" ]] || fail "Prepared manifest is missing or unsafe."
[[ -d "$SOURCE_DIR" && ! -L "$SOURCE_DIR" ]] || fail "Prepared source tree is missing or unsafe."
[[ -f "$FILE_MANIFEST" && ! -L "$FILE_MANIFEST" ]] || fail "Prepared file manifest is missing or unsafe."
[[ -f "$CHECKSUMS" && ! -L "$CHECKSUMS" ]] || fail "Prepared SHA256SUMS is missing or unsafe."
[[ -x "$MANIFEST_VALIDATOR" ]] || fail "Manifest validator is missing or not executable: $MANIFEST_VALIDATOR"
[[ -x "$AUDITOR" ]] || fail "Public-source auditor is missing or not executable: $AUDITOR"
[[ -f "$ALLOWLIST" && ! -L "$ALLOWLIST" ]] || fail "Content allowlist is missing or unsafe: $ALLOWLIST"

printf '=== PREPARED ARTIFACT INTEGRITY ===\n'
(
  cd "$PREPARED_DIR"
  sha256sum -c SHA256SUMS
)
"$MANIFEST_VALIDATOR" "$MANIFEST"

printf '\n=== PUBLIC SOURCE EXPOSURE SCAN ===\n'
set +e
"$AUDITOR" \
  --source-root "$SOURCE_DIR" \
  --manifest "$MANIFEST" \
  --file-manifest "$FILE_MANIFEST" \
  --allowlist "$ALLOWLIST" \
  --report "$SCAN_REPORT" \
  --summary "$SCAN_SUMMARY"
AUDIT_RC=$?
set -e
if [[ "$AUDIT_RC" -ne 0 ]]; then
  printf '\nPUBLIC SOURCE VERIFICATION STOPPED AT EXPOSURE SCAN.\n' >&2
  printf 'Findings: %s\n' "$SCAN_REPORT" >&2
  printf 'Summary:  %s\n' "$SCAN_SUMMARY" >&2
  exit "$AUDIT_RC"
fi

mapfile -t MANIFEST_FIELDS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
m=json.load(open(sys.argv[1], encoding="utf-8"))
print(m["product"]["version"])
print(m["source"]["commit"])
print(m["source"]["tree"])
print(m["source"]["sourceDateEpoch"])
print(m["export"]["generatedAtUtc"])
print(m["contents"]["treeSha256"])
print(m["contents"]["fileCount"])
PY
)
VERSION="${MANIFEST_FIELDS[0]}"
SOURCE_COMMIT="${MANIFEST_FIELDS[1]}"
SOURCE_TREE="${MANIFEST_FIELDS[2]}"
SOURCE_DATE_EPOCH="${MANIFEST_FIELDS[3]}"
GENERATED_AT_UTC="${MANIFEST_FIELDS[4]}"
PUBLIC_TREE_SHA256="${MANIFEST_FIELDS[5]}"
FILE_COUNT="${MANIFEST_FIELDS[6]}"

: > "$BUILD_STEPS"
printf 'step\tstatus\tlog\n' > "$BUILD_STEPS"

if [[ "$MODE" == "scan" ]]; then
  python3 - "$VERIFICATION_JSON" "$VERSION" "$SOURCE_COMMIT" "$SOURCE_TREE" "$PUBLIC_TREE_SHA256" "$FILE_COUNT" <<'PY'
import json, sys
out, version, commit, tree, public_sha, count = sys.argv[1:]
doc = {
    "schemaVersion": 1,
    "status": "pass",
    "mode": "scan-only",
    "productVersion": version,
    "sourceCommit": commit,
    "sourceTree": tree,
    "publicTreeSha256": public_sha,
    "fileCount": int(count),
    "exposureScan": "scan-summary.json",
    "buildSteps": [],
    "publicationPerformed": False,
}
open(out, "w", encoding="utf-8").write(json.dumps(doc, indent=2, sort_keys=True) + "\n")
PY
  printf '\nPUBLIC SOURCE SCAN VERIFICATION PASSED\n'
  printf 'Version:        %s\n' "$VERSION"
  printf 'Source commit:  %s\n' "$SOURCE_COMMIT"
  printf 'Public tree:    %s\n' "$PUBLIC_TREE_SHA256"
  printf 'Verification:   %s\n' "$VERIFICATION_JSON"
  printf 'No build, Git remote write, or publication operation was performed.\n'
  exit 0
fi

for command_name in git dotnet npm; do
  require_command "$command_name"
done
if [[ "$CONTAINER_BUILD" == true ]]; then
  require_command docker
  docker buildx version >/dev/null 2>&1 || fail "Docker Buildx is required for --container-build."
fi

for required_path in \
  installer/src/MemInstaller.sln \
  installer/src/Web/package.json \
  installer/src/Web/package-lock.json \
  migrate/Mem.Migrate.sln \
  migrate/src/Mem.Migrate.Web/ClientApp/package.json \
  migrate/src/Mem.Migrate.Web/ClientApp/package-lock.json \
  scripts/public-source/tests/public-source-prepare-tests.sh
do
  [[ -e "$SOURCE_DIR/$required_path" ]] || fail "Public source is missing required build/test input: $required_path"
done

VERIFY_TMP="$(mktemp -d "${TMPDIR:-/tmp}/mem-public-source-verify.XXXXXX")"
WORKSPACE="$VERIFY_TMP/source"
HOME_ROOT="$VERIFY_TMP/home"
mkdir -p -- "$WORKSPACE" "$HOME_ROOT" "$HOME_ROOT/.nuget/packages" "$HOME_ROOT/.npm"
cp -a -- "$SOURCE_DIR/." "$WORKSPACE/"

cleanup() {
  local rc=$?
  if [[ "$KEEP_WORKSPACE" == true ]]; then
    PRESERVED="$OUTPUT_DIR/workspace"
    if [[ ! -e "$PRESERVED" ]]; then
      mv -- "$WORKSPACE" "$PRESERVED" 2>/dev/null || true
      printf 'Preserved isolated workspace: %s\n' "$PRESERVED" >&2
    fi
  fi
  rm -rf -- "$VERIFY_TMP"
  exit "$rc"
}
trap cleanup EXIT

export HOME="$HOME_ROOT"
export DOTNET_CLI_HOME="$HOME_ROOT/.dotnet"
export NUGET_PACKAGES="$HOME_ROOT/.nuget/packages"
export npm_config_cache="$HOME_ROOT/.npm"
export npm_config_userconfig="$HOME_ROOT/.npmrc"
export npm_config_registry="https://registry.npmjs.org/"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export CI=true
unset GIT_DIR GIT_WORK_TREE || true

run_step() {
  local label="$1"
  local cwd="$2"
  shift 2
  local log="$OUTPUT_DIR/logs/${label}.log"
  printf '\n=== %s ===\n' "$label"
  set +e
  (
    cd "$cwd"
    "$@"
  ) >"$log" 2>&1
  local rc=$?
  set -e
  if [[ "$rc" -ne 0 ]]; then
    printf '%s\tfail\tlogs/%s.log\n' "$label" "$label" >> "$BUILD_STEPS"
    printf 'FAILED: %s (rc=%s)\n' "$label" "$rc" >&2
    printf 'Log: %s\n' "$log" >&2
    tail -n 80 "$log" >&2 || true
    exit "$rc"
  fi
  printf '%s\tpass\tlogs/%s.log\n' "$label" "$label" >> "$BUILD_STEPS"
  printf 'PASS: %s\n' "$label"
}

run_step \
  public-source-prepare-regression \
  "$WORKSPACE" \
  ./scripts/public-source/tests/public-source-prepare-tests.sh

run_step \
  installer-dotnet-restore \
  "$WORKSPACE/installer/src" \
  dotnet restore MemInstaller.sln --source https://api.nuget.org/v3/index.json
run_step \
  installer-dotnet-build \
  "$WORKSPACE/installer/src" \
  dotnet build MemInstaller.sln --no-restore
run_step \
  installer-dotnet-test \
  "$WORKSPACE/installer/src" \
  dotnet test MemInstaller.sln --no-build

run_step web-npm-ci "$WORKSPACE/installer/src/Web" npm ci --no-audit --no-fund
run_step web-test "$WORKSPACE/installer/src/Web" npm run test
run_step web-build "$WORKSPACE/installer/src/Web" npm run build

run_step \
  migrate-dotnet-restore \
  "$WORKSPACE/migrate" \
  dotnet restore Mem.Migrate.sln --source https://api.nuget.org/v3/index.json
run_step \
  migrate-dotnet-build \
  "$WORKSPACE/migrate" \
  dotnet build Mem.Migrate.sln --no-restore
run_step \
  migrate-dotnet-test \
  "$WORKSPACE/migrate" \
  dotnet test Mem.Migrate.sln --no-build

MIGRATE_CLIENT="$WORKSPACE/migrate/src/Mem.Migrate.Web/ClientApp"
run_step migrate-web-npm-ci "$MIGRATE_CLIENT" npm ci --no-audit --no-fund
run_step migrate-web-test "$MIGRATE_CLIENT" npm run test
run_step migrate-web-build "$MIGRATE_CLIENT" npm run build

if [[ "$CONTAINER_BUILD" == true ]]; then
  VERIFY_IMAGE="mem-public-source-verify:${SOURCE_COMMIT:0:12}"
  run_step \
    control-plane-container-build \
    "$WORKSPACE" \
    docker buildx build \
      --load \
      --file "$WORKSPACE/installer/Dockerfile" \
      --build-context "migrate-source=$WORKSPACE/migrate" \
      --build-arg "MEM_PRODUCT_VERSION=$VERSION" \
      --build-arg "MEM_COMMIT_SHA=$SOURCE_COMMIT" \
      --build-arg "BUILD_DATE=$GENERATED_AT_UTC" \
      --build-arg "SOURCE_DATE_EPOCH=$SOURCE_DATE_EPOCH" \
      --tag "$VERIFY_IMAGE" \
      "$WORKSPACE/installer"
  docker image rm "$VERIFY_IMAGE" >/dev/null 2>&1 || true
fi

python3 - "$VERIFICATION_JSON" "$VERSION" "$SOURCE_COMMIT" "$SOURCE_TREE" "$PUBLIC_TREE_SHA256" "$FILE_COUNT" "$CONTAINER_BUILD" "$BUILD_STEPS" <<'PY'
import json, sys
out, version, commit, tree, public_sha, count, container_build, steps_path = sys.argv[1:]
steps=[]
with open(steps_path, encoding="utf-8") as handle:
    next(handle, None)
    for line in handle:
        line=line.rstrip("\n")
        if not line:
            continue
        step, status, log = line.split("\t")
        steps.append({"step": step, "status": status, "log": log})
doc = {
    "schemaVersion": 1,
    "status": "pass",
    "mode": "full",
    "productVersion": version,
    "sourceCommit": commit,
    "sourceTree": tree,
    "publicTreeSha256": public_sha,
    "fileCount": int(count),
    "exposureScan": "scan-summary.json",
    "containerBuild": container_build == "true",
    "buildSteps": steps,
    "publicationPerformed": False,
}
open(out, "w", encoding="utf-8").write(json.dumps(doc, indent=2, sort_keys=True) + "\n")
PY

printf '\nPUBLIC SOURCE FULL VERIFICATION PASSED\n'
printf 'Version:        %s\n' "$VERSION"
printf 'Source commit:  %s\n' "$SOURCE_COMMIT"
printf 'Public tree:    %s\n' "$PUBLIC_TREE_SHA256"
printf 'Verification:   %s\n' "$VERIFICATION_JSON"
printf 'Build logs:     %s/logs\n' "$OUTPUT_DIR"
printf 'No Git remote write or publication operation was performed.\n'
