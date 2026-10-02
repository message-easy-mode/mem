#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
# Deterministic producer output must not depend on the operator's locale.
# The Python manifest validator uses code-point ordering, which matches C byte
# collation for the ASCII repository paths allowed by this producer.
export LC_ALL=C
umask 022

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
DEFAULT_REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
DEFAULT_POLICY="$SCRIPT_DIR/public-source-top-level-policy.tsv"
DEFAULT_PATH_EXCLUSIONS="$SCRIPT_DIR/public-source-path-exclusions.txt"
VALIDATOR="$SCRIPT_DIR/validate-public-source-manifest.py"

VERSION=""
SOURCE_COMMIT=""
OUTPUT_DIR=""
REPO_ROOT="$DEFAULT_REPO_ROOT"
POLICY_FILE="$DEFAULT_POLICY"
PATH_EXCLUSIONS_FILE="$DEFAULT_PATH_EXCLUSIONS"

usage() {
  cat <<'USAGE'
Prepare a deterministic, reviewable public-source snapshot for Message Easy Mode.

Usage:
  ./scripts/public-source/prepare-public-source.sh \
    --version 0.2.0 \
    --source-commit <full-git-commit> \
    --output-dir <new-directory> \
    [--repo-root <repository-root>] \
    [--policy <policy-file>] \
    [--path-exclusions <path-exclusions-file>]

The producer:
  * requires a clean Git worktree at the exact selected source commit;
  * classifies every tracked top-level path through an explicit include/exclude policy;
  * applies explicit reviewed nested-path exclusions inside approved roots;
  * fails closed when a tracked top-level path is unclassified;
  * exports Git-tracked bytes only from the selected commit;
  * rejects unsafe/publication-inappropriate tracked paths and symlinks;
  * emits a deterministic source archive, file manifest, checksums, and JSON evidence;
  * validates the resulting public-source manifest; and
  * performs no network access, Git commit, tag, remote write, or publication action.

The output directory must not already exist. If it is inside the source repository,
it must be ignored by Git so preparation cannot silently dirty release source.
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
    --version)
      [[ $# -ge 2 ]] || fail "--version requires a value."
      VERSION="$2"
      shift 2
      ;;
    --source-commit)
      [[ $# -ge 2 ]] || fail "--source-commit requires a value."
      SOURCE_COMMIT="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --repo-root)
      [[ $# -ge 2 ]] || fail "--repo-root requires a path."
      REPO_ROOT="$2"
      shift 2
      ;;
    --policy)
      [[ $# -ge 2 ]] || fail "--policy requires a path."
      POLICY_FILE="$2"
      shift 2
      ;;
    --path-exclusions)
      [[ $# -ge 2 ]] || fail "--path-exclusions requires a path."
      PATH_EXCLUSIONS_FILE="$2"
      shift 2
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

[[ -n "$VERSION" ]] || fail "--version is required."
[[ -n "$SOURCE_COMMIT" ]] || fail "--source-commit is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."

[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "--version must be an ordinary semantic version or prerelease version."
case "$VERSION" in
  *latest*|*stable*|*current*|*dev*)
    fail "Public source version may not contain a mutable channel/development label: $VERSION"
    ;;
esac

for command_name in git realpath mktemp rm mkdir mv cp chmod sha256sum python3 tar awk sort; do
  require_command "$command_name"
done

[[ -d "$REPO_ROOT" ]] || fail "Repository root does not exist: $REPO_ROOT"
[[ ! -L "$REPO_ROOT" ]] || fail "Repository root may not be a symlink: $REPO_ROOT"
REPO_ROOT="$(cd -- "$REPO_ROOT" && pwd -P)"

[[ -f "$POLICY_FILE" && ! -L "$POLICY_FILE" ]] || fail "Public-source policy is missing or unsafe: $POLICY_FILE"
POLICY_FILE="$(realpath -e -- "$POLICY_FILE")"
[[ -f "$PATH_EXCLUSIONS_FILE" && ! -L "$PATH_EXCLUSIONS_FILE" ]] || fail "Public-source path exclusions are missing or unsafe: $PATH_EXCLUSIONS_FILE"
PATH_EXCLUSIONS_FILE="$(realpath -e -- "$PATH_EXCLUSIONS_FILE")"
[[ -f "$VALIDATOR" && ! -L "$VALIDATOR" ]] || fail "Public-source manifest validator is missing: $VALIDATOR"

HEAD_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null)" ||
  fail "Repository root is not a readable Git worktree: $REPO_ROOT"
[[ "$SOURCE_COMMIT" =~ ^[0-9a-f]{40,64}$ ]] || fail "Invalid source commit: $SOURCE_COMMIT"
SELECTED_COMMIT="$(git -C "$REPO_ROOT" rev-parse "${SOURCE_COMMIT}^{commit}" 2>/dev/null)" ||
  fail "Selected source commit is not present in the repository: $SOURCE_COMMIT"
[[ "$SELECTED_COMMIT" == "$SOURCE_COMMIT" ]] || fail "--source-commit must be the full canonical commit id."
[[ "$SOURCE_COMMIT" == "$HEAD_COMMIT" ]] || fail "Selected source commit does not match repository HEAD."

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  fail "Repository worktree is dirty. Public-source preparation requires a clean committed source tree."
fi

SOURCE_TREE="$(git -C "$REPO_ROOT" rev-parse "${SOURCE_COMMIT}^{tree}")"
SOURCE_DATE_EPOCH="$(git -C "$REPO_ROOT" show -s --format=%ct "$SOURCE_COMMIT")"
[[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "Could not determine source commit timestamp."
GENERATED_AT_UTC="$(python3 - "$SOURCE_DATE_EPOCH" <<'PY'
from datetime import datetime, timezone
import sys
print(datetime.fromtimestamp(int(sys.argv[1]), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"))
PY
)"

OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
[[ "$OUTPUT_DIR" != "/" ]] || fail "Refusing to use filesystem root as output."
[[ "$OUTPUT_DIR" != "$REPO_ROOT" ]] || fail "Output directory may not be the repository root."
[[ ! -e "$OUTPUT_DIR" ]] || fail "Output directory already exists; refusing to overwrite it: $OUTPUT_DIR"

case "$OUTPUT_DIR/" in
  "$REPO_ROOT/"*)
    REL_OUTPUT="${OUTPUT_DIR#$REPO_ROOT/}"
    if ! git -C "$REPO_ROOT" check-ignore -q -- "$REL_OUTPUT"; then
      fail "Output inside the source repository must be covered by .gitignore: $REL_OUTPUT"
    fi
    ;;
esac

OUTPUT_PARENT="$(dirname -- "$OUTPUT_DIR")"
mkdir -p -- "$OUTPUT_PARENT"
[[ ! -L "$OUTPUT_PARENT" ]] || fail "Output parent may not be a symlink: $OUTPUT_PARENT"

# Parse the explicit top-level publication policy. Every selected commit top-level
# entry must be intentionally classified. This prevents a newly added internal
# repository root from silently becoming public.
declare -A POLICY_ACTION=()
while IFS=$'\t' read -r action path extra; do
  [[ -n "${action// }" ]] || continue
  [[ "$action" != \#* ]] || continue
  [[ -z "${extra:-}" ]] || fail "Malformed policy row (too many fields): $action $path $extra"
  [[ "$action" == "include" || "$action" == "exclude" ]] || fail "Unsupported policy action: $action"
  [[ -n "${path:-}" ]] || fail "Policy row is missing a top-level path."
  [[ "$path" != */* && "$path" != "." && "$path" != ".." ]] || fail "Policy paths must be single top-level names: $path"
  [[ -z "${POLICY_ACTION[$path]+x}" ]] || fail "Duplicate public-source policy path: $path"
  POLICY_ACTION["$path"]="$action"
done < "$POLICY_FILE"

INCLUDED_TOP_LEVEL=()
EXCLUDED_TOP_LEVEL=()
UNKNOWN_TOP_LEVEL=()
while IFS= read -r -d '' top_level; do
  action="${POLICY_ACTION[$top_level]:-}"
  case "$action" in
    include) INCLUDED_TOP_LEVEL+=("$top_level") ;;
    exclude) EXCLUDED_TOP_LEVEL+=("$top_level") ;;
    "") UNKNOWN_TOP_LEVEL+=("$top_level") ;;
    *) fail "Internal policy classification error for: $top_level" ;;
  esac
done < <(git -C "$REPO_ROOT" ls-tree -z --name-only "$SOURCE_COMMIT")

if [[ ${#UNKNOWN_TOP_LEVEL[@]} -gt 0 ]]; then
  printf 'ERROR: Tracked top-level paths are not classified by %s:\n' "$POLICY_FILE" >&2
  printf '  %s\n' "${UNKNOWN_TOP_LEVEL[@]}" >&2
  printf 'Classify each path explicitly as include or exclude before preparing public source.\n' >&2
  exit 1
fi
[[ ${#INCLUDED_TOP_LEVEL[@]} -gt 0 ]] || fail "Public-source policy selected no tracked content."

TMP_BUILD="$(mktemp -d "$OUTPUT_PARENT/.mem-public-source.XXXXXX")"
cleanup() {
  rm -rf -- "$TMP_BUILD"
}
trap cleanup EXIT

BUILD_ROOT="$TMP_BUILD/final"
SOURCE_DIR="$BUILD_ROOT/source"
EVIDENCE_DIR="$BUILD_ROOT/evidence"
mkdir -p -- "$SOURCE_DIR" "$EVIDENCE_DIR"

printf '%s\n' "${INCLUDED_TOP_LEVEL[@]}" | sort > "$EVIDENCE_DIR/included-top-level.txt"
if [[ ${#EXCLUDED_TOP_LEVEL[@]} -gt 0 ]]; then
  printf '%s\n' "${EXCLUDED_TOP_LEVEL[@]}" | sort > "$EVIDENCE_DIR/excluded-top-level.txt"
else
  : > "$EVIDENCE_DIR/excluded-top-level.txt"
fi

# Export from Git object data, not from the mutable working filesystem. This means
# only tracked bytes from the exact selected commit are eligible for publication.
git -C "$REPO_ROOT" archive --format=tar "$SOURCE_COMMIT" -- "${INCLUDED_TOP_LEVEL[@]}" |
  tar -xf - -C "$SOURCE_DIR"

# Apply only explicit, reviewed nested exclusions. Top-level classification remains
# separate so a whole repository root cannot be hidden through this file. Unknown
# nested local/build-state paths remain fail-closed in the safety scan below.
python3 - "$SOURCE_DIR" "$PATH_EXCLUSIONS_FILE" "$EVIDENCE_DIR/included-top-level.txt" "$EVIDENCE_DIR/excluded-paths.txt" <<'PY_PATH_EXCLUSIONS'
from __future__ import annotations

import pathlib
import shutil
import sys

root = pathlib.Path(sys.argv[1]).resolve()
policy = pathlib.Path(sys.argv[2])
included = set(pathlib.Path(sys.argv[3]).read_text(encoding="utf-8").splitlines())
evidence = pathlib.Path(sys.argv[4])

rules: list[str] = []
for line_no, raw in enumerate(policy.read_text(encoding="utf-8").splitlines(), start=1):
    value = raw.strip()
    if not value or value.startswith("#"):
        continue
    if "\\" in value or value.startswith("/") or any(ord(ch) < 32 or ord(ch) == 127 for ch in value):
        raise SystemExit(f"ERROR: Unsafe nested exclusion at {policy}:{line_no}: {value!r}")
    if any(ch in value for ch in "*?[]"):
        raise SystemExit(f"ERROR: Wildcards are not permitted in nested exclusions at {policy}:{line_no}: {value}")
    trimmed = value[:-1] if value.endswith("/") else value
    parts = pathlib.PurePosixPath(trimmed).parts
    if len(parts) < 2 or any(part in ("", ".", "..") for part in parts):
        raise SystemExit(f"ERROR: Nested exclusion must be a safe repository-relative path below an approved root: {value}")
    if parts[0] not in included:
        raise SystemExit(f"ERROR: Nested exclusion is not inside an included top-level root: {value}")
    rules.append(value)

if rules != sorted(set(rules)):
    raise SystemExit("ERROR: Nested public-source exclusions must be sorted and unique.")

for value in rules:
    is_dir_rule = value.endswith("/")
    rel = value[:-1] if is_dir_rule else value
    target = root.joinpath(*pathlib.PurePosixPath(rel).parts)
    try:
        target.relative_to(root)
    except ValueError:
        raise SystemExit(f"ERROR: Nested exclusion escapes public source root: {value}")
    if not target.exists() and not target.is_symlink():
        continue
    if target.is_symlink():
        target.unlink()
    elif is_dir_rule:
        if not target.is_dir():
            raise SystemExit(f"ERROR: Directory exclusion matched a non-directory: {value}")
        shutil.rmtree(target)
    else:
        if target.is_dir():
            raise SystemExit(f"ERROR: File exclusion matched a directory; add trailing /: {value}")
        target.unlink()

evidence.write_text("".join(f"{rule}\n" for rule in rules), encoding="utf-8", newline="\n")
PY_PATH_EXCLUSIONS

# Baseline source-surface safety checks. Deeper credential/content scanning and
# isolated full-product build qualification are intentionally added in 01B.
python3 - "$SOURCE_DIR" <<'PY_VALIDATE_TREE'
from __future__ import annotations

import fnmatch
import os
import pathlib
import stat
import sys

root = pathlib.Path(sys.argv[1])
forbidden_segments = {
    ".git",
    ".mem-slice-backups",
    ".state",
    ".vs",
    ".vscode",
    "bin",
    "coverage",
    "dist",
    "node_modules",
    "obj",
    "test-results",
    "TestResults",
}
forbidden_basenames = (".env", ".env.*")
forbidden_suffixes = (".db", ".db-shm", ".db-wal", ".sqlite", ".sqlite3", ".key", ".p12", ".pfx", ".pem")

entries = sorted(root.rglob("*"), key=lambda p: p.relative_to(root).as_posix())
for entry in entries:
    rel = entry.relative_to(root).as_posix()
    if any(ord(ch) < 32 or ord(ch) == 127 for ch in rel):
        raise SystemExit(f"ERROR: Public source contains a control-character path: {rel!r}")
    if entry.is_symlink():
        raise SystemExit(f"ERROR: Public source may not contain symlinks: {rel}")
    mode = entry.lstat().st_mode
    if not (stat.S_ISREG(mode) or stat.S_ISDIR(mode)):
        raise SystemExit(f"ERROR: Public source contains an unsupported filesystem entry: {rel}")
    parts = pathlib.PurePosixPath(rel).parts
    if any(part in forbidden_segments for part in parts):
        raise SystemExit(f"ERROR: Publication-inappropriate tracked path is included: {rel}")
    if entry.is_file():
        base = entry.name
        if any(fnmatch.fnmatch(base, pattern) for pattern in forbidden_basenames):
            raise SystemExit(f"ERROR: Local environment file is included in public source: {rel}")
        if base.endswith(forbidden_suffixes):
            raise SystemExit(f"ERROR: Secret/state-like file extension is included in public source: {rel}")
        os.chmod(entry, 0o755 if mode & stat.S_IXUSR else 0o644)
    elif entry.is_dir():
        os.chmod(entry, 0o755)
PY_VALIDATE_TREE

FILE_MANIFEST="$EVIDENCE_DIR/file-manifest.tsv"
IFS=' ' read -r FILE_COUNT PUBLIC_TREE_SHA256 < <(python3 - "$SOURCE_DIR" "$FILE_MANIFEST" <<'PY_FILE_MANIFEST'
from __future__ import annotations

import hashlib
import pathlib
import stat
import sys

root = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
records: list[bytes] = []
count = 0
with out_path.open("w", encoding="utf-8", newline="\n") as out:
    for path in sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.relative_to(root).as_posix()):
        rel = path.relative_to(root).as_posix()
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        size = len(data)
        mode = "0755" if path.stat().st_mode & stat.S_IXUSR else "0644"
        line = f"{digest}\t{size}\t{mode}\t{rel}\n"
        out.write(line)
        records.append(line.encode("utf-8"))
        count += 1
h = hashlib.sha256()
for record in records:
    h.update(record)
print(count, h.hexdigest())
PY_FILE_MANIFEST
)
[[ "$FILE_COUNT" =~ ^[0-9]+$ && "$FILE_COUNT" -gt 0 ]] || fail "Public source contains no regular files."
[[ "$PUBLIC_TREE_SHA256" =~ ^[0-9a-f]{64}$ ]] || fail "Could not compute public-source tree digest."

ARCHIVE_NAME="mem-${VERSION}-source.tar.gz"
ARCHIVE_PATH="$BUILD_ROOT/$ARCHIVE_NAME"
python3 - "$SOURCE_DIR" "$ARCHIVE_PATH" "$SOURCE_DATE_EPOCH" <<'PY_ARCHIVE'
from __future__ import annotations

import gzip
import pathlib
import stat
import sys
import tarfile

root = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
source_epoch = int(sys.argv[3])
files = sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.relative_to(root).as_posix())
with out_path.open("wb") as raw:
    with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode="w", format=tarfile.PAX_FORMAT) as tar:
            for path in files:
                rel = path.relative_to(root).as_posix()
                info = tar.gettarinfo(str(path), arcname=rel)
                info.uid = 0
                info.gid = 0
                info.uname = ""
                info.gname = ""
                info.mtime = source_epoch
                info.mode = 0o755 if path.stat().st_mode & stat.S_IXUSR else 0o644
                with path.open("rb") as handle:
                    tar.addfile(info, handle)
PY_ARCHIVE

ARCHIVE_SHA256="$(sha256sum "$ARCHIVE_PATH" | awk '{print $1}')"
ARCHIVE_SIZE="$(stat -c '%s' "$ARCHIVE_PATH")"
FILE_MANIFEST_SHA256="$(sha256sum "$FILE_MANIFEST" | awk '{print $1}')"
FILE_MANIFEST_SIZE="$(stat -c '%s' "$FILE_MANIFEST")"
POLICY_SHA256="$(sha256sum "$POLICY_FILE" | awk '{print $1}')"
PATH_EXCLUSIONS_SHA256="$(sha256sum "$PATH_EXCLUSIONS_FILE" | awk '{print $1}')"

MANIFEST_PATH="$BUILD_ROOT/public-source.json"
python3 - \
  "$MANIFEST_PATH" \
  "$VERSION" \
  "$SOURCE_COMMIT" \
  "$SOURCE_TREE" \
  "$SOURCE_DATE_EPOCH" \
  "$GENERATED_AT_UTC" \
  "$POLICY_SHA256" \
  "$PATH_EXCLUSIONS_SHA256" \
  "$FILE_COUNT" \
  "$PUBLIC_TREE_SHA256" \
  "$ARCHIVE_NAME" \
  "$ARCHIVE_SHA256" \
  "$ARCHIVE_SIZE" \
  "$FILE_MANIFEST_SHA256" \
  "$FILE_MANIFEST_SIZE" \
  "$EVIDENCE_DIR/included-top-level.txt" \
  "$EVIDENCE_DIR/excluded-top-level.txt" \
  "$EVIDENCE_DIR/excluded-paths.txt" <<'PY_MANIFEST'
from __future__ import annotations

import json
import pathlib
import sys

(
    manifest_path,
    version,
    source_commit,
    source_tree,
    source_epoch,
    generated_at,
    policy_sha256,
    path_exclusions_sha256,
    file_count,
    public_tree_sha256,
    archive_name,
    archive_sha256,
    archive_size,
    file_manifest_sha256,
    file_manifest_size,
    included_path,
    excluded_path,
    excluded_paths_path,
) = sys.argv[1:]

read_lines = lambda p: [line for line in pathlib.Path(p).read_text(encoding="utf-8").splitlines() if line]
doc = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "version": version,
    },
    "source": {
        "commit": source_commit,
        "tree": source_tree,
        "sourceDateEpoch": int(source_epoch),
        "workingTreeDirty": False,
    },
    "export": {
        "format": "mem-public-source-v1",
        "generatedAtUtc": generated_at,
        "policy": {
            "path": "scripts/public-source/public-source-top-level-policy.tsv",
            "sha256": policy_sha256,
        },
        "pathExclusions": {
            "path": "scripts/public-source/public-source-path-exclusions.txt",
            "sha256": path_exclusions_sha256,
        },
        "includedTopLevel": read_lines(included_path),
        "excludedTopLevel": read_lines(excluded_path),
        "excludedPaths": read_lines(excluded_paths_path),
    },
    "contents": {
        "fileCount": int(file_count),
        "treeSha256": public_tree_sha256,
    },
    "artifacts": {
        "archive": {
            "fileName": archive_name,
            "sha256": archive_sha256,
            "sizeBytes": int(archive_size),
        },
        "fileManifest": {
            "fileName": "evidence/file-manifest.tsv",
            "sha256": file_manifest_sha256,
            "sizeBytes": int(file_manifest_size),
        },
    },
}
with open(manifest_path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(doc, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY_MANIFEST

python3 "$VALIDATOR" "$MANIFEST_PATH"

cat > "$BUILD_ROOT/SHA256SUMS" <<EOF_SUMS
${ARCHIVE_SHA256}  ${ARCHIVE_NAME}
${FILE_MANIFEST_SHA256}  evidence/file-manifest.tsv
$(sha256sum "$MANIFEST_PATH" | awk '{print $1}')  public-source.json
EOF_SUMS

# The source commit must still be authoritative when preparation completes.
FINAL_HEAD="$(git -C "$REPO_ROOT" rev-parse HEAD)"
[[ "$FINAL_HEAD" == "$SOURCE_COMMIT" ]] || fail "Repository HEAD changed during public-source preparation."
if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  fail "Repository worktree became dirty during public-source preparation."
fi

mv -- "$BUILD_ROOT" "$OUTPUT_DIR"
trap - EXIT
rm -rf -- "$TMP_BUILD"

printf '\nPUBLIC SOURCE PREPARATION PASSED\n'
printf 'Version:              %s\n' "$VERSION"
printf 'Source commit:        %s\n' "$SOURCE_COMMIT"
printf 'Source Git tree:      %s\n' "$SOURCE_TREE"
printf 'Exported files:       %s\n' "$FILE_COUNT"
printf 'Public tree SHA-256:  %s\n' "$PUBLIC_TREE_SHA256"
printf 'Archive:              %s\n' "$OUTPUT_DIR/$ARCHIVE_NAME"
printf 'Manifest:             %s\n' "$OUTPUT_DIR/public-source.json"
printf 'Evidence:             %s\n' "$OUTPUT_DIR/evidence"
printf '\nNo Git repository was modified. No network operation or publication was performed.\n'
printf 'Review the prepared source tree before any later verification/publication step:\n  %s\n' "$OUTPUT_DIR/source"
