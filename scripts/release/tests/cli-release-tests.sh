#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"
REPO_ROOT="$(cd -- "${RELEASE_DIR}/../.." && pwd -P)"
BUILDER="${RELEASE_DIR}/build-cli-release.sh"
INSTALLER_BUILDER="${RELEASE_DIR}/build-installer-bundle.sh"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-cli-release-tests.XXXXXX")"
trap 'rm -rf -- "${TMP_ROOT}"' EXIT

pass() { printf 'PASS: %s\n' "$1"; PASSED=$((PASSED + 1)); }
fail_test() { printf 'FAIL: %s\n' "$1" >&2; [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2; FAILED=$((FAILED + 1)); }

[[ -x "${BUILDER}" ]] || { echo "Missing executable builder: ${BUILDER}" >&2; exit 1; }
[[ -x "${INSTALLER_BUILDER}" ]] || { echo "Missing installer builder: ${INSTALLER_BUILDER}" >&2; exit 1; }

FAKE_BIN="${TMP_ROOT}/bin"
mkdir -p "${FAKE_BIN}"
REAL_GIT="$(command -v git)"
REAL_PYTHON3="$(command -v python3)"
REAL_SHA256SUM="$(command -v sha256sum)"

cat > "${FAKE_BIN}/dotnet" <<'EOF_DOTNET'
#!/usr/bin/env bash
set -Eeuo pipefail
: "${FAKE_DOTNET_LOG:?}"
printf '%s\n' "$@" > "${FAKE_DOTNET_LOG}"

OUT=""
VERSION=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        -o)
            OUT="$2"
            shift 2
            ;;
        -p:InformationalVersion=*)
            VERSION="${1#-p:InformationalVersion=}"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

[[ -n "${OUT}" ]] || { echo "fake dotnet: missing -o" >&2; exit 2; }
[[ -n "${VERSION}" ]] || { echo "fake dotnet: missing InformationalVersion" >&2; exit 2; }

mkdir -p "${OUT}"
cat > "${OUT}/mem" <<EOF_MEM
#!/usr/bin/env bash
if [[ "\${1:-}" == "--version" ]]; then
cat <<'EOF_VERSION'
Message Easy Mode CLI
Version: ${VERSION}
Command: mem
EOF_VERSION
exit 0
fi
echo "fake mem"
EOF_MEM
chmod 0755 "${OUT}/mem"
EOF_DOTNET
chmod 0755 "${FAKE_BIN}/dotnet"

export PATH="${FAKE_BIN}:$(dirname "${REAL_GIT}"):$(dirname "${REAL_PYTHON3}"):$(dirname "${REAL_SHA256SUM}"):/usr/bin:/bin"
export FAKE_DOTNET_LOG="${TMP_ROOT}/dotnet-args.txt"

VERSION="0.2.0"
SOURCE_COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD)"
SOURCE_EPOCH="$(git -C "${REPO_ROOT}" show -s --format=%ct "${SOURCE_COMMIT}")"
OUT_A="${TMP_ROOT}/out-a"
OUT_B="${TMP_ROOT}/out-b"

"${BUILDER}" \
  --version "${VERSION}" \
  --source-commit "${SOURCE_COMMIT}" \
  --source-date-epoch "${SOURCE_EPOCH}" \
  --allow-dirty \
  --output-dir "${OUT_A}" >/dev/null

ARTIFACT_A="${OUT_A}/mem-cli-0.2.0-linux-x64"
BUILD_INFO_A="${ARTIFACT_A}.build-info.json"

[[ -x "${ARTIFACT_A}" ]] && pass "canonical standalone CLI artifact is produced" || fail_test "canonical standalone CLI artifact is produced"
[[ -f "${BUILD_INFO_A}" ]] && pass "CLI producer build-info is produced" || fail_test "CLI producer build-info is produced"

VERSION_OUTPUT="$("${ARTIFACT_A}" --version)"
EXPECTED=$'Message Easy Mode CLI\nVersion: 0.2.0\nCommand: mem'
[[ "${VERSION_OUTPUT}" == "${EXPECTED}" ]] && pass "standalone CLI reports exact Message Easy Mode 0.2.0 identity" || fail_test "standalone CLI reports exact Message Easy Mode 0.2.0 identity" "${VERSION_OUTPUT}"

grep -Fq -- '-p:Version=0.2.0' "${FAKE_DOTNET_LOG}" \
  && grep -Fq -- '-p:AssemblyVersion=0.2.0.0' "${FAKE_DOTNET_LOG}" \
  && grep -Fq -- '-p:FileVersion=0.2.0.0' "${FAKE_DOTNET_LOG}" \
  && grep -Fq -- '-p:InformationalVersion=0.2.0' "${FAKE_DOTNET_LOG}" \
  && pass "dotnet publish receives exact release version metadata" \
  || fail_test "dotnet publish receives exact release version metadata"

grep -Fq -- '-p:Product=Message Easy Mode CLI' "${FAKE_DOTNET_LOG}" \
  && grep -Fq -- '-p:Company=Message Easy Mode' "${FAKE_DOTNET_LOG}" \
  && pass "dotnet publish receives current product metadata" \
  || fail_test "dotnet publish receives current product metadata"

EXPECTED_WORKTREE_DIRTY=false
EXPECTED_RELEASE_ELIGIBLE=true
if [[ -n "$(git -C "${REPO_ROOT}" status --porcelain --untracked-files=all)" ]]; then
  EXPECTED_WORKTREE_DIRTY=true
  EXPECTED_RELEASE_ELIGIBLE=false
fi

python3 - \
  "${BUILD_INFO_A}" \
  "${SOURCE_COMMIT}" \
  "${EXPECTED_WORKTREE_DIRTY}" \
  "${EXPECTED_RELEASE_ELIGIBLE}" <<'PY_CHECK' \
  && pass "build-info records artifact hash, source commit, runtime, and truthful dirty proof status" \
  || fail_test "build-info records artifact hash, source commit, runtime, and truthful dirty proof status"
import hashlib, json, pathlib, sys
path = pathlib.Path(sys.argv[1])
commit = sys.argv[2]
expected_dirty = sys.argv[3] == "true"
expected_release_eligible = sys.argv[4] == "true"
doc = json.loads(path.read_text())
artifact = path.with_name(doc["artifact"]["fileName"])
assert doc["product"]["version"] == "0.2.0"
assert doc["artifact"]["id"] == "cli"
assert doc["artifact"]["runtime"] == "linux-x64"
assert doc["source"]["commit"] == commit
assert doc["source"]["workingTreeDirty"] is expected_dirty
assert doc["build"]["releaseEligible"] is expected_release_eligible
assert doc["artifact"]["sizeBytes"] == artifact.stat().st_size
assert doc["artifact"]["sha256"] == hashlib.sha256(artifact.read_bytes()).hexdigest()
PY_CHECK

"${BUILDER}" \
  --version "${VERSION}" \
  --source-commit "${SOURCE_COMMIT}" \
  --source-date-epoch "${SOURCE_EPOCH}" \
  --allow-dirty \
  --output-dir "${OUT_B}" >/dev/null

ARTIFACT_B="${OUT_B}/mem-cli-0.2.0-linux-x64"
[[ "$(sha256sum "${ARTIFACT_A}" | awk '{print $1}')" == "$(sha256sum "${ARTIFACT_B}" | awk '{print $1}')" ]] \
  && pass "identical CLI producer inputs yield identical artifact bytes in the deterministic fixture" \
  || fail_test "identical CLI producer inputs yield identical artifact bytes in the deterministic fixture"

set +e
BAD_OUTPUT="$("${BUILDER}" --version dev --source-commit "${SOURCE_COMMIT}" --source-date-epoch "${SOURCE_EPOCH}" --allow-dirty --output-dir "${TMP_ROOT}/bad" 2>&1)"
BAD_STATUS=$?
set -e
[[ ${BAD_STATUS} -ne 0 && "${BAD_OUTPUT}" == *"Invalid release version"* ]] \
  && pass "CLI producer rejects dev as a public release version" \
  || fail_test "CLI producer rejects dev as a public release version" "${BAD_OUTPUT}"

set +e
DIRTY_OUTPUT="$("${BUILDER}" --version "${VERSION}" --source-commit "${SOURCE_COMMIT}" --source-date-epoch "${SOURCE_EPOCH}" --output-dir "${TMP_ROOT}/dirty" 2>&1)"
DIRTY_STATUS=$?
set -e
if [[ -n "$(git -C "${REPO_ROOT}" status --porcelain --untracked-files=all)" ]]; then
  [[ ${DIRTY_STATUS} -ne 0 && "${DIRTY_OUTPUT}" == *"worktree is dirty"* ]] \
    && pass "CLI producer refuses a dirty release-eligible build" \
    || fail_test "CLI producer refuses a dirty release-eligible build" "${DIRTY_OUTPUT}"
else
  [[ ${DIRTY_STATUS} -eq 0 ]] \
    && pass "CLI producer accepts the clean worktree without --allow-dirty" \
    || fail_test "CLI producer accepts the clean worktree without --allow-dirty" "${DIRTY_OUTPUT}"
fi

# Prove the 03B installer consumes exactly the standalone CLI bytes.
INSTALLER_OUT="${TMP_ROOT}/installer"
FAKE_DIGEST="$(printf 'a%.0s' {1..64})"
"${INSTALLER_BUILDER}" \
  --version "${VERSION}" \
  --control-plane-image "ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:${FAKE_DIGEST}" \
  --source-commit "${SOURCE_COMMIT}" \
  --source-date-epoch "${SOURCE_EPOCH}" \
  --cli-binary "${ARTIFACT_A}" \
  --output-dir "${INSTALLER_OUT}" >/dev/null

ARCHIVE="${INSTALLER_OUT}/mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz"
EXTRACT="${TMP_ROOT}/installer-extract"
mkdir -p "${EXTRACT}"
tar -xzf "${ARCHIVE}" -C "${EXTRACT}"
EMBEDDED="${EXTRACT}/mem-installer-0.2.0-ubuntu-24.04-amd64/bootstrap/cli/mem"

[[ "$(sha256sum "${ARTIFACT_A}" | awk '{print $1}')" == "$(sha256sum "${EMBEDDED}" | awk '{print $1}')" ]] \
  && pass "03B installer embeds exactly the canonical standalone CLI bytes" \
  || fail_test "03B installer embeds exactly the canonical standalone CLI bytes"

printf '\nCLI release producer tests: %s passed, %s failed\n' "${PASSED}" "${FAILED}"
[[ "${FAILED}" -eq 0 ]]
