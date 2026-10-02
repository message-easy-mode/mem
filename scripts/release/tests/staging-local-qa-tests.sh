#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
PACKAGER="$RELEASE_DIR/package-local-qa-release.sh"
COPIER="$RELEASE_DIR/scp-local-qa-release.sh"
PUSHER="$RELEASE_DIR/push-staging-control-plane.sh"
BOOTSTRAP_TEMPLATE="$RELEASE_DIR/templates/install-mem-release.sh.in"
VALIDATOR="$RELEASE_DIR/validate-release-manifest.py"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-staging-local-qa-tests.XXXXXX")"
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

for required in "$PACKAGER" "$COPIER" "$PUSHER" "$BOOTSTRAP_TEMPLATE" "$VALIDATOR"; do
  [[ -e "$required" ]] || { echo "Missing staging release tool: $required" >&2; exit 2; }
done

if bash -n "$PACKAGER" && bash -n "$COPIER" && bash -n "$PUSHER" && bash -n "$BOOTSTRAP_TEMPLATE"; then
  pass "staging/local-QA shell tooling is syntactically valid"
else
  fail_test "staging/local-QA shell tooling is syntactically valid"
fi

if grep -Fq -- '--release-dir <directory>' "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq 'acquire "release.json"' "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq 'exec "$INSTALLER_ROOT/install.sh" "${INSTALLER_ARGS[@]}"' "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq -- '--control-plane-image override' "$BOOTSTRAP_TEMPLATE"; then
  pass "versioned bootstrap exposes local release acquisition while preserving bundled-installer argument passthrough"
else
  fail_test "versioned bootstrap exposes local release acquisition while preserving bundled-installer argument passthrough"
fi

if grep -Fq "MEM_RELEASE_VERSION='@@VERSION@@'" "$BOOTSTRAP_TEMPLATE" &&
   ! grep -Eq "^VERSION='@@VERSION@@'$" "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq "read -r OS_ID OS_VERSION_ID" "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq 'source /etc/os-release' "$BOOTSTRAP_TEMPLATE" &&
   grep -Fq '"$MEM_RELEASE_VERSION"' "$BOOTSTRAP_TEMPLATE"; then
  pass "versioned bootstrap keeps MEM release version isolated from os-release VERSION metadata"
else
  fail_test "versioned bootstrap keeps MEM release version isolated from os-release VERSION metadata"
fi

if grep -Fq 'registry.vs4.one/message-easy-mode/mem-control-plane' "$PUSHER" &&
   grep -Fq "resolve_image 'docker:29-cli'" "$PUSHER" &&
   grep -Fq -- '--build-arg "DOCKER_CLI_IMAGE=$DOCKER_CLI_IMAGE"' "$PUSHER" &&
   grep -Fq -- '--platform linux/amd64' "$PUSHER" &&
   grep -Fq -- '--push' "$PUSHER" &&
   grep -Fq -- '--expected-repository "$IMAGE_REPOSITORY"' "$PUSHER" &&
   grep -Fq 'ghcr.io/message-easy-mode/mem-control-plane' "$PUSHER"; then
  pass "staging image publisher resolves Docker CLI input and preserves canonical GHCR-shaped release identity"
else
  fail_test "staging image publisher resolves Docker CLI input and preserves canonical GHCR-shaped release identity"
fi

VERSION="0.2.0-rc.1"
DIGEST="sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
FIXTURE="$TMP_ROOT/release"
mkdir -p "$FIXTURE"

printf '#!/usr/bin/env bash\n# fixture supports --release-dir\n# --release-dir\nexit 0\n' > "$FIXTURE/install-mem-${VERSION}.sh"
chmod 0755 "$FIXTURE/install-mem-${VERSION}.sh"
printf '#!/usr/bin/env bash\nexit 0\n' > "$FIXTURE/install-mem-migrate-${VERSION}.sh"
chmod 0755 "$FIXTURE/install-mem-migrate-${VERSION}.sh"
printf 'fixture cli\n' > "$FIXTURE/mem-cli-${VERSION}-linux-x64"
printf '{"releaseVersion":"%s"}\n' "$VERSION" > "$FIXTURE/mem-migrate-${VERSION}-release.json"
printf 'fixture migrate\n' > "$FIXTURE/mem-migrate-${VERSION}-linux-x64.tar.gz"
MIGRATE_SHA="$(sha256sum "$FIXTURE/mem-migrate-${VERSION}-linux-x64.tar.gz" | awk '{print $1}')"
printf '%s  %s\n' "$MIGRATE_SHA" "mem-migrate-${VERSION}-linux-x64.tar.gz" > "$FIXTURE/mem-migrate-${VERSION}-linux-x64.tar.gz.sha256"
printf '{"spdxVersion":"SPDX-2.3"}\n' > "$FIXTURE/mem-${VERSION}-sbom.spdx.json"

INSTALLER_STAGE="$TMP_ROOT/installer-stage/mem-installer-${VERSION}-ubuntu-24.04-amd64"
mkdir -p "$INSTALLER_STAGE/bootstrap"
cat > "$INSTALLER_STAGE/bootstrap/install.sh" <<'EOF_INSTALLER'
#!/usr/bin/env bash
# Fixture proves the current installer contract exposes the explicit QA image override.
if [[ "${1:-}" == "--control-plane-image" ]]; then
  shift 2
fi
exit 0
EOF_INSTALLER
chmod 0755 "$INSTALLER_STAGE/bootstrap/install.sh"
tar -czf "$FIXTURE/mem-installer-${VERSION}-ubuntu-24.04-amd64.tar.gz" \
  -C "$TMP_ROOT/installer-stage" "mem-installer-${VERSION}-ubuntu-24.04-amd64"

python3 - "$FIXTURE" "$VERSION" "$DIGEST" <<'PY_MANIFEST'
import hashlib, json, os, sys
from pathlib import Path
root = Path(sys.argv[1])
version, digest = sys.argv[2:]

def artifact(artifact_id, kind, filename, **extra):
    path = root / filename
    record = {
        "id": artifact_id,
        "kind": kind,
        "fileName": filename,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "sizeBytes": path.stat().st_size,
        "sourceId": "control-plane",
    }
    record.update(extra)
    return record

platform = {"os": "ubuntu", "version": "24.04", "architecture": "amd64"}
doc = {
    "schemaVersion": 1,
    "product": {"id": "mem", "name": "Message Easy Mode", "version": version, "channel": "prerelease"},
    "release": {
        "generatedAtUtc": "2026-09-05T00:00:00Z",
        "publication": {
            "provider": "github-releases",
            "repository": "https://github.com/message-easy-mode/mem-releases",
            "tag": f"v{version}",
        },
    },
    "sources": [{
        "id": "control-plane",
        "repository": "https://github.com/message-easy-mode/mem",
        "commit": "0123456789abcdef0123456789abcdef01234567",
    }],
    "support": {"controlPlaneHosts": [platform]},
    "controlPlane": {
        "sourceId": "control-plane",
        "image": {
            "repository": "ghcr.io/message-easy-mode/mem-control-plane",
            "tag": version,
            "digest": digest,
            "reference": f"ghcr.io/message-easy-mode/mem-control-plane:{version}@{digest}",
        },
    },
    "artifacts": [
        artifact("bootstrap", "bootstrap-script", f"install-mem-{version}.sh"),
        artifact("installer", "installer-bundle", f"mem-installer-{version}-ubuntu-24.04-amd64.tar.gz", platform=platform),
        artifact("cli", "cli-binary", f"mem-cli-{version}-linux-x64", runtime="linux-x64"),
        artifact("migrate-bootstrap", "migrate-bootstrap-script", f"install-mem-migrate-{version}.sh"),
        artifact("migrate-manifest", "component-release-manifest", f"mem-migrate-{version}-release.json"),
        artifact("migrate", "migrate-bundle", f"mem-migrate-{version}-linux-x64.tar.gz", runtime="linux-x64"),
        artifact("migrate-checksum", "sha256-sidecar", f"mem-migrate-{version}-linux-x64.tar.gz.sha256"),
        artifact("sbom", "sbom-spdx-json", f"mem-{version}-sbom.spdx.json"),
    ],
    "verification": {
        "checksums": {"algorithm": "sha256", "fileName": "SHA256SUMS"},
        "publication": {"provider": "github-releases", "immutableReleaseRequired": True, "releaseAttestationRequired": True},
        "provenance": {"provider": "github-artifact-attestations", "fileArtifactsRequired": True, "containerImageRequired": True},
    },
}
(root / "release.json").write_text(json.dumps(doc, indent=2, sort_keys=True) + "\n", encoding="utf-8")
PY_MANIFEST

(
  cd "$FIXTURE"
  for name in \
    "install-mem-${VERSION}.sh" \
    "install-mem-migrate-${VERSION}.sh" \
    "mem-${VERSION}-sbom.spdx.json" \
    "mem-cli-${VERSION}-linux-x64" \
    "mem-installer-${VERSION}-ubuntu-24.04-amd64.tar.gz" \
    "mem-migrate-${VERSION}-linux-x64.tar.gz" \
    "mem-migrate-${VERSION}-linux-x64.tar.gz.sha256" \
    "mem-migrate-${VERSION}-release.json" \
    "release.json"; do
    sha256sum "$name"
  done | LC_ALL=C sort -k2 > SHA256SUMS
)

if python3 "$VALIDATOR" "$FIXTURE/release.json" --assets-dir "$FIXTURE" >/dev/null; then
  pass "local-QA fixture is an exact valid schema-v1 release directory"
else
  fail_test "local-QA fixture is an exact valid schema-v1 release directory"
fi


# Regression for the clean-room failure found on Ubuntu 24.04.2:
# /etc/os-release defines VERSION="24.04.2 LTS ...". The public bootstrap must
# not let that generic host variable replace the requested MEM release version.
RUNTIME_FIXTURE="$TMP_ROOT/runtime-release"
cp -a "$FIXTURE" "$RUNTIME_FIXTURE"
FAKE_OS_RELEASE="$TMP_ROOT/os-release"
cat > "$FAKE_OS_RELEASE" <<'EOF_OS_RELEASE'
ID=ubuntu
VERSION_ID="24.04"
VERSION="24.04.2 LTS (Noble Numbat)"
EOF_OS_RELEASE
python3 - \
  "$BOOTSTRAP_TEMPLATE" \
  "$RUNTIME_FIXTURE/install-mem-${VERSION}.sh" \
  "$VERSION" \
  "$FAKE_OS_RELEASE" <<'PY_RENDER_RUNTIME'
import sys
from pathlib import Path

template_path, output_path, version, os_release_path = sys.argv[1:]
text = Path(template_path).read_text(encoding="utf-8")
text = text.replace("@@VERSION@@", version)
text = text.replace("/etc/os-release", os_release_path)
if "@@" in text:
    raise SystemExit("unresolved template placeholder")
Path(output_path).write_text(text, encoding="utf-8")
PY_RENDER_RUNTIME
chmod 0755 "$RUNTIME_FIXTURE/install-mem-${VERSION}.sh"

# release.json itself is unchanged; refresh the aggregate checksum for the
# rendered bootstrap while preserving the exact manifest/product identity.
(
  cd "$RUNTIME_FIXTURE"
  for name in \
    "install-mem-${VERSION}.sh" \
    "install-mem-migrate-${VERSION}.sh" \
    "mem-${VERSION}-sbom.spdx.json" \
    "mem-cli-${VERSION}-linux-x64" \
    "mem-installer-${VERSION}-ubuntu-24.04-amd64.tar.gz" \
    "mem-migrate-${VERSION}-linux-x64.tar.gz" \
    "mem-migrate-${VERSION}-linux-x64.tar.gz.sha256" \
    "mem-migrate-${VERSION}-release.json" \
    "release.json"; do
    sha256sum "$name"
  done | LC_ALL=C sort -k2 > SHA256SUMS
)

set +e
RUNTIME_OUTPUT="$("$RUNTIME_FIXTURE/install-mem-${VERSION}.sh" \
  --release-dir "$RUNTIME_FIXTURE" \
  --dry-run 2>&1)"
RUNTIME_STATUS=$?
set -e
if [[ $RUNTIME_STATUS -ne 0 &&
      "$RUNTIME_OUTPUT" == *"Installer archive is missing required paths"* &&
      "$RUNTIME_OUTPUT" != *"product identity does not match"* ]]; then
  pass "Ubuntu os-release VERSION metadata cannot overwrite the requested MEM release identity"
else
  fail_test "Ubuntu os-release VERSION metadata cannot overwrite the requested MEM release identity" "$RUNTIME_OUTPUT"
fi

STAGING_IMAGE="registry.vs4.one/message-easy-mode/mem-control-plane:${VERSION}@${DIGEST}"
OUTPUT="$TMP_ROOT/output"
mkdir -p "$OUTPUT"
if "$PACKAGER" \
  --release-dir "$FIXTURE" \
  --staging-image "$STAGING_IMAGE" \
  --output-dir "$OUTPUT" >/dev/null; then
  pass "local-QA packager accepts the exact release plus same-digest private registry image"
else
  fail_test "local-QA packager accepts the exact release plus same-digest private registry image"
fi

BUNDLE="$OUTPUT/mem-local-qa-${VERSION}.tar.gz"
EXTRACTED="$TMP_ROOT/extracted"
mkdir -p "$EXTRACTED"
tar -xzf "$BUNDLE" -C "$EXTRACTED"
KIT="$EXTRACTED/mem-local-qa-${VERSION}"
if [[ -x "$KIT/install-local.sh" ]] &&
   grep -Fq -- '--release-dir "$SCRIPT_DIR/release"' "$KIT/install-local.sh" &&
   grep -Fq -- '--control-plane-image "$STAGING_IMAGE"' "$KIT/install-local.sh" &&
   [[ "$(cat "$KIT/STAGING-CONTROL-PLANE-IMAGE")" == "$STAGING_IMAGE" ]] &&
   cmp -s "$FIXTURE/release.json" "$KIT/release/release.json" &&
   cmp -s "$FIXTURE/SHA256SUMS" "$KIT/release/SHA256SUMS"; then
  pass "QA transport keeps public release bytes unchanged and adds only an explicit local installer wrapper"
else
  fail_test "QA transport keeps public release bytes unchanged and adds only an explicit local installer wrapper"
fi

if (
  cd "$KIT"
  sha256sum -c QA-SHA256SUMS >/dev/null &&
  cd release
  sha256sum -c SHA256SUMS >/dev/null
); then
  pass "packaged QA wrapper and copied public release checksums verify"
else
  fail_test "packaged QA wrapper and copied public release checksums verify"
fi

BAD_IMAGE="registry.vs4.one/message-easy-mode/mem-control-plane:${VERSION}@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
expect_reject \
  "QA packager rejects a private-registry image whose digest differs from release.json" \
  "digest does not match" \
  "$PACKAGER" \
  --release-dir "$FIXTURE" \
  --staging-image "$BAD_IMAGE" \
  --output-dir "$TMP_ROOT/bad-output"

DRY_OUTPUT="$($COPIER \
  --bundle "$BUNDLE" \
  --target qa@ubuntu24 \
  --remote-root /tmp/mem-qa \
  --dry-run 2>&1)" || true
if [[ "$DRY_OUTPUT" == *"[dry-run] scp"* &&
      "$DRY_OUTPUT" == *"Remote path: /tmp/mem-qa/mem-local-qa-${VERSION}"* ]]; then
  pass "SCP helper provides a no-network dry-run for an arbitrary operator-selected Ubuntu host"
else
  fail_test "SCP helper provides a no-network dry-run for an arbitrary operator-selected Ubuntu host" "$DRY_OUTPUT"
fi

OLD_BOOTSTRAP_FIXTURE="$TMP_ROOT/old-bootstrap-release"
cp -a "$FIXTURE" "$OLD_BOOTSTRAP_FIXTURE"
printf '#!/usr/bin/env bash\nexit 0\n' > "$OLD_BOOTSTRAP_FIXTURE/install-mem-${VERSION}.sh"
chmod 0755 "$OLD_BOOTSTRAP_FIXTURE/install-mem-${VERSION}.sh"
python3 - "$OLD_BOOTSTRAP_FIXTURE/release.json" <<'PY_REFRESH'
import hashlib, json, pathlib, sys
root = pathlib.Path(sys.argv[1]).parent
manifest_path = root / "release.json"
doc = json.loads(manifest_path.read_text())
for artifact in doc["artifacts"]:
    path = root / artifact["fileName"]
    data = path.read_bytes()
    artifact["sha256"] = hashlib.sha256(data).hexdigest()
    artifact["sizeBytes"] = len(data)
manifest_path.write_text(json.dumps(doc, indent=2, sort_keys=True) + "\n")
checks = []
for path in sorted(root.iterdir(), key=lambda p: p.name.encode()):
    if path.name == "SHA256SUMS" or not path.is_file():
        continue
    checks.append(f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}")
(root / "SHA256SUMS").write_text("\n".join(checks) + "\n")
PY_REFRESH
expect_reject \
  "QA packager rejects release assets built before the local release-dir bootstrap exists" \
  "does not support --release-dir" \
  "$PACKAGER" \
  --release-dir "$OLD_BOOTSTRAP_FIXTURE" \
  --staging-image "$STAGING_IMAGE" \
  --output-dir "$TMP_ROOT/old-bootstrap-output"

MOCK_BIN="$TMP_ROOT/mock-network-bin"
mkdir -p "$MOCK_BIN"
cat > "$MOCK_BIN/scp" <<'MOCK_SCP'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ "${1:-}" == "--" ]] && shift
source_path="$1"
destination="$2"
remote_path="${destination#*:}"
cp -- "$source_path" "$remote_path"
MOCK_SCP
cat > "$MOCK_BIN/ssh" <<'MOCK_SSH'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ "${1:-}" == "--" ]] && shift
target="$1"
shift
if [[ "${1:-}" == "bash -s" ]]; then
  bash -s
else
  bash -c "$1"
fi
MOCK_SSH
chmod 0755 "$MOCK_BIN/scp" "$MOCK_BIN/ssh"
REMOTE_ROOT="$TMP_ROOT/mock-remote"
if PATH="$MOCK_BIN:$PATH" "$COPIER" \
  --bundle "$BUNDLE" \
  --target qa@ubuntu24 \
  --remote-root "$REMOTE_ROOT" >/dev/null &&
   [[ -x "$REMOTE_ROOT/mem-local-qa-${VERSION}/install-local.sh" ]] &&
   (cd "$REMOTE_ROOT/mem-local-qa-${VERSION}" && sha256sum -c QA-SHA256SUMS >/dev/null) &&
   (cd "$REMOTE_ROOT/mem-local-qa-${VERSION}/release" && sha256sum -c SHA256SUMS >/dev/null); then
  pass "SCP helper verifies and atomically publishes a copied QA kit with mocked OpenSSH transport"
else
  fail_test "SCP helper verifies and atomically publishes a copied QA kit with mocked OpenSSH transport"
fi

printf '\nStaging/local-QA release tests: %d passed, %d failed\n' "$PASSED" "$FAILED"
[[ "$FAILED" -eq 0 ]]
