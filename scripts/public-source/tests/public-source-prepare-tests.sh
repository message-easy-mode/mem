#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
PUBLIC_SOURCE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
PREPARE="$PUBLIC_SOURCE_DIR/prepare-public-source.sh"
VALIDATOR="$PUBLIC_SOURCE_DIR/validate-public-source-manifest.py"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-public-source-tests.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT

pass() {
  printf 'PASS: %s\n' "$1"
  PASSED=$((PASSED + 1))
}

fail_test() {
  printf 'FAIL: %s\n' "$1" >&2
  [[ $# -lt 2 ]] || printf '  %s\n' "$2" >&2
  FAILED=$((FAILED + 1))
}

new_repo() {
  local repo="$1"
  mkdir -p "$repo"
  git -C "$repo" init -q
  git -C "$repo" config user.name "MEM Public Source Test"
  git -C "$repo" config user.email "public-source-test@example.invalid"

  mkdir -p \
    "$repo/bootstrap" \
    "$repo/cli" \
    "$repo/dev" \
    "$repo/images" \
    "$repo/installer/.vscode" \
    "$repo/installer/src/.vscode" \
    "$repo/migrate" \
    "$repo/scripts/release" \
    "$repo/.github/workflows" \
    "$repo/.mem-slice-backups" \
    "$repo/.vscode" \
    "$repo/legacy"

  printf '# fixture\n' > "$repo/README.md"
  printf 'license\n' > "$repo/LICENSE.txt"
  printf '*.tmp\n' > "$repo/.gitignore"
  printf 'bootstrap\n' > "$repo/bootstrap/README.md"
  printf 'cli\n' > "$repo/cli/README.md"
  printf '#!/usr/bin/env bash\necho dev\n' > "$repo/dev/mem-env"
  chmod 0755 "$repo/dev/mem-env"
  printf 'image-source\n' > "$repo/images/README.md"
  printf 'installer\n' > "$repo/installer/README.md"
  printf '{\"editor.formatOnSave\": true}\n' > "$repo/installer/.vscode/settings.json"
  printf '{\"configurations\": []}\n' > "$repo/installer/src/.vscode/launch.json"
  printf 'migrate\n' > "$repo/migrate/README.md"
  printf '#!/usr/bin/env bash\necho release\n' > "$repo/scripts/release/build.sh"
  chmod 0755 "$repo/scripts/release/build.sh"
  printf 'workflow\n' > "$repo/.github/workflows/internal.yml"
  printf 'slice-backup\n' > "$repo/.mem-slice-backups/example.txt"
  printf '{}\n' > "$repo/.vscode/settings.json"
  printf 'legacy\n' > "$repo/legacy/README.md"
  printf '#!/usr/bin/env bash\necho install\n' > "$repo/install.sh"
  chmod 0755 "$repo/install.sh"
  printf '#!/usr/bin/env bash\necho slice\n' > "$repo/apply-slice.sh"
  chmod 0755 "$repo/apply-slice.sh"

  git -C "$repo" add -A
  git -C "$repo" commit -qm "fixture"
}

run_prepare() {
  local repo="$1" out="$2" version="${3:-0.2.0}"
  local commit
  commit="$(git -C "$repo" rev-parse HEAD)"
  "$PREPARE" \
    --repo-root "$repo" \
    --version "$version" \
    --source-commit "$commit" \
    --output-dir "$out"
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

# Happy-path export, exclusion, archive/checksum, manifest validation.
repo="$TMP_ROOT/happy-repo"
out="$TMP_ROOT/happy-out"
new_repo "$repo"
if run_prepare "$repo" "$out" >/dev/null; then
  if [[ -f "$out/source/installer/README.md" \
        && -f "$out/source/scripts/release/build.sh" \
        && -f "$out/source/install.sh" \
        && ! -e "$out/source/installer/.vscode" \
        && ! -e "$out/source/installer/src/.vscode" \
        && ! -e "$out/source/.github" \
        && ! -e "$out/source/.mem-slice-backups" \
        && ! -e "$out/source/.vscode" \
        && ! -e "$out/source/legacy" \
        && ! -e "$out/source/apply-slice.sh" ]]; then
    pass "explicit top-level and nested-exclusion policy produces the intended public tree"
  else
    fail_test "explicit top-level and nested-exclusion policy produces the intended public tree"
  fi
else
  fail_test "happy-path public source preparation succeeds"
fi

if python3 "$VALIDATOR" "$out/public-source.json" >/dev/null && (cd "$out" && sha256sum -c SHA256SUMS >/dev/null); then
  pass "manifest and aggregate checksums validate"
else
  fail_test "manifest and aggregate checksums validate"
fi

if [[ "$(stat -c '%a' "$out/source/dev/mem-env")" == "755" && "$(stat -c '%a' "$out/source/README.md")" == "644" ]]; then
  pass "Git executable semantics are preserved in the review tree"
else
  fail_test "Git executable semantics are preserved in the review tree"
fi

# Determinism.
out2="$TMP_ROOT/happy-out-2"
if run_prepare "$repo" "$out2" >/dev/null \
  && cmp -s "$out/mem-0.2.0-source.tar.gz" "$out2/mem-0.2.0-source.tar.gz" \
  && cmp -s "$out/public-source.json" "$out2/public-source.json" \
  && cmp -s "$out/evidence/file-manifest.tsv" "$out2/evidence/file-manifest.tsv"; then
  pass "same commit and policy produce byte-identical archive and evidence"
else
  fail_test "same commit and policy produce byte-identical archive and evidence"
fi

# Caller locale / collation must not affect producer ordering. This wrapper
# deliberately reverses sort output unless the producer has pinned LC_ALL=C.
locale_repo="$TMP_ROOT/locale-repo"
locale_out="$TMP_ROOT/locale-out"
locale_bin="$TMP_ROOT/locale-bin"
new_repo "$locale_repo"
mkdir -p "$locale_bin"
real_sort="$(command -v sort)"
cat > "$locale_bin/sort" <<EOF_SORT_WRAPPER
#!/usr/bin/env bash
set -euo pipefail
if [[ "\${LC_ALL:-}" != "C" ]]; then
  exec "$real_sort" -r "\$@"
fi
exec "$real_sort" "\$@"
EOF_SORT_WRAPPER
chmod 0755 "$locale_bin/sort"
locale_commit="$(git -C "$locale_repo" rev-parse HEAD)"
if LC_ALL=POSIX PATH="$locale_bin:$PATH" \
  "$PREPARE" \
    --repo-root "$locale_repo" \
    --version 0.2.0 \
    --source-commit "$locale_commit" \
    --output-dir "$locale_out" >/dev/null \
  && python3 "$VALIDATOR" "$locale_out/public-source.json" >/dev/null; then
  pass "caller locale cannot change public-source manifest ordering"
else
  fail_test "caller locale cannot change public-source manifest ordering"
fi

# Dirty source rejection.
dirty_repo="$TMP_ROOT/dirty-repo"
new_repo "$dirty_repo"
printf 'dirty\n' >> "$dirty_repo/README.md"
dirty_commit="$(git -C "$dirty_repo" rev-parse HEAD)"
expect_reject \
  "dirty source tree is rejected" \
  "worktree is dirty" \
  "$PREPARE" --repo-root "$dirty_repo" --version 0.2.0 --source-commit "$dirty_commit" --output-dir "$TMP_ROOT/dirty-out"

# Exact selected commit must equal HEAD.
commit_repo="$TMP_ROOT/commit-repo"
new_repo "$commit_repo"
old_commit="$(git -C "$commit_repo" rev-parse HEAD)"
printf 'second\n' >> "$commit_repo/README.md"
git -C "$commit_repo" add README.md
git -C "$commit_repo" commit -qm second
expect_reject \
  "selected source commit must equal HEAD" \
  "does not match repository HEAD" \
  "$PREPARE" --repo-root "$commit_repo" --version 0.2.0 --source-commit "$old_commit" --output-dir "$TMP_ROOT/commit-out"

# Unknown top-level paths fail closed.
unknown_repo="$TMP_ROOT/unknown-repo"
new_repo "$unknown_repo"
mkdir -p "$unknown_repo/private-notes"
printf 'not classified\n' > "$unknown_repo/private-notes/README.md"
git -C "$unknown_repo" add private-notes/README.md
git -C "$unknown_repo" commit -qm unknown
unknown_commit="$(git -C "$unknown_repo" rev-parse HEAD)"
expect_reject \
  "unclassified tracked top-level path is rejected" \
  "not classified" \
  "$PREPARE" --repo-root "$unknown_repo" --version 0.2.0 --source-commit "$unknown_commit" --output-dir "$TMP_ROOT/unknown-out"


# A publication-inappropriate nested path is rejected unless it is explicitly
# reviewed in the nested-exclusion policy.
nested_repo="$TMP_ROOT/nested-repo"
new_repo "$nested_repo"
empty_exclusions="$TMP_ROOT/empty-path-exclusions.txt"
: > "$empty_exclusions"
nested_commit="$(git -C "$nested_repo" rev-parse HEAD)"
expect_reject \
  "unreviewed nested .vscode path is rejected" \
  "Publication-inappropriate tracked path is included: installer/.vscode" \
  "$PREPARE" --repo-root "$nested_repo" --version 0.2.0 --source-commit "$nested_commit" --output-dir "$TMP_ROOT/nested-out" --path-exclusions "$empty_exclusions"

# The second reviewed nested editor path is independently fail-closed. Keep
# only installer/.vscode approved and prove installer/src/.vscode is rejected.
nested_src_repo="$TMP_ROOT/nested-src-repo"
new_repo "$nested_src_repo"
first_only_exclusions="$TMP_ROOT/first-only-path-exclusions.txt"
printf 'installer/.vscode/\n' > "$first_only_exclusions"
nested_src_commit="$(git -C "$nested_src_repo" rev-parse HEAD)"
expect_reject \
  "unreviewed installer source .vscode path is rejected" \
  "Publication-inappropriate tracked path is included: installer/src/.vscode" \
  "$PREPARE" \
    --repo-root "$nested_src_repo" \
    --version 0.2.0 \
    --source-commit "$nested_src_commit" \
    --output-dir "$TMP_ROOT/nested-src-out" \
    --path-exclusions "$first_only_exclusions"

# Obvious local environment/state file fails even inside an included root.
forbidden_repo="$TMP_ROOT/forbidden-repo"
new_repo "$forbidden_repo"
printf 'TOKEN=example\n' > "$forbidden_repo/installer/.env"
git -C "$forbidden_repo" add -f installer/.env
git -C "$forbidden_repo" commit -qm forbidden
forbidden_commit="$(git -C "$forbidden_repo" rev-parse HEAD)"
expect_reject \
  "tracked .env inside an included root is rejected" \
  "Local environment file" \
  "$PREPARE" --repo-root "$forbidden_repo" --version 0.2.0 --source-commit "$forbidden_commit" --output-dir "$TMP_ROOT/forbidden-out"

# Symlinks fail rather than being exported to public source.
symlink_repo="$TMP_ROOT/symlink-repo"
new_repo "$symlink_repo"
ln -s ../README.md "$symlink_repo/installer/readme-link"
git -C "$symlink_repo" add installer/readme-link
git -C "$symlink_repo" commit -qm symlink
symlink_commit="$(git -C "$symlink_repo" rev-parse HEAD)"
expect_reject \
  "tracked symlink in included source is rejected" \
  "may not contain symlinks" \
  "$PREPARE" --repo-root "$symlink_repo" --version 0.2.0 --source-commit "$symlink_commit" --output-dir "$TMP_ROOT/symlink-out"

# Output must be new.
existing_repo="$TMP_ROOT/existing-repo"
new_repo "$existing_repo"
mkdir -p "$TMP_ROOT/existing-out"
existing_commit="$(git -C "$existing_repo" rev-parse HEAD)"
expect_reject \
  "existing output directory is never overwritten" \
  "already exists" \
  "$PREPARE" --repo-root "$existing_repo" --version 0.2.0 --source-commit "$existing_commit" --output-dir "$TMP_ROOT/existing-out"

printf '\nPublic-source prepare tests: %s passed, %s failed\n' "$PASSED" "$FAILED"
[[ $FAILED -eq 0 ]]
