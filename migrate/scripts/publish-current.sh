#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/publish-current.sh --output <path> [--runtime linux-x64] [--configuration Release] [--version <semver>]

Builds the React Source Assistant, publishes both self-contained executables,
and creates one installable payload containing:
  mem-migrate
  mem-migrate-web
  wwwroot/
  required runtime companion files
USAGE
}

fail() { echo "ERROR: $*" >&2; exit 1; }

OUTPUT_DIR=""
RUNTIME="linux-x64"
CONFIGURATION="Release"
RELEASE_VERSION=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output) [[ $# -ge 2 ]] || fail "--output requires a path"; OUTPUT_DIR="$2"; shift 2 ;;
    --runtime) [[ $# -ge 2 ]] || fail "--runtime requires a value"; RUNTIME="$2"; shift 2 ;;
    --configuration) [[ $# -ge 2 ]] || fail "--configuration requires a value"; CONFIGURATION="$2"; shift 2 ;;
    --version) [[ $# -ge 2 ]] || fail "--version requires a value"; RELEASE_VERSION="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Unknown argument: $1" ;;
  esac
done

[[ -n "$OUTPUT_DIR" ]] || fail "--output is required"
if [[ -n "$RELEASE_VERSION" ]]; then
  [[ "$RELEASE_VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]] ||
    fail "--version must be an ordinary semantic version or prerelease version."
fi
command -v dotnet >/dev/null 2>&1 || fail "dotnet is required"
command -v npm >/dev/null 2>&1 || fail "npm is required"

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
OUTPUT_DIR="$(realpath -m -- "$OUTPUT_DIR")"
[[ "$OUTPUT_DIR" != "/" ]] || fail "Refusing to use the filesystem root as output"
[[ "$OUTPUT_DIR" != "$ROOT_DIR" ]] || fail "Refusing to overwrite the source root"

TEMP_ROOT="$(mktemp -d /tmp/mem-migrate-publish.XXXXXX)"
trap 'rm -rf -- "$TEMP_ROOT"' EXIT
CLI_DIR="$TEMP_ROOT/cli"
WEB_DIR="$TEMP_ROOT/web"
CLIENT_DIR="$ROOT_DIR/src/Mem.Migrate.Web/ClientApp"

printf '%s\n' "Building Source Assistant client..."
(
  cd "$CLIENT_DIR"
  npm ci --no-audit --no-fund
  if [[ -n "$RELEASE_VERSION" ]]; then
    MEM_MIGRATE_RELEASE_VERSION="$RELEASE_VERSION" npm run build
  else
    npm run build
  fi
)

VERSION_ARGS=()
if [[ -n "$RELEASE_VERSION" ]]; then
  IFS='.' read -r version_major version_minor version_patch_rest <<< "$RELEASE_VERSION"
  version_patch="${version_patch_rest%%-*}"
  assembly_version="${version_major}.${version_minor}.${version_patch}.0"
  VERSION_ARGS=(
    "-p:Version=$RELEASE_VERSION"
    "-p:AssemblyVersion=$assembly_version"
    "-p:FileVersion=$assembly_version"
    "-p:InformationalVersion=$RELEASE_VERSION"
    "-p:Product=MEM Migrate"
    "-p:Company=Message Easy Mode"
    "-p:IncludeSourceRevisionInInformationalVersion=false"
  )
fi

printf '%s\n' "Publishing mem-migrate CLI..."
dotnet publish "$ROOT_DIR/src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  "${VERSION_ARGS[@]}" \
  --output "$CLI_DIR"

printf '%s\n' "Publishing mem-migrate Source Assistant..."
dotnet publish "$ROOT_DIR/src/Mem.Migrate.Web/Mem.Migrate.Web.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  "${VERSION_ARGS[@]}" \
  --output "$WEB_DIR"

rm -rf -- "$OUTPUT_DIR"
mkdir -p -- "$OUTPUT_DIR"
cp -a "$CLI_DIR/." "$OUTPUT_DIR/"

while IFS= read -r -d '' source_path; do
  relative_path="${source_path#"$WEB_DIR/"}"
  destination_path="$OUTPUT_DIR/$relative_path"
  if [[ -e "$destination_path" ]]; then
    if [[ -f "$source_path" && -f "$destination_path" ]] && cmp -s -- "$source_path" "$destination_path"; then
      continue
    fi
    fail "Publish payload collision: $relative_path"
  fi

  if [[ -d "$source_path" ]]; then
    mkdir -p -- "$destination_path"
  else
    mkdir -p -- "$(dirname -- "$destination_path")"
    cp -a -- "$source_path" "$destination_path"
  fi
done < <(find "$WEB_DIR" -mindepth 1 -print0 | sort -z)

[[ -x "$OUTPUT_DIR/mem-migrate" ]] || fail "Combined payload is missing executable mem-migrate"
[[ -x "$OUTPUT_DIR/mem-migrate-web" ]] || fail "Combined payload is missing executable mem-migrate-web"
[[ -f "$OUTPUT_DIR/libe_sqlite3.so" ]] || fail "Combined payload is missing libe_sqlite3.so"
[[ -f "$OUTPUT_DIR/wwwroot/index.html" ]] || fail "Combined payload is missing Source Assistant static assets"

printf '%s\n' "Combined self-contained payload: $OUTPUT_DIR"
