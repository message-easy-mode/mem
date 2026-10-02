#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
export LC_ALL=C

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
PUBLIC_SOURCE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
PREPARE="$PUBLIC_SOURCE_DIR/prepare-public-source.sh"
VERIFY="$PUBLIC_SOURCE_DIR/verify-public-source.sh"
AUDIT="$PUBLIC_SOURCE_DIR/audit-public-source.py"
ALLOWLIST="$PUBLIC_SOURCE_DIR/public-source-content-allowlist.tsv"

for cmd in git python3 mktemp rm mkdir chmod grep; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "ERROR: missing test dependency: $cmd" >&2
    exit 1
  }
done

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-public-source-verification-tests.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT
EMPTY_ALLOWLIST="$TMP_ROOT/empty-allowlist.tsv"
: > "$EMPTY_ALLOWLIST"

PASS_COUNT=0
FAIL_COUNT=0

pass() {
  PASS_COUNT=$((PASS_COUNT + 1))
  printf 'PASS: %s\n' "$1"
}

fail_test() {
  FAIL_COUNT=$((FAIL_COUNT + 1))
  printf 'FAIL: %s\n' "$1" >&2
}

expect_success() {
  local label="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    pass "$label"
  else
    fail_test "$label"
  fi
}

expect_failure() {
  local label="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    fail_test "$label"
  else
    pass "$label"
  fi
}

init_repo() {
  local repo="$1"
  mkdir -p "$repo"
  git -C "$repo" init -q
  git -C "$repo" config user.name "MEM Public Source Test"
  git -C "$repo" config user.email "public-source-test@example.invalid"
}

commit_repo() {
  local repo="$1"
  git -C "$repo" add -A
  git -C "$repo" commit -qm "fixture"
  git -C "$repo" rev-parse HEAD
}

write_policy() {
  local path="$1"
  shift
  : > "$path"
  for root in "$@"; do
    printf 'include\t%s\n' "$root" >> "$path"
  done
}

prepare_repo() {
  local repo="$1"
  local output="$2"
  local policy="$3"
  local exclusions="$4"
  local commit
  commit="$(git -C "$repo" rev-parse HEAD)"
  "$PREPARE" \
    --version 0.2.0 \
    --source-commit "$commit" \
    --output-dir "$output" \
    --repo-root "$repo" \
    --policy "$policy" \
    --path-exclusions "$exclusions" >/dev/null
}

run_audit() {
  local prepared="$1"
  local report="$2"
  local summary="$3"
  local allowlist="${4:-$EMPTY_ALLOWLIST}"
  "$AUDIT" \
    --source-root "$prepared/source" \
    --manifest "$prepared/public-source.json" \
    --file-manifest "$prepared/evidence/file-manifest.tsv" \
    --allowlist "$allowlist" \
    --report "$report" \
    --summary "$summary"
}

make_single_file_prepared() {
  local name="$1"
  local content="$2"
  local out="$TMP_ROOT/$name-audit"
  python3 - "$out" "$content" <<'PY_FAST_FIXTURE'
from __future__ import annotations
import hashlib, json, os, pathlib, sys
out=pathlib.Path(sys.argv[1])
content=sys.argv[2] + "\n"
source=out/"source"/"fixture"
source.mkdir(parents=True, exist_ok=True)
path=source/"value.txt"
path.write_text(content, encoding="utf-8")
os.chmod(path, 0o644)
data=path.read_bytes()
line=f"{hashlib.sha256(data).hexdigest()}\t{len(data)}\t0644\tfixture/value.txt\n"
evidence=out/"evidence"
evidence.mkdir(parents=True, exist_ok=True)
fm=evidence/"file-manifest.tsv"
fm.write_text(line, encoding="utf-8")
sha=hashlib.sha256(line.encode()).hexdigest()
doc={
  "schemaVersion":1,
  "source":{"commit":"0"*40,"tree":"1"*40,"sourceDateEpoch":0,"workingTreeDirty":False},
  "contents":{"fileCount":1,"treeSha256":sha},
}
(out/"public-source.json").write_text(json.dumps(doc, indent=2, sort_keys=True)+"\n", encoding="utf-8")
PY_FAST_FIXTURE
  printf '%s\n' "$out"
}

make_verifier_single_file_prepared() {
  local name="$1"
  local content="$2"
  local out="$TMP_ROOT/$name-complete"
  python3 - "$out" "$content" <<'PY_COMPLETE_SINGLE'
from __future__ import annotations
import hashlib, json, os, pathlib, sys
out=pathlib.Path(sys.argv[1])
content=sys.argv[2] + "\n"
source=out/"source"/"fixture"
source.mkdir(parents=True, exist_ok=True)
path=source/"value.txt"
path.write_text(content, encoding="utf-8")
os.chmod(path, 0o644)

def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

data=path.read_bytes()
line=f"{digest(data)}\t{len(data)}\t0644\tfixture/value.txt\n"
evidence=out/"evidence"
evidence.mkdir(parents=True, exist_ok=True)
fm=evidence/"file-manifest.tsv"
fm.write_text(line, encoding="utf-8")
archive=out/"mem-0.2.0-source.tar.gz"
archive.write_bytes(b"fixture archive\n")
fm_bytes=fm.read_bytes()
archive_bytes=archive.read_bytes()
manifest={
  "schemaVersion":1,
  "product":{"id":"mem","name":"Message Easy Mode","version":"0.2.0"},
  "source":{"commit":"0"*40,"tree":"1"*40,"sourceDateEpoch":0,"workingTreeDirty":False},
  "export":{
    "format":"mem-public-source-v1",
    "generatedAtUtc":"1970-01-01T00:00:00Z",
    "policy":{"path":"scripts/public-source/public-source-top-level-policy.tsv","sha256":"2"*64},
    "pathExclusions":{"path":"scripts/public-source/public-source-path-exclusions.txt","sha256":"3"*64},
    "includedTopLevel":["fixture"],
    "excludedTopLevel":[],
    "excludedPaths":[],
  },
  "contents":{"fileCount":1,"treeSha256":digest(fm_bytes)},
  "artifacts":{
    "archive":{"fileName":archive.name,"sha256":digest(archive_bytes),"sizeBytes":len(archive_bytes)},
    "fileManifest":{"fileName":"evidence/file-manifest.tsv","sha256":digest(fm_bytes),"sizeBytes":len(fm_bytes)},
  },
}
manifest_path=out/"public-source.json"
manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True)+"\n", encoding="utf-8")
checks=[
  (digest(archive.read_bytes()), archive.name),
  (digest(fm.read_bytes()), "evidence/file-manifest.tsv"),
  (digest(manifest_path.read_bytes()), "public-source.json"),
]
(out/"SHA256SUMS").write_text("".join(f"{d}  {n}\n" for d,n in checks), encoding="utf-8")
PY_COMPLETE_SINGLE
  printf '%s\n' "$out"
}

fabricate_complete_prepared_tree() {
  local source_dir="$1"
  local out="$2"
  python3 - "$source_dir" "$out" <<'PY_COMPLETE_TREE'
from __future__ import annotations
import hashlib, json, os, pathlib, shutil, sys
src=pathlib.Path(sys.argv[1])
out=pathlib.Path(sys.argv[2])
root=out/"source"
shutil.copytree(src, root)

def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

records=[]
for path in sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p:p.relative_to(root).as_posix()):
    rel=path.relative_to(root).as_posix()
    data=path.read_bytes()
    mode="0755" if path.stat().st_mode & 0o100 else "0644"
    os.chmod(path, 0o755 if mode=="0755" else 0o644)
    records.append(f"{digest(data)}\t{len(data)}\t{mode}\t{rel}\n")
evidence=out/"evidence"; evidence.mkdir(parents=True, exist_ok=True)
fm=evidence/"file-manifest.tsv"; fm.write_text("".join(records), encoding="utf-8")
archive=out/"mem-0.2.0-source.tar.gz"; archive.write_bytes(b"fixture archive\n")
tops=sorted({p.split('/',1)[0] for p in [r.split('\t',3)[3].rstrip('\n') for r in records]})
fm_bytes=fm.read_bytes(); archive_bytes=archive.read_bytes()
manifest={
  "schemaVersion":1,
  "product":{"id":"mem","name":"Message Easy Mode","version":"0.2.0"},
  "source":{"commit":"0"*40,"tree":"1"*40,"sourceDateEpoch":0,"workingTreeDirty":False},
  "export":{
    "format":"mem-public-source-v1",
    "generatedAtUtc":"1970-01-01T00:00:00Z",
    "policy":{"path":"scripts/public-source/public-source-top-level-policy.tsv","sha256":"2"*64},
    "pathExclusions":{"path":"scripts/public-source/public-source-path-exclusions.txt","sha256":"3"*64},
    "includedTopLevel":tops,
    "excludedTopLevel":[],
    "excludedPaths":[],
  },
  "contents":{"fileCount":len(records),"treeSha256":digest(fm_bytes)},
  "artifacts":{
    "archive":{"fileName":archive.name,"sha256":digest(archive_bytes),"sizeBytes":len(archive_bytes)},
    "fileManifest":{"fileName":"evidence/file-manifest.tsv","sha256":digest(fm_bytes),"sizeBytes":len(fm_bytes)},
  },
}
manifest_path=out/"public-source.json"; manifest_path.write_text(json.dumps(manifest,indent=2,sort_keys=True)+"\n",encoding="utf-8")
checks=[(digest(archive.read_bytes()),archive.name),(digest(fm.read_bytes()),"evidence/file-manifest.tsv"),(digest(manifest_path.read_bytes()),"public-source.json")]
(out/"SHA256SUMS").write_text("".join(f"{d}  {n}\n" for d,n in checks),encoding="utf-8")
PY_COMPLETE_TREE
}

# Clean scan and manifest integrity.
CLEAN_OUT="$(make_single_file_prepared clean 'ordinary public source text')"
expect_success \
  "clean prepared source passes exposure audit" \
  run_audit "$CLEAN_OUT" "$TMP_ROOT/clean.tsv" "$TMP_ROOT/clean.json"

if python3 - "$TMP_ROOT/clean.json" <<'PY' >/dev/null
import json,sys
x=json.load(open(sys.argv[1]))
assert x["status"] == "pass" and x["blockFindings"] == 0
PY
then
  pass "clean scan summary reports zero blockers"
else
  fail_test "clean scan summary reports zero blockers"
fi

# A post-preparation mutation must fail before content scanning.
printf 'mutated\n' >> "$CLEAN_OUT/source/fixture/value.txt"
expect_failure \
  "prepared source mutation is rejected against file-manifest evidence" \
  run_audit "$CLEAN_OUT" "$TMP_ROOT/mutated.tsv" "$TMP_ROOT/mutated.json"

# High-confidence blocker classes.
BAD_HOME="/home/"'master'"/Code/private-project"
HOME_OUT="$(make_single_file_prepared home "$BAD_HOME")"
expect_failure \
  "personal workstation home path is blocking" \
  run_audit "$HOME_OUT" "$TMP_ROOT/home.tsv" "$TMP_ROOT/home.json"

grep -q $'BLOCK\tpersonal-home-master\t' "$TMP_ROOT/home.tsv" \
  && pass "personal-home finding is classified by rule id" \
  || fail_test "personal-home finding is classified by rule id"

BAD_LEGACY="MatrixEasyMode"'/Internal/'"matrix-easy-mode-deploy"
LEGACY_OUT="$(make_single_file_prepared legacy-path "$BAD_LEGACY")"
expect_failure \
  "legacy private repository identity is blocking" \
  run_audit "$LEGACY_OUT" "$TMP_ROOT/legacy.tsv" "$TMP_ROOT/legacy.json"

BAD_REPO="https://github.com/message-easy-mode/"'mem-control-plane'
REPO_OUT="$(make_single_file_prepared obsolete-repo "$BAD_REPO")"
expect_failure \
  "obsolete GitHub source repository identity is blocking" \
  run_audit "$REPO_OUT" "$TMP_ROOT/repo.tsv" "$TMP_ROOT/repo.json"

PRIVATE_KEY="-----BEGIN "'PRIVATE KEY'"-----"
KEY_OUT="$(make_single_file_prepared private-key "$PRIVATE_KEY")"
expect_failure \
  "private-key header is blocking" \
  run_audit "$KEY_OUT" "$TMP_ROOT/key.tsv" "$TMP_ROOT/key.json"

GITHUB_TOKEN="ghp_"'0123456789ABCDEFGHIJKL'
TOKEN_OUT="$(make_single_file_prepared github-token "$GITHUB_TOKEN")"
expect_failure \
  "GitHub token shape is blocking" \
  run_audit "$TOKEN_OUT" "$TMP_ROOT/token.tsv" "$TMP_ROOT/token.json"

AWS_KEY="AKIA"'ABCDEFGHIJKLMNOP'
AWS_OUT="$(make_single_file_prepared aws-key "$AWS_KEY")"
expect_failure \
  "AWS access-key shape is blocking" \
  run_audit "$AWS_OUT" "$TMP_ROOT/aws.tsv" "$TMP_ROOT/aws.json"

NPM_TOKEN="//registry.npmjs.org/:_auth"'Token=fixture-secret-value'
NPM_OUT="$(make_single_file_prepared npm-token "$NPM_TOKEN")"
expect_failure \
  "npm auth-token assignment is blocking" \
  run_audit "$NPM_OUT" "$TMP_ROOT/npm.tsv" "$TMP_ROOT/npm.json"

# Private addresses are review findings, not automatic failures.
RFC_OUT="$(make_single_file_prepared rfc1918 'example address 10.20.30.40')"
expect_success \
  "RFC1918 address is surfaced for review without automatic rejection" \
  run_audit "$RFC_OUT" "$TMP_ROOT/rfc.tsv" "$TMP_ROOT/rfc.json"
if python3 - "$TMP_ROOT/rfc.json" <<'PY' >/dev/null
import json,sys
x=json.load(open(sys.argv[1]))
assert x["blockFindings"] == 0 and x["reviewFindings"] == 1
PY
then
  pass "RFC1918 review count is recorded"
else
  fail_test "RFC1918 review count is recorded"
fi

# Exact reviewed allowlist support and stale-exemption rejection.
ALLOW="$TMP_ROOT/home-allow.tsv"
printf 'personal-home-master\tfixture/value.txt\n' > "$ALLOW"
expect_success \
  "exact rule/path allowlist can suppress a reviewed blocker" \
  run_audit "$HOME_OUT" "$TMP_ROOT/home-allowed.tsv" "$TMP_ROOT/home-allowed.json" "$ALLOW"

STALE_CLEAN_OUT="$(make_single_file_prepared stale-clean 'ordinary public source text')"
STALE_ALLOW="$TMP_ROOT/stale-allow.tsv"
printf 'personal-home-master\tfixture/other.txt\n' > "$STALE_ALLOW"
expect_failure \
  "stale content allowlist entries fail closed" \
  run_audit "$STALE_CLEAN_OUT" "$TMP_ROOT/stale.tsv" "$TMP_ROOT/stale.json" "$STALE_ALLOW"

# verify-public-source scan-only orchestration.
CLEAN_SCAN_OUT="$(make_verifier_single_file_prepared verify-clean 'ordinary public source text')"
expect_success \
  "scan-only verifier validates prepared artifact and writes evidence" \
  "$VERIFY" \
    --prepared-dir "$CLEAN_SCAN_OUT" \
    --output-dir "$TMP_ROOT/verify-clean-evidence" \
    --allowlist "$EMPTY_ALLOWLIST" \
    --scan-only
[[ -f "$TMP_ROOT/verify-clean-evidence/verification.json" ]] \
  && pass "scan-only verification.json is produced" \
  || fail_test "scan-only verification.json is produced"

BLOCK_SCAN_OUT="$(make_verifier_single_file_prepared verify-block "$BAD_HOME")"
expect_failure \
  "scan-only verifier stops before build when blocker is present" \
  "$VERIFY" \
    --prepared-dir "$BLOCK_SCAN_OUT" \
    --output-dir "$TMP_ROOT/verify-block-evidence" \
    --allowlist "$EMPTY_ALLOWLIST" \
    --scan-only
[[ -f "$TMP_ROOT/verify-block-evidence/exposure-findings.tsv" ]] \
  && pass "failed scan retains exposure findings" \
  || fail_test "failed scan retains exposure findings"

# Full build orchestration with fake toolchain proves the verifier uses only its
# copied exported workspace and emits an auditable step list without publication.
FULL_SOURCE="$TMP_ROOT/full-source"
FULL_OUT="$TMP_ROOT/full-out"
mkdir -p \
  "$FULL_SOURCE/installer/src/Web" \
  "$FULL_SOURCE/migrate/src/Mem.Migrate.Web/ClientApp" \
  "$FULL_SOURCE/scripts/public-source/tests"
printf 'fixture\n' > "$FULL_SOURCE/installer/src/MemInstaller.sln"
printf '{}\n' > "$FULL_SOURCE/installer/src/Web/package.json"
printf '{}\n' > "$FULL_SOURCE/installer/src/Web/package-lock.json"
printf 'fixture\n' > "$FULL_SOURCE/migrate/Mem.Migrate.sln"
printf '{}\n' > "$FULL_SOURCE/migrate/src/Mem.Migrate.Web/ClientApp/package.json"
printf '{}\n' > "$FULL_SOURCE/migrate/src/Mem.Migrate.Web/ClientApp/package-lock.json"
cat > "$FULL_SOURCE/scripts/public-source/tests/public-source-prepare-tests.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
echo fixture-public-source-tests
EOF
chmod 0755 "$FULL_SOURCE/scripts/public-source/tests/public-source-prepare-tests.sh"
fabricate_complete_prepared_tree "$FULL_SOURCE" "$FULL_OUT"

FAKE_BIN="$TMP_ROOT/fake-bin"
FAKE_LOG="$TMP_ROOT/fake-tools.log"
mkdir -p "$FAKE_BIN"
cat > "$FAKE_BIN/dotnet" <<'EOF'
#!/usr/bin/env bash
printf 'dotnet\t%s\t%s\n' "$PWD" "$*" >> "$FAKE_TOOL_LOG"
exit 0
EOF
cat > "$FAKE_BIN/npm" <<'EOF'
#!/usr/bin/env bash
printf 'npm\t%s\t%s\n' "$PWD" "$*" >> "$FAKE_TOOL_LOG"
exit 0
EOF
chmod 0755 "$FAKE_BIN/dotnet" "$FAKE_BIN/npm"

if PATH="$FAKE_BIN:$PATH" FAKE_TOOL_LOG="$FAKE_LOG" \
  "$VERIFY" \
    --prepared-dir "$FULL_OUT" \
    --output-dir "$TMP_ROOT/full-evidence" \
    --allowlist "$EMPTY_ALLOWLIST" \
    --full >/dev/null 2>&1; then
  pass "full verifier orchestrates isolated build/test steps with a clean exported fixture"
else
  fail_test "full verifier orchestrates isolated build/test steps with a clean exported fixture"
fi

if python3 - "$TMP_ROOT/full-evidence/verification.json" <<'PY' >/dev/null
import json,sys
x=json.load(open(sys.argv[1]))
assert x["status"] == "pass" and x["mode"] == "full"
assert x["publicationPerformed"] is False
assert x["containerBuild"] is False
assert len(x["buildSteps"]) == 13
assert all(step["status"] == "pass" for step in x["buildSteps"])
PY
then
  pass "full verification records all expected build steps"
else
  fail_test "full verification records all expected build steps"
fi

PRIVATE_WORK_REPO="/home""/master""/Code/MEM/mem"
if grep -Fq "$PRIVATE_WORK_REPO" "$FAKE_LOG" 2>/dev/null; then
  fail_test "isolated build commands do not reference canonical private working repo"
else
  pass "isolated build commands do not reference canonical private working repo"
fi

printf '\nPublic-source verification tests: %d passed, %d failed\n' "$PASS_COUNT" "$FAIL_COUNT"
[[ "$FAIL_COUNT" -eq 0 ]]
