#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"

VERSION=""
CONTROL_PLANE_IMAGE=""
CLI_DIR=""
MIGRATE_DIR=""
SBOM_PATH=""
OUTPUT_DIR=""
SOURCE_COMMIT=""

usage() {
  cat <<'EOF'
Assemble the exact public Message Easy Mode release asset set.

Usage:
  ./scripts/release/assemble-product-release.sh \
    --version 0.2.0 \
    --control-plane-image ghcr.io/message-easy-mode/mem-control-plane:0.2.0@sha256:<digest> \
    --cli-dir <release-eligible CLI output> \
    --migrate-dir <release-eligible Migrate output> \
    --sbom <SPDX-2.3 JSON file> \
    --output-dir <directory>

The assembler:
  * requires a clean committed source tree;
  * requires CLI and Migrate producer evidence from the same source commit;
  * builds the 03B installer itself with the exact standalone CLI bytes;
  * binds the installer to the exact pushed Control Plane image digest;
  * validates the SPDX JSON input;
  * generates the exact versioned public bootstrap;
  * emits release.json and aggregate SHA256SUMS; and
  * validates the final directory against the schema-v1 release contract.

Component *.build-info.json files are producer evidence only and are deliberately
not copied into the public GitHub Release asset set.
EOF
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
    --control-plane-image)
      [[ $# -ge 2 ]] || fail "--control-plane-image requires a value."
      CONTROL_PLANE_IMAGE="$2"
      shift 2
      ;;
    --cli-dir)
      [[ $# -ge 2 ]] || fail "--cli-dir requires a path."
      CLI_DIR="$2"
      shift 2
      ;;
    --migrate-dir)
      [[ $# -ge 2 ]] || fail "--migrate-dir requires a path."
      MIGRATE_DIR="$2"
      shift 2
      ;;
    --sbom)
      [[ $# -ge 2 ]] || fail "--sbom requires a path."
      SBOM_PATH="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --source-commit)
      [[ $# -ge 2 ]] || fail "--source-commit requires a value."
      SOURCE_COMMIT="$2"
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
[[ -n "$CONTROL_PLANE_IMAGE" ]] || fail "--control-plane-image is required."
[[ -n "$CLI_DIR" ]] || fail "--cli-dir is required."
[[ -n "$MIGRATE_DIR" ]] || fail "--migrate-dir is required."
[[ -n "$SBOM_PATH" ]] || fail "--sbom is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."

[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "--version must be an ordinary semantic version or prerelease version."
case "$VERSION" in
  *latest*|*stable*|*current*|*dev*)
    fail "Public release version may not contain a mutable channel/development label: $VERSION"
    ;;
esac

for command_name in git realpath mktemp rm mkdir cp chmod sha256sum awk python3 cmp stat; do
  require_command "$command_name"
done

INSTALLER_BUILDER="$SCRIPT_DIR/build-installer-bundle.sh"
VALIDATOR="$SCRIPT_DIR/validate-release-manifest.py"
BOOTSTRAP_TEMPLATE="$SCRIPT_DIR/templates/install-mem-release.sh.in"
for required in "$INSTALLER_BUILDER" "$VALIDATOR" "$BOOTSTRAP_TEMPLATE"; do
  [[ -f "$required" ]] || fail "Required release tooling is missing: $required"
done
[[ -x "$INSTALLER_BUILDER" ]] || fail "Installer builder is not executable: $INSTALLER_BUILDER"

CLI_DIR="$(realpath -m -- "$CLI_DIR")"
MIGRATE_DIR="$(realpath -m -- "$MIGRATE_DIR")"
SBOM_PATH="$(realpath -m -- "$SBOM_PATH")"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
[[ -d "$CLI_DIR" ]] || fail "CLI release directory does not exist: $CLI_DIR"
[[ -d "$MIGRATE_DIR" ]] || fail "Migrate release directory does not exist: $MIGRATE_DIR"
[[ -f "$SBOM_PATH" ]] || fail "SBOM file does not exist: $SBOM_PATH"
[[ "$OUTPUT_DIR" != "/" ]] || fail "Refusing to use filesystem root as output."

HEAD_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null)" ||
  fail "Repository root is not a readable Git worktree: $REPO_ROOT"
if [[ -z "$SOURCE_COMMIT" ]]; then
  SOURCE_COMMIT="$HEAD_COMMIT"
fi
[[ "$SOURCE_COMMIT" =~ ^[0-9a-f]{40,64}$ ]] ||
  fail "Invalid source commit: $SOURCE_COMMIT"
[[ "$SOURCE_COMMIT" == "$HEAD_COMMIT" ]] ||
  fail "Selected source commit does not match repository HEAD."

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  fail "Repository worktree is dirty. Product release assembly requires a clean committed source tree."
fi

SOURCE_DATE_EPOCH="$(git -C "$REPO_ROOT" show -s --format=%ct "$SOURCE_COMMIT")"
[[ "$SOURCE_DATE_EPOCH" =~ ^[0-9]+$ ]] || fail "Could not determine source commit timestamp."
GENERATED_AT_UTC="$(python3 - "$SOURCE_DATE_EPOCH" <<'PY'
from datetime import datetime, timezone
import sys
print(datetime.fromtimestamp(int(sys.argv[1]), timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"))
PY
)"

CHANNEL="stable"
if [[ "$VERSION" == *-* ]]; then
  CHANNEL="prerelease"
fi

IMAGE_REPOSITORY="ghcr.io/message-easy-mode/mem-control-plane"
IMAGE_PREFIX="${IMAGE_REPOSITORY}:${VERSION}@sha256:"
[[ "$CONTROL_PLANE_IMAGE" == "$IMAGE_PREFIX"* ]] ||
  fail "Control Plane image must use exact version $VERSION and an immutable sha256 digest."
IMAGE_DIGEST="${CONTROL_PLANE_IMAGE#*@}"
[[ "$IMAGE_DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] ||
  fail "Control Plane image digest must be sha256:<64 lowercase hex>."

CLI_NAME="mem-cli-${VERSION}-linux-x64"
CLI_PATH="$CLI_DIR/$CLI_NAME"
CLI_BUILD_INFO="$CLI_DIR/${CLI_NAME}.build-info.json"

MIGRATE_BOOTSTRAP_NAME="install-mem-migrate-${VERSION}.sh"
MIGRATE_MANIFEST_NAME="mem-migrate-${VERSION}-release.json"
MIGRATE_ARCHIVE_NAME="mem-migrate-${VERSION}-linux-x64.tar.gz"
MIGRATE_CHECKSUM_NAME="${MIGRATE_ARCHIVE_NAME}.sha256"
MIGRATE_BUILD_INFO_NAME="mem-migrate-${VERSION}-linux-x64.build-info.json"

for required in \
  "$CLI_PATH" \
  "$CLI_BUILD_INFO" \
  "$MIGRATE_DIR/$MIGRATE_BOOTSTRAP_NAME" \
  "$MIGRATE_DIR/$MIGRATE_MANIFEST_NAME" \
  "$MIGRATE_DIR/$MIGRATE_ARCHIVE_NAME" \
  "$MIGRATE_DIR/$MIGRATE_CHECKSUM_NAME" \
  "$MIGRATE_DIR/$MIGRATE_BUILD_INFO_NAME"; do
  [[ -f "$required" ]] || fail "Required component release file is missing: $required"
done

chmod +x "$CLI_PATH"
CLI_VERSION_OUTPUT="$("$CLI_PATH" --version 2>&1)" ||
  fail "Standalone MEM CLI --version failed."
EXPECTED_CLI_OUTPUT="$(cat <<EOF
Message Easy Mode CLI
Version: ${VERSION}
Command: mem
EOF
)"
[[ "$CLI_VERSION_OUTPUT" == "$EXPECTED_CLI_OUTPUT" ]] ||
  fail "Standalone CLI does not report exact Message Easy Mode $VERSION identity."

python3 - \
  "$CLI_PATH" \
  "$CLI_BUILD_INFO" \
  "$VERSION" \
  "$SOURCE_COMMIT" \
  "$SOURCE_DATE_EPOCH" <<'PY_VALIDATE_CLI'
import hashlib
import json
import os
import sys

binary_path, info_path, version, source_commit, source_epoch = sys.argv[1:]

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

with open(info_path, "r", encoding="utf-8") as handle:
    doc = json.load(handle)

if doc.get("schemaVersion") != 1:
    raise SystemExit("ERROR: CLI build-info schemaVersion must be 1.")
if doc.get("product") != {
    "id": "mem",
    "name": "Message Easy Mode",
    "version": version,
}:
    raise SystemExit("ERROR: CLI build-info product identity mismatch.")

artifact = doc.get("artifact", {})
expected_name = f"mem-cli-{version}-linux-x64"
if (
    artifact.get("id") != "cli"
    or artifact.get("kind") != "cli-binary"
    or artifact.get("fileName") != expected_name
    or artifact.get("runtime") != "linux-x64"
    or artifact.get("sha256") != digest(binary_path)
    or artifact.get("sizeBytes") != os.path.getsize(binary_path)
):
    raise SystemExit("ERROR: CLI build-info artifact evidence does not match the supplied binary.")

source = doc.get("source", {})
if (
    source.get("commit") != source_commit
    or source.get("sourceDateEpoch") != int(source_epoch)
    or source.get("workingTreeDirty") is not False
):
    raise SystemExit("ERROR: CLI build-info is not from the clean selected source commit.")

build = doc.get("build", {})
if build.get("releaseEligible") is not True:
    raise SystemExit("ERROR: CLI build-info is not releaseEligible=true.")
if (
    build.get("configuration") != "Release"
    or build.get("selfContained") is not True
    or build.get("singleFile") is not True
):
    raise SystemExit("ERROR: CLI build-info does not describe the canonical release build.")
PY_VALIDATE_CLI

python3 - \
  "$MIGRATE_DIR" \
  "$MIGRATE_BUILD_INFO_NAME" \
  "$VERSION" \
  "$CHANNEL" \
  "$SOURCE_COMMIT" \
  "$SOURCE_DATE_EPOCH" <<'PY_VALIDATE_MIGRATE'
import hashlib
import json
import os
import re
import sys
from pathlib import Path

directory, build_info_name, version, channel, source_commit, source_epoch = sys.argv[1:]
root = Path(directory)

def fail(message):
    raise SystemExit(f"ERROR: {message}")

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

with open(root / build_info_name, "r", encoding="utf-8") as handle:
    info = json.load(handle)

if info.get("schemaVersion") != 1:
    fail("Migrate build-info schemaVersion must be 1")
product = info.get("product", {})
if (
    product.get("id") != "mem"
    or product.get("name") != "Message Easy Mode"
    or product.get("component") != "MEM Migrate"
    or product.get("version") != version
    or product.get("channel") != channel
):
    fail("Migrate build-info product identity mismatch")

source = info.get("source", {})
if (
    source.get("commit") != source_commit
    or source.get("sourceDateEpoch") != int(source_epoch)
    or source.get("workingTreeDirty") is not False
):
    fail("Migrate build-info is not from the clean selected source commit")

build = info.get("build", {})
if build.get("releaseEligible") is not True or build.get("runtime") != "linux-x64":
    fail("Migrate build-info is not releaseEligible linux-x64 evidence")

expected = {
    "migrate-bootstrap": (
        f"install-mem-migrate-{version}.sh",
        "migrate-bootstrap-script",
    ),
    "migrate-manifest": (
        f"mem-migrate-{version}-release.json",
        "component-release-manifest",
    ),
    "migrate": (
        f"mem-migrate-{version}-linux-x64.tar.gz",
        "migrate-bundle",
    ),
    "migrate-checksum": (
        f"mem-migrate-{version}-linux-x64.tar.gz.sha256",
        "sha256-sidecar",
    ),
}
records = {item.get("id"): item for item in info.get("artifacts", [])}
if set(records) != set(expected):
    fail("Migrate build-info artifact set is not exact")

for artifact_id, (name, kind) in expected.items():
    record = records[artifact_id]
    path = root / name
    if (
        record.get("fileName") != name
        or record.get("kind") != kind
        or record.get("sha256") != digest(path)
        or record.get("sizeBytes") != path.stat().st_size
    ):
        fail(f"Migrate build-info mismatch for {artifact_id}")

component_manifest_path = root / expected["migrate-manifest"][0]
with open(component_manifest_path, "r", encoding="utf-8") as handle:
    component = json.load(handle)
archive_name = expected["migrate"][0]
archive_path = root / archive_name
archive_sha = digest(archive_path)
required_component = {
    "schemaVersion": 1,
    "channel": channel,
    "releaseVersion": version,
    "runtime": "linux-x64",
    "archiveFileName": archive_name,
    "archiveSha256": archive_sha,
}
for key, value in required_component.items():
    if component.get(key) != value:
        fail(f"Migrate component manifest mismatch for {key}")

sidecar_path = root / expected["migrate-checksum"][0]
parts = sidecar_path.read_text(encoding="utf-8").strip().split(None, 1)
if len(parts) != 2:
    fail("Migrate checksum sidecar is malformed")
sidecar_sha, sidecar_name = parts
sidecar_name = sidecar_name.lstrip("*")
if sidecar_sha != archive_sha or sidecar_name != archive_name:
    fail("Migrate checksum sidecar does not match the archive")
PY_VALIDATE_MIGRATE

python3 - "$SBOM_PATH" <<'PY_VALIDATE_SBOM'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    doc = json.load(handle)

if doc.get("spdxVersion") != "SPDX-2.3":
    raise SystemExit("ERROR: SBOM must be SPDX-2.3 JSON.")
if doc.get("dataLicense") != "CC0-1.0":
    raise SystemExit("ERROR: SPDX SBOM dataLicense must be CC0-1.0.")
if doc.get("SPDXID") != "SPDXRef-DOCUMENT":
    raise SystemExit("ERROR: SPDX SBOM document identity is invalid.")
if not isinstance(doc.get("name"), str) or not doc["name"].strip():
    raise SystemExit("ERROR: SPDX SBOM name is required.")
creation = doc.get("creationInfo")
if not isinstance(creation, dict) or not creation.get("created") or not creation.get("creators"):
    raise SystemExit("ERROR: SPDX SBOM creationInfo is incomplete.")
PY_VALIDATE_SBOM

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-product-release.XXXXXX")"
cleanup() {
  rm -rf -- "$TMP_ROOT"
}
trap cleanup EXIT INT TERM

INSTALLER_DIR="$TMP_ROOT/installer"
mkdir -p -- "$INSTALLER_DIR"

SOURCE_DATE_EPOCH="$SOURCE_DATE_EPOCH" "$INSTALLER_BUILDER" \
  --version "$VERSION" \
  --control-plane-image "$CONTROL_PLANE_IMAGE" \
  --source-commit "$SOURCE_COMMIT" \
  --source-date-epoch "$SOURCE_DATE_EPOCH" \
  --output-dir "$INSTALLER_DIR" \
  --cli-binary "$CLI_PATH"

INSTALLER_NAME="mem-installer-${VERSION}-ubuntu-24.04-amd64.tar.gz"
INSTALLER_PATH="$INSTALLER_DIR/$INSTALLER_NAME"
INSTALLER_SIDECAR="${INSTALLER_PATH}.sha256"
[[ -f "$INSTALLER_PATH" && -f "$INSTALLER_SIDECAR" ]] ||
  fail "03B installer builder did not produce the expected archive and sidecar."
(
  cd "$INSTALLER_DIR"
  sha256sum -c "$(basename "$INSTALLER_SIDECAR")" >/dev/null
) || fail "03B installer outer SHA-256 sidecar failed."

INSTALLER_VERIFY="$TMP_ROOT/installer-verify"
mkdir -p -- "$INSTALLER_VERIFY"

python3 - \
  "$INSTALLER_PATH" \
  "$INSTALLER_VERIFY" \
  "$VERSION" \
  "$SOURCE_COMMIT" \
  "$SOURCE_DATE_EPOCH" \
  "$CONTROL_PLANE_IMAGE" \
  "$CLI_PATH" <<'PY_VERIFY_INSTALLER'
import hashlib
import json
import pathlib
import sys
import tarfile
from pathlib import Path

archive_path, destination, version, source_commit, source_epoch, image_reference, cli_path = sys.argv[1:]
root = f"mem-installer-{version}-ubuntu-24.04-amd64"

def fail(message):
    raise SystemExit(f"ERROR: {message}")

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

required = {
    f"{root}/VERSION",
    f"{root}/BUILD-INFO.json",
    f"{root}/SHA256SUMS",
    f"{root}/install.sh",
    f"{root}/bootstrap/install.sh",
    f"{root}/bootstrap/release.env",
    f"{root}/bootstrap/cli/mem",
    f"{root}/bootstrap/cli/install-host-command.sh",
}
seen = set()

try:
    archive = tarfile.open(archive_path, "r:gz")
except (OSError, tarfile.TarError) as exc:
    fail(f"installer archive cannot be opened: {exc}")

with archive:
    for member in archive.getmembers():
        name = member.name.rstrip("/")
        path = pathlib.PurePosixPath(name)
        if (
            not name
            or path.is_absolute()
            or any(part in ("", ".", "..") for part in path.parts)
            or not path.parts
            or path.parts[0] != root
        ):
            fail(f"unsafe installer archive path: {member.name}")
        if name in seen:
            fail(f"duplicate installer archive path: {member.name}")
        seen.add(name)
        if not (member.isfile() or member.isdir()):
            fail(f"installer archive contains unsupported entry type: {member.name}")
    missing = required - seen
    if missing:
        fail("installer archive is missing: " + ", ".join(sorted(missing)))
    archive.extractall(destination, filter="data")

extracted = Path(destination) / root
if (extracted / "VERSION").read_text(encoding="utf-8").strip() != version:
    fail("installer VERSION mismatch")

with open(extracted / "BUILD-INFO.json", "r", encoding="utf-8") as handle:
    info = json.load(handle)
if info.get("product", {}).get("version") != version:
    fail("installer BUILD-INFO version mismatch")
if info.get("source", {}).get("commit") != source_commit:
    fail("installer BUILD-INFO source commit mismatch")
if info.get("source", {}).get("sourceDateEpoch") != int(source_epoch):
    fail("installer BUILD-INFO source timestamp mismatch")
if info.get("controlPlane", {}).get("image") != image_reference:
    fail("installer BUILD-INFO Control Plane image mismatch")
if info.get("platform") != {
    "os": "ubuntu",
    "version": "24.04",
    "architecture": "amd64",
}:
    fail("installer BUILD-INFO platform mismatch")

embedded_cli = extracted / "bootstrap/cli/mem"
if digest(embedded_cli) != digest(cli_path):
    fail("installer does not embed the exact standalone CLI bytes")
if info.get("cli", {}).get("sha256") != digest(cli_path):
    fail("installer BUILD-INFO CLI hash mismatch")

expected = {}
for raw in (extracted / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
    if not raw.strip():
        continue
    parts = raw.split(None, 1)
    if len(parts) != 2:
        fail("installer inner SHA256SUMS is malformed")
    sha, name = parts
    name = name.lstrip("*")
    if name in expected:
        fail("installer inner SHA256SUMS has duplicate paths")
    expected[name] = sha
for name, sha in expected.items():
    path = extracted / name
    if not path.is_file() or digest(path) != sha:
        fail(f"installer inner SHA256SUMS mismatch for {name}")
PY_VERIFY_INSTALLER

STAGING="$TMP_ROOT/public"
mkdir -p -- "$STAGING"

BOOTSTRAP_NAME="install-mem-${VERSION}.sh"
BOOTSTRAP_PATH="$STAGING/$BOOTSTRAP_NAME"
python3 - "$BOOTSTRAP_TEMPLATE" "$BOOTSTRAP_PATH" "$VERSION" <<'PY_RENDER_BOOTSTRAP'
import sys
from pathlib import Path

template_path, output_path, version = sys.argv[1:]
text = Path(template_path).read_text(encoding="utf-8")
text = text.replace("@@VERSION@@", version)
if "@@" in text:
    raise SystemExit("ERROR: unresolved public bootstrap template placeholder.")
Path(output_path).write_text(text, encoding="utf-8")
PY_RENDER_BOOTSTRAP
chmod 0755 "$BOOTSTRAP_PATH"

cp -a -- "$INSTALLER_PATH" "$STAGING/$INSTALLER_NAME"
cp -a -- "$CLI_PATH" "$STAGING/$CLI_NAME"
cp -a -- "$MIGRATE_DIR/$MIGRATE_BOOTSTRAP_NAME" "$STAGING/$MIGRATE_BOOTSTRAP_NAME"
cp -a -- "$MIGRATE_DIR/$MIGRATE_MANIFEST_NAME" "$STAGING/$MIGRATE_MANIFEST_NAME"
cp -a -- "$MIGRATE_DIR/$MIGRATE_ARCHIVE_NAME" "$STAGING/$MIGRATE_ARCHIVE_NAME"
cp -a -- "$MIGRATE_DIR/$MIGRATE_CHECKSUM_NAME" "$STAGING/$MIGRATE_CHECKSUM_NAME"
SBOM_NAME="mem-${VERSION}-sbom.spdx.json"
cp -a -- "$SBOM_PATH" "$STAGING/$SBOM_NAME"

python3 - \
  "$STAGING/release.json" \
  "$VERSION" \
  "$CHANNEL" \
  "$GENERATED_AT_UTC" \
  "$SOURCE_COMMIT" \
  "$CONTROL_PLANE_IMAGE" \
  "$STAGING/$BOOTSTRAP_NAME" \
  "$STAGING/$INSTALLER_NAME" \
  "$STAGING/$CLI_NAME" \
  "$STAGING/$MIGRATE_BOOTSTRAP_NAME" \
  "$STAGING/$MIGRATE_MANIFEST_NAME" \
  "$STAGING/$MIGRATE_ARCHIVE_NAME" \
  "$STAGING/$MIGRATE_CHECKSUM_NAME" \
  "$STAGING/$SBOM_NAME" <<'PY_MANIFEST'
import hashlib
import json
import os
import sys

(
    output_path,
    version,
    channel,
    generated_at,
    source_commit,
    image_reference,
    bootstrap_path,
    installer_path,
    cli_path,
    migrate_bootstrap_path,
    migrate_manifest_path,
    migrate_path,
    migrate_checksum_path,
    sbom_path,
) = sys.argv[1:]

def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def artifact(path, artifact_id, kind, **extra):
    item = {
        "id": artifact_id,
        "kind": kind,
        "fileName": os.path.basename(path),
        "sha256": digest(path),
        "sizeBytes": os.path.getsize(path),
        "sourceId": "control-plane",
    }
    item.update(extra)
    return item

image_repository = "ghcr.io/message-easy-mode/mem-control-plane"
image_tag, image_digest = image_reference.rsplit("@", 1)[0].rsplit(":", 1)[1], image_reference.rsplit("@", 1)[1]

document = {
    "schemaVersion": 1,
    "product": {
        "id": "mem",
        "name": "Message Easy Mode",
        "version": version,
        "channel": channel,
    },
    "release": {
        "generatedAtUtc": generated_at,
        "publication": {
            "provider": "github-releases",
            "repository": "https://github.com/message-easy-mode/mem-releases",
            "tag": f"v{version}",
        },
    },
    "sources": [
        {
            "id": "control-plane",
            "repository": "https://github.com/message-easy-mode/mem",
            "commit": source_commit,
        }
    ],
    "support": {
        "controlPlaneHosts": [
            {
                "os": "ubuntu",
                "version": "24.04",
                "architecture": "amd64",
            }
        ]
    },
    "controlPlane": {
        "sourceId": "control-plane",
        "image": {
            "repository": image_repository,
            "tag": image_tag,
            "digest": image_digest,
            "reference": image_reference,
        },
    },
    "artifacts": [
        artifact(bootstrap_path, "bootstrap", "bootstrap-script"),
        artifact(
            installer_path,
            "installer",
            "installer-bundle",
            platform={
                "os": "ubuntu",
                "version": "24.04",
                "architecture": "amd64",
            },
        ),
        artifact(cli_path, "cli", "cli-binary", runtime="linux-x64"),
        artifact(
            migrate_bootstrap_path,
            "migrate-bootstrap",
            "migrate-bootstrap-script",
        ),
        artifact(
            migrate_manifest_path,
            "migrate-manifest",
            "component-release-manifest",
        ),
        artifact(migrate_path, "migrate", "migrate-bundle", runtime="linux-x64"),
        artifact(
            migrate_checksum_path,
            "migrate-checksum",
            "sha256-sidecar",
        ),
        artifact(sbom_path, "sbom", "sbom-spdx-json"),
    ],
    "verification": {
        "checksums": {
            "algorithm": "sha256",
            "fileName": "SHA256SUMS",
        },
        "publication": {
            "provider": "github-releases",
            "immutableReleaseRequired": True,
            "releaseAttestationRequired": True,
        },
        "provenance": {
            "provider": "github-artifact-attestations",
            "fileArtifactsRequired": True,
            "containerImageRequired": True,
        },
    },
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY_MANIFEST

(
  cd "$STAGING"
  {
    printf '%s\n' \
      "$BOOTSTRAP_NAME" \
      "$CLI_NAME" \
      "$INSTALLER_NAME" \
      "$MIGRATE_ARCHIVE_NAME" \
      "$MIGRATE_BOOTSTRAP_NAME" \
      "$MIGRATE_CHECKSUM_NAME" \
      "$MIGRATE_MANIFEST_NAME" \
      "$SBOM_NAME" \
      "release.json" \
      | LC_ALL=C sort \
      | xargs sha256sum
  } > SHA256SUMS
)

python3 "$VALIDATOR" "$STAGING/release.json" --assets-dir "$STAGING" >/dev/null

# The final product manifest must describe the same clean commit that is still
# checked out after component validation/installer construction.
[[ "$(git -C "$REPO_ROOT" rev-parse HEAD)" == "$SOURCE_COMMIT" ]] ||
  fail "Repository HEAD changed during product release assembly."
if [[ -n "$(git -C "$REPO_ROOT" status --porcelain --untracked-files=all)" ]]; then
  fail "Repository worktree changed during product release assembly."
fi

rm -rf -- "$OUTPUT_DIR"
mkdir -p -- "$OUTPUT_DIR"
cp -a -- "$STAGING/." "$OUTPUT_DIR/"

printf '[release] Message Easy Mode product release set complete\n'
printf 'Version:        %s\n' "$VERSION"
printf 'Channel:        %s\n' "$CHANNEL"
printf 'Source commit:  %s\n' "$SOURCE_COMMIT"
printf 'Generated UTC:  %s\n' "$GENERATED_AT_UTC"
printf 'Control Plane:  %s\n' "$CONTROL_PLANE_IMAGE"
printf 'Output:         %s\n' "$OUTPUT_DIR"
printf 'Public assets:  10\n'
printf '\n'
printf 'Next publication gate: attest the image and release artifacts, create a draft\n'
printf 'GitHub Release, attach the complete validated set, then publish it only after\n'
printf 'release immutability is enabled for message-easy-mode/mem-releases.\n'
