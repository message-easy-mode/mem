#!/usr/bin/env bash
set -u

CLI_PROJECT="./cli/src/Mem.Cli/Mem.Cli.csproj"
TOKEN="${MEM_INSTALLER_TOKEN:-mem_test123}"
VALID_ZIP="${1:-${MEM_BACKUP_VALIDATION_ZIP:-}}"

WORK_DIR="$(mktemp -d)"
FAILURES=0

cleanup() {
  rm -rf "$WORK_DIR"
}

trap cleanup EXIT

print_header() {
  echo
  echo "============================================================"
  echo "$1"
  echo "============================================================"
}

make_zip_wrong_root() {
  local output="$1"

  python3 - "$output" <<'PY'
import sys
import zipfile

output = sys.argv[1]

with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as z:
    z.writestr("wrong-root/file.txt", "not a MEM export")
PY
}

make_zip_unsafe_path() {
  local output="$1"

  python3 - "$output" <<'PY'
import sys
import zipfile

output = sys.argv[1]

with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as z:
    z.writestr("../evil.txt", "unsafe path")
    z.writestr("mem-stack-export/mem-export-manifest.json", "{}")
PY
}

make_zip_missing_manifest() {
  local input="$1"
  local output="$2"

  python3 - "$input" "$output" <<'PY'
import sys
import zipfile

source = sys.argv[1]
target = sys.argv[2]
skip = "mem-stack-export/mem-export-manifest.json"

with zipfile.ZipFile(source, "r") as zin:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            if item.filename == skip:
                continue

            zout.writestr(item, zin.read(item.filename))
PY
}

make_zip_missing_payload_file() {
  local input="$1"
  local output="$2"

  python3 - "$input" "$output" <<'PY'
import sys
import zipfile

source = sys.argv[1]
target = sys.argv[2]
skip = "mem-stack-export/element/config.json"

with zipfile.ZipFile(source, "r") as zin:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            if item.filename == skip:
                continue

            zout.writestr(item, zin.read(item.filename))
PY
}

make_zip_tampered_file() {
  local input="$1"
  local output="$2"

  python3 - "$input" "$output" <<'PY'
import sys
import zipfile

source = sys.argv[1]
target = sys.argv[2]
tamper = "mem-stack-export/element/config.json"

with zipfile.ZipFile(source, "r") as zin:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)

            if item.filename == tamper:
                data = data + b"\n// tampered by validation test\n"

            zout.writestr(item, data)
PY
}

run_expect_status() {
  local label="$1"
  local zip_path="$2"
  local expected_status="$3"

  local output="$WORK_DIR/${label}.json"
  local exit_code=0

  print_header "$label"

  dotnet run --project "$CLI_PROJECT" -- \
    backups validate "$zip_path" \
    --json \
    --installer-token "$TOKEN" > "$output" || exit_code=$?

  cat "$output" | jq '{
    status,
    detail,
    integrity,
    failedChecks: [.checks[]? | select(.passed == false) | { code, message, detail }],
    errors
  }'

  local actual_status
  actual_status="$(jq -r '.status // "null"' "$output")"

  if [[ "$actual_status" != "$expected_status" ]]; then
    echo "Expected status '$expected_status' but got '$actual_status'"
    FAILURES=$((FAILURES + 1))
    return
  fi

  if [[ "$expected_status" == "valid" && "$exit_code" -ne 0 ]]; then
    echo "Expected exit code 0 for valid ZIP but got $exit_code"
    FAILURES=$((FAILURES + 1))
    return
  fi

  if [[ "$expected_status" != "valid" && "$exit_code" -eq 0 ]]; then
    echo "Expected non-zero exit code for invalid ZIP but got 0"
    FAILURES=$((FAILURES + 1))
    return
  fi

  echo "PASS: $label"
}

if [[ -z "$VALID_ZIP" || ! -f "$VALID_ZIP" ]]; then
  if [[ -n "$VALID_ZIP" ]]; then
    echo "Valid ZIP not found: $VALID_ZIP"
  else
    echo "A valid MEM stack export ZIP is required."
  fi
  echo
  echo "Usage:"
  echo "  $0 /path/to/mem-stack-export.zip"
  echo "  MEM_BACKUP_VALIDATION_ZIP=/path/to/mem-stack-export.zip $0"
  exit 1
fi

PLAIN_TEXT_ZIP="$WORK_DIR/not-a-zip.zip"
WRONG_ROOT_ZIP="$WORK_DIR/wrong-root.zip"
UNSAFE_PATH_ZIP="$WORK_DIR/unsafe-path.zip"
MISSING_MANIFEST_ZIP="$WORK_DIR/missing-manifest.zip"
MISSING_PAYLOAD_ZIP="$WORK_DIR/missing-payload-file.zip"
TAMPERED_ZIP="$WORK_DIR/tampered-file.zip"

printf "this is not a real zip\n" > "$PLAIN_TEXT_ZIP"

make_zip_wrong_root "$WRONG_ROOT_ZIP"
make_zip_unsafe_path "$UNSAFE_PATH_ZIP"
make_zip_missing_manifest "$VALID_ZIP" "$MISSING_MANIFEST_ZIP"
make_zip_missing_payload_file "$VALID_ZIP" "$MISSING_PAYLOAD_ZIP"
make_zip_tampered_file "$VALID_ZIP" "$TAMPERED_ZIP"

run_expect_status "valid-export" "$VALID_ZIP" "valid"
run_expect_status "not-a-zip" "$PLAIN_TEXT_ZIP" "invalid"
run_expect_status "wrong-root" "$WRONG_ROOT_ZIP" "invalid"
run_expect_status "unsafe-path" "$UNSAFE_PATH_ZIP" "invalid"
run_expect_status "missing-manifest" "$MISSING_MANIFEST_ZIP" "invalid"
run_expect_status "missing-payload-file" "$MISSING_PAYLOAD_ZIP" "invalid"
run_expect_status "tampered-file" "$TAMPERED_ZIP" "invalid"

echo
echo "============================================================"

if [[ "$FAILURES" -eq 0 ]]; then
  echo "All backup export validation tests passed."
  exit 0
fi

echo "$FAILURES backup export validation test(s) failed."
exit 1