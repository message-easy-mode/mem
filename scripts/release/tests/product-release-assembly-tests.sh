#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-product-release-tests.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT

pass() { printf 'PASS: %s\n' "$1"; PASSED=$((PASSED + 1)); }
fail_test() {
  printf 'FAIL: %s\n' "$1" >&2
  [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2
  FAILED=$((FAILED + 1))
}

expect_reject() {
  local name="$1" needle="$2"
  shift 2
  local output status
  set +e
  output="$("$@" 2>&1)"
  status=$?
  set -e
  if [[ $status -ne 0 && "$output" == *"$needle"* ]]; then
    pass "$name"
  else
    fail_test "$name" "$output"
  fi
}

REPO="$TMP_ROOT/repo"
mkdir -p \
  "$REPO/scripts/release/templates" \
  "$REPO/bootstrap/lib" \
  "$REPO/bootstrap/cli"

cp -a "$RELEASE_DIR/assemble-product-release.sh" "$REPO/scripts/release/"
cp -a "$RELEASE_DIR/build-installer-bundle.sh" "$REPO/scripts/release/"
cp -a "$RELEASE_DIR/validate-release-manifest.py" "$REPO/scripts/release/"
cp -a "$RELEASE_DIR/RELEASE-CONTRACT.md" "$REPO/scripts/release/"
cp -a "$RELEASE_DIR/templates/install-mem-release.sh.in" "$REPO/scripts/release/templates/"

cat > "$REPO/install.sh" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
exec "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)/bootstrap/install.sh" "$@"
EOF
cat > "$REPO/bootstrap/install.sh" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf 'fixture installer\n'
EOF
cat > "$REPO/bootstrap/lib/common.sh" <<'EOF'
#!/usr/bin/env bash
fixture_common() { :; }
EOF
cat > "$REPO/bootstrap/cli/install-host-command.sh" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
exit 0
EOF
chmod 0755 \
  "$REPO/install.sh" \
  "$REPO/bootstrap/install.sh" \
  "$REPO/bootstrap/lib/common.sh" \
  "$REPO/bootstrap/cli/install-host-command.sh" \
  "$REPO/scripts/release/assemble-product-release.sh" \
  "$REPO/scripts/release/build-installer-bundle.sh" \
  "$REPO/scripts/release/validate-release-manifest.py"

git -C "$REPO" init -q
git -C "$REPO" config user.email "release-tests@example.invalid"
git -C "$REPO" config user.name "MEM release tests"
git -C "$REPO" add .
GIT_AUTHOR_DATE="2026-09-05T00:00:00Z" \
GIT_COMMITTER_DATE="2026-09-05T00:00:00Z" \
git -C "$REPO" commit -qm "fixture release source"

SOURCE_COMMIT="$(git -C "$REPO" rev-parse HEAD)"
SOURCE_EPOCH="$(git -C "$REPO" show -s --format=%ct HEAD)"
IMAGE="ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"

CLI_DIR="$TMP_ROOT/cli"
MIGRATE_DIR="$TMP_ROOT/migrate"
mkdir -p "$CLI_DIR" "$MIGRATE_DIR"

CLI="$CLI_DIR/mem-cli-0.2.0-linux-x64"
cat > "$CLI" <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" == "--version" ]]; then
  cat <<'OUT'
Message Easy Mode CLI
Version: 0.2.0
Command: mem
OUT
  exit 0
fi
exit 0
EOF
chmod 0755 "$CLI"

python3 - "$CLI" "$CLI_DIR/mem-cli-0.2.0-linux-x64.build-info.json" "$SOURCE_COMMIT" "$SOURCE_EPOCH" <<'PY'
import hashlib, json, os, sys
binary, output, commit, epoch = sys.argv[1:]
h = hashlib.sha256(open(binary, "rb").read()).hexdigest()
doc = {
    "schemaVersion": 1,
    "product": {"id": "mem", "name": "Message Easy Mode", "version": "0.2.0"},
    "artifact": {
        "id": "cli",
        "kind": "cli-binary",
        "fileName": "mem-cli-0.2.0-linux-x64",
        "runtime": "linux-x64",
        "sha256": h,
        "sizeBytes": os.path.getsize(binary),
    },
    "source": {
        "commit": commit,
        "sourceDateEpoch": int(epoch),
        "workingTreeDirty": False,
    },
    "build": {
        "configuration": "Release",
        "selfContained": True,
        "singleFile": True,
        "generatedAtUtc": "2026-09-05T00:00:00Z",
        "releaseEligible": True,
    },
}
with open(output, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2, sort_keys=True)
    f.write("\n")
PY

cat > "$MIGRATE_DIR/install-mem-migrate-0.2.0.sh" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod 0755 "$MIGRATE_DIR/install-mem-migrate-0.2.0.sh"
printf 'fixture migrate archive\n' > "$MIGRATE_DIR/mem-migrate-0.2.0-linux-x64.tar.gz"
MIGRATE_SHA="$(sha256sum "$MIGRATE_DIR/mem-migrate-0.2.0-linux-x64.tar.gz" | awk '{print $1}')"
printf '%s  %s\n' "$MIGRATE_SHA" "mem-migrate-0.2.0-linux-x64.tar.gz" \
  > "$MIGRATE_DIR/mem-migrate-0.2.0-linux-x64.tar.gz.sha256"

python3 - "$MIGRATE_DIR/mem-migrate-0.2.0-release.json" "$MIGRATE_SHA" <<'PY'
import json, sys
output, sha = sys.argv[1:]
doc = {
    "schemaVersion": 1,
    "channel": "stable",
    "releaseVersion": "0.2.0",
    "runtime": "linux-x64",
    "archiveFileName": "mem-migrate-0.2.0-linux-x64.tar.gz",
    "archiveSha256": sha,
    "publishedAtUtc": "2026-09-05T00:00:00Z",
}
with open(output, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2, sort_keys=True)
    f.write("\n")
PY

python3 - "$MIGRATE_DIR" "$SOURCE_COMMIT" "$SOURCE_EPOCH" <<'PY'
import hashlib, json, os, sys
from pathlib import Path
root = Path(sys.argv[1])
commit, epoch = sys.argv[2:]

def record(name, artifact_id, kind):
    p = root / name
    h = hashlib.sha256(p.read_bytes()).hexdigest()
    return {
        "id": artifact_id,
        "kind": kind,
        "fileName": name,
        "sha256": h,
        "sizeBytes": p.stat().st_size,
    }

doc = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "component": "MEM Migrate",
        "version": "0.2.0",
        "channel": "stable",
    },
    "source": {
        "commit": commit,
        "sourceDateEpoch": int(epoch),
        "workingTreeDirty": False,
    },
    "build": {
        "generatedAtUtc": "2026-09-05T00:00:00Z",
        "releaseEligible": True,
        "runtime": "linux-x64",
    },
    "artifacts": [
        record("install-mem-migrate-0.2.0.sh", "migrate-bootstrap", "migrate-bootstrap-script"),
        record("mem-migrate-0.2.0-release.json", "migrate-manifest", "component-release-manifest"),
        record("mem-migrate-0.2.0-linux-x64.tar.gz", "migrate", "migrate-bundle"),
        record("mem-migrate-0.2.0-linux-x64.tar.gz.sha256", "migrate-checksum", "sha256-sidecar"),
    ],
}
with open(root / "mem-migrate-0.2.0-linux-x64.build-info.json", "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2, sort_keys=True)
    f.write("\n")
PY

SBOM="$TMP_ROOT/mem-0.2.0-sbom.spdx.json"
cat > "$SBOM" <<'EOF'
{
  "spdxVersion": "SPDX-2.3",
  "dataLicense": "CC0-1.0",
  "SPDXID": "SPDXRef-DOCUMENT",
  "name": "Message Easy Mode 0.2.0 fixture SBOM",
  "documentNamespace": "https://example.invalid/mem/0.2.0/fixture",
  "creationInfo": {
    "created": "2026-09-05T00:00:00Z",
    "creators": ["Tool: MEM fixture"]
  },
  "packages": []
}
EOF

ASSEMBLER="$REPO/scripts/release/assemble-product-release.sh"
VALIDATOR="$REPO/scripts/release/validate-release-manifest.py"
OUTPUT="$TMP_ROOT/output"

assemble() {
  "$ASSEMBLER" \
    --version 0.2.0 \
    --control-plane-image "$IMAGE" \
    --cli-dir "$CLI_DIR" \
    --migrate-dir "$MIGRATE_DIR" \
    --sbom "$SBOM" \
    --output-dir "$1"
}

assemble "$OUTPUT" >/dev/null

EXPECTED_FILES="$(cat <<'EOF'
SHA256SUMS
install-mem-0.2.0.sh
install-mem-migrate-0.2.0.sh
mem-0.2.0-sbom.spdx.json
mem-cli-0.2.0-linux-x64
mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz.sha256
mem-migrate-0.2.0-release.json
release.json
EOF
)"
ACTUAL_FILES="$(find "$OUTPUT" -maxdepth 1 -type f -printf '%f\n' | LC_ALL=C sort)"
if [[ "$ACTUAL_FILES" == "$EXPECTED_FILES" ]]; then
  pass "assembler emits the exact ten-file public release set"
else
  fail_test "assembler emits the exact ten-file public release set" "$ACTUAL_FILES"
fi

EXPECTED_CHECKSUM_ORDER="$(cat <<'EOF'
install-mem-0.2.0.sh
install-mem-migrate-0.2.0.sh
mem-0.2.0-sbom.spdx.json
mem-cli-0.2.0-linux-x64
mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz
mem-migrate-0.2.0-linux-x64.tar.gz.sha256
mem-migrate-0.2.0-release.json
release.json
EOF
)"
ACTUAL_CHECKSUM_ORDER="$(awk '{print $2}' "$OUTPUT/SHA256SUMS")"
if [[ "$ACTUAL_CHECKSUM_ORDER" == "$EXPECTED_CHECKSUM_ORDER" ]]; then
  pass "aggregate SHA256SUMS uses canonical bytewise filename order"
else
  fail_test "aggregate SHA256SUMS uses canonical bytewise filename order" "$ACTUAL_CHECKSUM_ORDER"
fi

if python3 "$VALIDATOR" "$OUTPUT/release.json" --assets-dir "$OUTPUT" >/dev/null; then
  pass "assembled release.json and SHA256SUMS validate against real output bytes"
else
  fail_test "assembled release.json and SHA256SUMS validate against real output bytes"
fi

if python3 - "$OUTPUT/release.json" "$SOURCE_COMMIT" "$IMAGE" <<'PY'
import json, sys
path, commit, image = sys.argv[1:]
doc = json.load(open(path, "r", encoding="utf-8"))
assert doc["release"]["generatedAtUtc"] == "2026-09-05T00:00:00Z"
assert doc["release"]["publication"] == {
    "provider": "github-releases",
    "repository": "https://github.com/message-easy-mode/mem-releases",
    "tag": "v0.2.0",
}
assert doc["sources"] == [{
    "id": "control-plane",
    "repository": "https://github.com/message-easy-mode/mem",
    "commit": commit,
}]
assert doc["controlPlane"]["image"]["reference"] == image
PY
then
  pass "manifest separates canonical release/source repositories and binds commit, tag, and image digest"
else
  fail_test "manifest separates canonical release/source repositories and binds commit, tag, and image digest"
fi

INSPECT="$TMP_ROOT/installer-inspect"
mkdir -p "$INSPECT"
tar -xzf "$OUTPUT/mem-installer-0.2.0-ubuntu-24.04-amd64.tar.gz" -C "$INSPECT"
if cmp -s "$CLI" "$INSPECT/mem-installer-0.2.0-ubuntu-24.04-amd64/bootstrap/cli/mem"; then
  pass "product assembler embeds byte-for-byte the canonical standalone CLI"
else
  fail_test "product assembler embeds byte-for-byte the canonical standalone CLI"
fi

BOOTSTRAP="$OUTPUT/install-mem-0.2.0.sh"
if grep -Fq "TAG='v0.2.0'" "$BOOTSTRAP" &&
   grep -Fq "RELEASE_REPOSITORY='message-easy-mode/mem-releases'" "$BOOTSTRAP" &&
   grep -Fq "SOURCE_REPOSITORY='message-easy-mode/mem'" "$BOOTSTRAP" &&
   grep -Fq 'BASE_URL="https://github.com/${RELEASE_REPOSITORY}/releases/download/${TAG}"' "$BOOTSTRAP" &&
   ! grep -Eq 'releases/(latest|stable)|:stable|:dev' "$BOOTSTRAP"; then
  pass "public bootstrap is pinned to mem-releases and exact v0.2.0 release identity"
else
  fail_test "public bootstrap is pinned to mem-releases and exact v0.2.0 release identity"
fi

if ! grep -Fq 'registry.vs4.one' "$BOOTSTRAP" &&
   ! grep -Fq 'registry.vs4.one' "$OUTPUT/release.json" &&
   ! grep -RFIq 'registry.vs4.one' "$INSPECT/mem-installer-0.2.0-ubuntu-24.04-amd64"; then
  pass "public bootstrap, manifest, and installer bundle do not advertise the private staging registry"
else
  fail_test "public bootstrap, manifest, and installer bundle do not advertise the private staging registry"
fi

(
  cd "$OUTPUT"
  sha256sum -c SHA256SUMS >/dev/null
) && pass "aggregate SHA256SUMS verifies every manifest/public artifact" ||
  fail_test "aggregate SHA256SUMS verifies every manifest/public artifact"

EXTRA="$TMP_ROOT/output-extra"
cp -a "$OUTPUT" "$EXTRA"
printf 'not public\n' > "$EXTRA/debug.txt"
expect_reject \
  "assets validator rejects unexpected public files" \
  "unexpected public files" \
  python3 "$VALIDATOR" "$EXTRA/release.json" --assets-dir "$EXTRA"

printf '\nProduct release assembly tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
[[ $FAILED -eq 0 ]]
