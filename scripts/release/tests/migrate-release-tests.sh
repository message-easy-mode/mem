#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
BUILDER="$RELEASE_ROOT/build-migrate-release.sh"
SUITE="$(mktemp -d "${TMPDIR:-/tmp}/mem-migrate-root-release-tests.XXXXXX")"
trap 'rm -rf -- "$SUITE"' EXIT INT TERM

PASSED=0
FAILED=0
pass() { PASSED=$((PASSED + 1)); printf 'PASS: %s\n' "$1"; }
fail_test() { FAILED=$((FAILED + 1)); printf 'FAIL: %s\n' "$1" >&2; [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2; }

make_component() {
  local root="$1"
  local version="$2"
  local channel="$3"
  local archive="mem-migrate-${version}-linux-x64.tar.gz"
  mkdir -p "$root"
  printf 'fixture migrate archive %s\n' "$version" > "$root/$archive"
  local sha
  sha="$(sha256sum "$root/$archive" | awk '{print $1}')"
  printf '%s  %s\n' "$sha" "$archive" > "$root/$archive.sha256"
  cat > "$root/install-mem-migrate.sh" <<'SH'
#!/usr/bin/env bash
exit 0
SH
  chmod 0755 "$root/install-mem-migrate.sh"
  python3 - "$root/release.json" "$version" "$channel" "$archive" "$sha" <<'PY'
import json, sys
path, version, channel, archive, sha = sys.argv[1:]
with open(path, "w", encoding="utf-8") as h:
    json.dump({
        "schemaVersion": 1,
        "channel": channel,
        "releaseVersion": version,
        "runtime": "linux-x64",
        "archiveFileName": archive,
        "archiveSha256": sha,
        "publishedAtUtc": "2026-09-05T00:00:00Z",
    }, h, indent=2)
    h.write("\n")
PY
}

component="$SUITE/component"
output="$SUITE/output"
make_component "$component" "0.2.0" "stable"

if "$BUILDER" \
    --version 0.2.0 \
    --component-release-dir "$component" \
    --output-dir "$output" >/dev/null; then
  pass "fixture component release maps to unified public Migrate assets"
else
  fail_test "fixture component release maps to unified public Migrate assets"
fi

for expected in \
  install-mem-migrate-0.2.0.sh \
  mem-migrate-0.2.0-release.json \
  mem-migrate-0.2.0-linux-x64.tar.gz \
  mem-migrate-0.2.0-linux-x64.tar.gz.sha256 \
  mem-migrate-0.2.0-linux-x64.build-info.json; do
  if [[ -f "$output/$expected" ]]; then
    pass "producer emits $expected"
  else
    fail_test "producer emits $expected"
  fi
done

if cmp -s "$component/mem-migrate-0.2.0-linux-x64.tar.gz" "$output/mem-migrate-0.2.0-linux-x64.tar.gz"; then
  pass "unified release preserves exact Migrate archive bytes"
else
  fail_test "unified release preserves exact Migrate archive bytes"
fi

if python3 - "$output/mem-migrate-0.2.0-linux-x64.build-info.json" <<'PY'
import json, sys
with open(sys.argv[1], "r", encoding="utf-8") as h:
    doc=json.load(h)
assert doc["product"]["version"] == "0.2.0"
assert doc["product"]["channel"] == "stable"
assert doc["build"]["releaseEligible"] is False
ids={a["id"] for a in doc["artifacts"]}
assert ids == {"migrate-bootstrap","migrate-manifest","migrate","migrate-checksum"}
PY
then
  pass "Migrate producer evidence records exact version and non-eligible fixture proof"
else
  fail_test "Migrate producer evidence records exact version and non-eligible fixture proof"
fi

bad="$SUITE/bad"
make_component "$bad" "0.2.1" "stable"
set +e
result="$("$BUILDER" --version 0.2.0 --component-release-dir "$bad" --output-dir "$SUITE/bad-output" 2>&1)"
status=$?
set -e
if [[ $status -ne 0 && "$result" == *"component release manifest mismatch"* ]]; then
  pass "producer rejects component release version mismatch"
else
  fail_test "producer rejects component release version mismatch" "$result"
fi

corrupt="$SUITE/corrupt"
make_component "$corrupt" "0.2.0" "stable"
printf 'corruption\n' >> "$corrupt/mem-migrate-0.2.0-linux-x64.tar.gz"
set +e
result="$("$BUILDER" --version 0.2.0 --component-release-dir "$corrupt" --output-dir "$SUITE/corrupt-output" 2>&1)"
status=$?
set -e
if [[ $status -ne 0 && "$result" == *"checksum sidecar does not match"* ]]; then
  pass "producer rejects corrupted component archive"
else
  fail_test "producer rejects corrupted component archive" "$result"
fi

set +e
result="$("$BUILDER" --version dev --component-release-dir "$component" --output-dir "$SUITE/dev-output" 2>&1)"
status=$?
set -e
if [[ $status -ne 0 && "$result" == *"semantic version"* ]]; then
  pass "producer rejects dev as a public Migrate release version"
else
  fail_test "producer rejects dev as a public Migrate release version" "$result"
fi

printf '\nMigrate public release tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
[[ "$FAILED" -eq 0 ]]
