#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
VALIDATOR="$RELEASE_DIR/validate-release-manifest.py"
EXAMPLE="$RELEASE_DIR/examples/release-0.2.0.example.json"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-release-contract.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT

pass() { printf 'PASS: %s\n' "$1"; PASSED=$((PASSED + 1)); }
fail_test() { printf 'FAIL: %s\n' "$1" >&2; [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2; FAILED=$((FAILED + 1)); }

mutate() {
  local src="$1" dst="$2" code="$3"
  python3 - "$src" "$dst" "$code" <<'PY'
import json, sys
src, dst, code = sys.argv[1:]
with open(src, "r", encoding="utf-8") as h:
    doc = json.load(h)
exec(code, {"doc": doc})
with open(dst, "w", encoding="utf-8") as h:
    json.dump(doc, h, indent=2)
    h.write("\n")
PY
}

expect_reject() {
  local name="$1" file="$2" needle="$3"
  local output status
  set +e
  output="$(python3 "$VALIDATOR" "$file" 2>&1)"
  status=$?
  set -e
  if [[ $status -ne 0 && "$output" == *"$needle"* ]]; then
    pass "$name"
  else
    fail_test "$name" "$output"
  fi
}

python3 "$VALIDATOR" "$EXAMPLE" >/dev/null && pass "valid 0.2.0 example manifest is accepted" || fail_test "valid 0.2.0 example manifest is accepted"

mutate "$EXAMPLE" "$TMP_ROOT/stable-prerelease.json" "doc['product']['version']='0.2.0-rc.1'; doc['release']['publication']['tag']='v0.2.0-rc.1'; doc['controlPlane']['image']['tag']='0.2.0-rc.1'; doc['controlPlane']['image']['reference']=doc['controlPlane']['image']['reference'].replace(':0.2.0@', ':0.2.0-rc.1@'); [a.__setitem__('fileName', a['fileName'].replace('0.2.0','0.2.0-rc.1')) for a in doc['artifacts']]"
expect_reject "stable channel rejects prerelease version" "$TMP_ROOT/stable-prerelease.json" "stable channel"

mutate "$EXAMPLE" "$TMP_ROOT/unsupported-host.json" "doc['support']['controlPlaneHosts'].append({'os':'ubuntu','version':'22.04','architecture':'amd64'})"
expect_reject "0.2.0 rejects unproven extra host support" "$TMP_ROOT/unsupported-host.json" "exactly Ubuntu 24.04 amd64"

mutate "$EXAMPLE" "$TMP_ROOT/mutable-filename.json" "doc['artifacts'][2]['fileName']='mem-cli-stable-0.2.0-linux-x64'"
expect_reject "public artifact filenames reject mutable channel words" "$TMP_ROOT/mutable-filename.json" "mutable channel word"

mutate "$EXAMPLE" "$TMP_ROOT/bad-image.json" "doc['controlPlane']['image']['reference']='ghcr.io/message-easy-mode/mem-control-plane:stable'"
expect_reject "Control Plane reference must use exact version and digest" "$TMP_ROOT/bad-image.json" "exact version+digest"

mutate "$EXAMPLE" "$TMP_ROOT/bad-digest.json" "doc['controlPlane']['image']['digest']='sha256:1234'"
expect_reject "Control Plane digest must be immutable SHA-256" "$TMP_ROOT/bad-digest.json" "sha256:<64 lowercase hex>"

mutate "$EXAMPLE" "$TMP_ROOT/missing-sbom.json" "doc['artifacts']=[a for a in doc['artifacts'] if a['id']!='sbom']"
expect_reject "required SBOM artifact cannot disappear" "$TMP_ROOT/missing-sbom.json" "missing required artifacts"

mutate "$EXAMPLE" "$TMP_ROOT/missing-migrate-manifest.json" "doc['artifacts']=[a for a in doc['artifacts'] if a['id']!='migrate-manifest']"
expect_reject "required Migrate component manifest cannot disappear" "$TMP_ROOT/missing-migrate-manifest.json" "missing required artifacts"

mutate "$EXAMPLE" "$TMP_ROOT/wrong-org.json" "doc['release']['publication']['repository']='https://github.com/other-org/mem'"
expect_reject "public release repository must be under message-easy-mode" "$TMP_ROOT/wrong-org.json" "message-easy-mode"


mutate "$EXAMPLE" "$TMP_ROOT/wrong-repository.json" "doc['release']['publication']['repository']='https://github.com/message-easy-mode/mem'"
expect_reject "0.2.0 publication repository is exactly mem-releases" "$TMP_ROOT/wrong-repository.json" "must be https://github.com/message-easy-mode/mem-releases"

mutate "$EXAMPLE" "$TMP_ROOT/wrong-source-repository.json" "doc['sources'][0]['repository']='https://github.com/message-easy-mode/mem-releases'"
expect_reject "0.2.0 source repository is canonical mem" "$TMP_ROOT/wrong-source-repository.json" "source must be control-plane at https://github.com/message-easy-mode/mem"

mutate "$EXAMPLE" "$TMP_ROOT/wrong-source.json" "doc['sources'][0]['id']='mem-deploy'; doc['controlPlane']['sourceId']='mem-deploy'; [a.__setitem__('sourceId','mem-deploy') for a in doc['artifacts']]"
expect_reject "0.2.0 source identity is exactly control-plane" "$TMP_ROOT/wrong-source.json" "source must be control-plane"

printf '\nRelease contract tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
[[ $FAILED -eq 0 ]]
