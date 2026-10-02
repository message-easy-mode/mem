#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"

RELEASE_DIR=""
STAGING_IMAGE=""
OUTPUT_DIR=""
REPLACE=false

usage() {
  cat <<'USAGE'
Package an already-assembled exact MEM release plus private-registry QA launch data.

Usage:
  ./scripts/release/package-local-qa-release.sh \
    --release-dir ./artifacts/product-release \
    --staging-image \
      registry.vs4.one/message-easy-mode/mem-control-plane:0.2.0-rc.1@sha256:<digest> \
    --output-dir ./artifacts/local-qa \
    [--replace]

The input release directory must be the normal exact schema-v1 public release set.
It is validated without modification. The staging image must use the same exact
version tag and image digest recorded by release.json.

Output:
  mem-local-qa-<version>.tar.gz
  mem-local-qa-<version>.tar.gz.sha256

Archive layout:
  mem-local-qa-<version>/
    release/                         exact unmodified ten-file release set
    STAGING-CONTROL-PLANE-IMAGE      private registry digest reference
    install-local.sh                 local QA convenience launcher
    QA-SHA256SUMS                    checks the QA-only wrapper files

The QA launcher invokes the normal versioned release bootstrap with --release-dir
and passes --control-plane-image to the already-verified bundled installer. It
does not rewrite release.json, the installer bundle, or public SHA256SUMS.
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
    --release-dir)
      [[ $# -ge 2 ]] || fail "--release-dir requires a path."
      RELEASE_DIR="$2"
      shift 2
      ;;
    --staging-image)
      [[ $# -ge 2 ]] || fail "--staging-image requires a digest-bearing image reference."
      STAGING_IMAGE="$2"
      shift 2
      ;;
    --output-dir)
      [[ $# -ge 2 ]] || fail "--output-dir requires a path."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --replace)
      REPLACE=true
      shift
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

[[ -n "$RELEASE_DIR" ]] || fail "--release-dir is required."
[[ -n "$STAGING_IMAGE" ]] || fail "--staging-image is required."
[[ -n "$OUTPUT_DIR" ]] || fail "--output-dir is required."

for command_name in python3 realpath mktemp rm mkdir cp chmod sha256sum tar gzip grep dirname awk basename; do
  require_command "$command_name"
done

VALIDATOR="$SCRIPT_DIR/validate-release-manifest.py"
[[ -f "$VALIDATOR" ]] || fail "Release manifest validator is missing: $VALIDATOR"

RELEASE_DIR="$(realpath -e -- "$RELEASE_DIR")" || fail "Release directory does not exist."
[[ -d "$RELEASE_DIR" ]] || fail "--release-dir is not a directory: $RELEASE_DIR"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
mkdir -p -- "$OUTPUT_DIR"

printf '[qa] Validating exact public release asset set...\n'
python3 "$VALIDATOR" "$RELEASE_DIR/release.json" --assets-dir "$RELEASE_DIR" >/dev/null
(
  cd "$RELEASE_DIR"
  sha256sum -c SHA256SUMS >/dev/null
) || fail "Public release SHA256SUMS verification failed."

IFS=$'\t' read -r VERSION CANONICAL_DIGEST INSTALLER_NAME < <(
  python3 - "$RELEASE_DIR/release.json" <<'PY'
import json, re, sys
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    doc = json.load(handle)
version = str(doc.get("product", {}).get("version", ""))
digest = str(doc.get("controlPlane", {}).get("image", {}).get("digest", ""))
installer = ""
for artifact in doc.get("artifacts", []):
    if artifact.get("id") == "installer":
        installer = str(artifact.get("fileName", ""))
        break
if re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?", version) is None:
    raise SystemExit("ERROR: release.json version is not ordinary stable/prerelease SemVer")
if re.fullmatch(r"sha256:[0-9a-f]{64}", digest) is None:
    raise SystemExit("ERROR: release.json Control Plane digest is invalid")
if not installer:
    raise SystemExit("ERROR: release.json does not declare the installer artifact")
print(f"{version}\t{digest}\t{installer}")
PY
)

IFS=$'\t' read -r STAGING_REPOSITORY STAGING_TAG STAGING_DIGEST < <(
  python3 - "$STAGING_IMAGE" <<'PY'
import re, sys
value = sys.argv[1]
if "://" in value or any(ch.isspace() for ch in value):
    raise SystemExit("ERROR: staging image must be a Docker repository reference, not a URL")
try:
    tagged, digest = value.rsplit("@", 1)
    repository, tag = tagged.rsplit(":", 1)
except ValueError:
    raise SystemExit("ERROR: staging image must be <repository>:<version>@sha256:<digest>")
if "/" not in repository:
    raise SystemExit("ERROR: staging image repository must include registry and repository path")
if re.fullmatch(r"sha256:[0-9a-f]{64}", digest) is None:
    raise SystemExit("ERROR: staging image digest must be immutable SHA-256")
print(f"{repository}\t{tag}\t{digest}")
PY
)

[[ "$STAGING_TAG" == "$VERSION" ]] || \
  fail "Staging image tag '$STAGING_TAG' does not match release version '$VERSION'."
[[ "$STAGING_DIGEST" == "$CANONICAL_DIGEST" ]] || \
  fail "Staging image digest does not match the immutable digest recorded by release.json."

BOOTSTRAP_PATH="$RELEASE_DIR/install-mem-${VERSION}.sh"
[[ -f "$BOOTSTRAP_PATH" && ! -L "$BOOTSTRAP_PATH" ]] || \
  fail "Versioned release bootstrap is missing or unsafe: $BOOTSTRAP_PATH"
if ! grep -Fq -- '--release-dir' "$BOOTSTRAP_PATH"; then
  fail "Versioned release bootstrap does not support --release-dir; rebuild the release assets after applying this staging slice."
fi

INSTALLER_PATH="$RELEASE_DIR/$INSTALLER_NAME"
[[ -f "$INSTALLER_PATH" ]] || fail "Installer artifact is missing: $INSTALLER_PATH"
INSTALLER_ROOT="mem-installer-${VERSION}-ubuntu-24.04-amd64"
if ! tar -xOf "$INSTALLER_PATH" "$INSTALLER_ROOT/bootstrap/install.sh" 2>/dev/null | \
     grep -Fq -- '--control-plane-image'; then
  fail "Bundled installer does not advertise the explicit --control-plane-image QA override."
fi

KIT_NAME="mem-local-qa-${VERSION}"
BUNDLE_PATH="$OUTPUT_DIR/${KIT_NAME}.tar.gz"
SIDECAR_PATH="${BUNDLE_PATH}.sha256"
if [[ -e "$BUNDLE_PATH" || -e "$SIDECAR_PATH" ]]; then
  [[ "$REPLACE" == true ]] || fail "QA bundle already exists. Use --replace to replace disposable QA transport output."
  rm -f -- "$BUNDLE_PATH" "$SIDECAR_PATH"
fi

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-local-qa-package.XXXXXX")"
trap 'rm -rf -- "$TMP_ROOT"' EXIT INT TERM
KIT_ROOT="$TMP_ROOT/$KIT_NAME"
mkdir -p -- "$KIT_ROOT/release"
cp -a -- "$RELEASE_DIR/." "$KIT_ROOT/release/"
printf '%s\n' "$STAGING_IMAGE" > "$KIT_ROOT/STAGING-CONTROL-PLANE-IMAGE"
chmod 0644 "$KIT_ROOT/STAGING-CONTROL-PLANE-IMAGE"

cat > "$KIT_ROOT/install-local.sh" <<EOF_INSTALL
#!/usr/bin/env bash
set -Eeuo pipefail
IFS=\$'\\n\\t'

SCRIPT_DIR="\$(cd -- "\$(dirname -- "\${BASH_SOURCE[0]}")" && pwd -P)"

(
  cd "\$SCRIPT_DIR"
  sha256sum -c QA-SHA256SUMS >/dev/null
  cd release
  sha256sum -c SHA256SUMS >/dev/null
) || {
  printf 'ERROR: copied MEM QA kit checksum verification failed.\\n' >&2
  exit 1
}

STAGING_IMAGE="\$(cat "\$SCRIPT_DIR/STAGING-CONTROL-PLANE-IMAGE")"

printf '[qa] Installing Message Easy Mode ${VERSION} from copied local release assets.\\n'
printf '[qa] Control Plane image: %s\\n' "\$STAGING_IMAGE"

exec "\$SCRIPT_DIR/release/install-mem-${VERSION}.sh" \\
  --release-dir "\$SCRIPT_DIR/release" \\
  --control-plane-image "\$STAGING_IMAGE" \\
  "\$@"
EOF_INSTALL
chmod 0755 "$KIT_ROOT/install-local.sh"

(
  cd "$KIT_ROOT"
  sha256sum \
    STAGING-CONTROL-PLANE-IMAGE \
    install-local.sh \
    release/SHA256SUMS > QA-SHA256SUMS
)
chmod 0644 "$KIT_ROOT/QA-SHA256SUMS"

# The transport archive is QA-only. Deterministic archive metadata keeps repeat
# packaging of the same release+staging reference stable and easy to compare.
(
  cd "$TMP_ROOT"
  LC_ALL=C tar \
    --sort=name \
    --mtime='UTC 1970-01-01' \
    --owner=0 \
    --group=0 \
    --numeric-owner \
    -cf - "$KIT_NAME" | gzip -n > "$BUNDLE_PATH"
)

BUNDLE_SHA="$(sha256sum "$BUNDLE_PATH" | awk '{print $1}')"
printf '%s  %s\n' "$BUNDLE_SHA" "$(basename -- "$BUNDLE_PATH")" > "$SIDECAR_PATH"
chmod 0644 "$BUNDLE_PATH" "$SIDECAR_PATH"

printf '\nMEM local QA release package ready\n'
printf 'Version:        %s\n' "$VERSION"
printf 'Release assets: %s\n' "$RELEASE_DIR"
printf 'Staging image:  %s\n' "$STAGING_IMAGE"
printf 'Bundle:         %s\n' "$BUNDLE_PATH"
printf 'Bundle SHA256:  %s\n' "$BUNDLE_SHA"
