#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
RELEASE_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
REPO_ROOT="$(cd -- "$RELEASE_DIR/../.." && pwd -P)"
DOCKERFILE="$REPO_ROOT/installer/Dockerfile"
VERIFIER="$RELEASE_DIR/verify-control-plane-image.sh"

PASSED=0
FAILED=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mem-image-release-tests.XXXXXX")"
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

[[ -f "$DOCKERFILE" ]] || { echo "Missing Dockerfile: $DOCKERFILE" >&2; exit 2; }
[[ -x "$VERIFIER" ]] || { echo "Missing verifier: $VERIFIER" >&2; exit 2; }

if grep -Fq 'MEM_MIGRATE_RELEASE_VERSION="${MEM_PRODUCT_VERSION}" npm run build' "$DOCKERFILE" &&
   [[ "$(grep -Fc -- '-p:InformationalVersion="${MEM_PRODUCT_VERSION}"' "$DOCKERFILE")" -ge 3 ]] &&
   grep -Fq 'org.opencontainers.image.source="https://github.com/message-easy-mode/mem"' "$DOCKERFILE" &&
   grep -Fq 'MEM_PRODUCT_VERSION=${MEM_PRODUCT_VERSION}' "$DOCKERFILE" &&
   grep -Fq 'MEM_COMMIT_SHA=${MEM_COMMIT_SHA}' "$DOCKERFILE"; then
  pass "Control Plane image build propagates exact MEM release identity through API, Migrate and OCI metadata"
else
  fail_test "Control Plane image build propagates exact MEM release identity through API, Migrate and OCI metadata"
fi

if grep -Fq 'RUN rm -rf ./src/Mem.Migrate.Web/wwwroot' "$DOCKERFILE" &&
   grep -Fq 'grep -RIql -- "${MEM_PRODUCT_VERSION}" ./src/Mem.Migrate.Web/wwwroot' "$DOCKERFILE" &&
   grep -Fq "! grep -RIql -- '0.2.0-alpha.1' ./src/Mem.Migrate.Web/wwwroot" "$DOCKERFILE"; then
  pass "Control Plane image build cleans stale Source Assistant assets and fails closed on development identity"
else
  fail_test "Control Plane image build cleans stale Source Assistant assets and fails closed on development identity"
fi

if grep -Fq 'ARG NODE_IMAGE=node:22-alpine' "$DOCKERFILE" &&
   grep -Fq 'ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim' "$DOCKERFILE" &&
   grep -Fq 'ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim' "$DOCKERFILE" &&
   grep -Fq 'ARG DOCKER_CLI_IMAGE=docker:29-cli' "$DOCKERFILE" &&
   grep -Fq 'SOURCE_DATE_EPOCH' "$DOCKERFILE" &&
   grep -Fq 'LC_ALL=C sort' "$DOCKERFILE"; then
  pass "Dockerfile exposes release-resolved base inputs and deterministic release metadata controls"
else
  fail_test "Dockerfile exposes release-resolved base inputs and deterministic release metadata controls"
fi

if grep -Fq 'FROM ${DOCKER_CLI_IMAGE} AS docker-cli' "$DOCKERFILE" &&
   grep -Fq 'apt-get install --yes --no-install-recommends age ca-certificates curl tzdata' "$DOCKERFILE" &&
   grep -Fq 'COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker' "$DOCKERFILE" &&
   grep -Fq 'age --version >/dev/null' "$DOCKERFILE" &&
   grep -Fq 'age-keygen --version >/dev/null' "$DOCKERFILE" &&
   grep -Fq 'docker --version >/dev/null' "$DOCKERFILE"; then
  pass "Control Plane runtime ships and build-verifies age, age-keygen and Docker CLI migration dependencies"
else
  fail_test "Control Plane runtime ships and build-verifies age, age-keygen and Docker CLI migration dependencies"
fi

if grep -Fq '> /out/combined/VERSION' "$DOCKERFILE" &&
   grep -Fq '> /out/combined/BUILD-INFO.json' "$DOCKERFILE" &&
   grep -Fq '"releaseVersion"' "$DOCKERFILE" &&
   grep -Fq '"sourceCommit"' "$DOCKERFILE" &&
   grep -Fq '/opt/mem/migrate/current/VERSION' "$DOCKERFILE" &&
   grep -Fq '/opt/mem/migrate/current/BUILD-INFO.json' "$DOCKERFILE"; then
  pass "Control Plane image retains and build-verifies embedded MEM Migrate release provenance"
else
  fail_test "Control Plane image retains and build-verifies embedded MEM Migrate release provenance"
fi

MOCK_BIN="$TMP_ROOT/bin"
mkdir -p "$MOCK_BIN"
cat > "$MOCK_BIN/docker" <<'MOCK'
#!/usr/bin/env bash
set -Eeuo pipefail

case "${1:-}" in
  pull)
    exit 0
    ;;
  image)
    [[ "${2:-}" == "inspect" ]] || exit 91
    revision="$EXPECTED_COMMIT"
    [[ "${MOCK_BAD_REVISION:-0}" != "1" ]] || revision="cccccccccccccccccccccccccccccccccccccccc"
    cat <<JSON
[
  {
    "RepoDigests": ["${EXPECTED_REPOSITORY}@${EXPECTED_DIGEST}"],
    "Os": "linux",
    "Architecture": "amd64",
    "Config": {
      "Labels": {
        "org.opencontainers.image.source": "https://github.com/message-easy-mode/mem",
        "org.opencontainers.image.version": "${EXPECTED_VERSION}",
        "org.opencontainers.image.revision": "${revision}",
        "org.opencontainers.image.created": "${EXPECTED_CREATED}"
      },
      "Env": [
        "MEM_PRODUCT_VERSION=${EXPECTED_VERSION}",
        "MEM_COMMIT_SHA=${EXPECTED_COMMIT}"
      ]
    }
  }
]
JSON
    ;;
  run)
    args="$*"
    if [[ "$args" == *'--entrypoint age-keygen'* ]]; then
      [[ "${MOCK_MISSING_NATIVE_TOOL:-}" != "age-keygen" ]] || exit 127
      printf '1.1.1\n'
    elif [[ "$args" == *'--entrypoint age '* ]]; then
      [[ "${MOCK_MISSING_NATIVE_TOOL:-}" != "age" ]] || exit 127
      printf '1.1.1\n'
    elif [[ "$args" == *'--entrypoint docker '* ]]; then
      [[ "${MOCK_MISSING_NATIVE_TOOL:-}" != "docker" ]] || exit 127
      printf 'Docker version 29.8.1, build mock\n'
    elif [[ "$args" == *'/mem-migrate-web'* ]]; then
      if [[ "${MOCK_BAD_WEB_VERSION:-0}" == "1" ]]; then
        printf '0.2.0-alpha.1\n'
      else
        printf '%s\n' "$EXPECTED_VERSION"
      fi
    elif [[ "$args" == *'/mem-migrate'* ]]; then
      printf '%s\n' "$EXPECTED_VERSION"
    else
      exit 92
    fi
    ;;
  create)
    printf 'mock-container-id\n'
    ;;
  cp)
    source_path="${2:-}"
    dest="${3:-}"
    case "$source_path" in
      *:/opt/mem/migrate/current/VERSION)
        [[ "${MOCK_MISSING_MIGRATE_METADATA:-}" != "VERSION" ]] || exit 1
        if [[ "${MOCK_BAD_MIGRATE_METADATA:-}" == "version" ]]; then
          printf '0.2.0-alpha.1\n' > "$dest"
        else
          printf '%s\n' "$EXPECTED_VERSION" > "$dest"
        fi
        ;;
      *:/opt/mem/migrate/current/BUILD-INFO.json)
        [[ "${MOCK_MISSING_MIGRATE_METADATA:-}" != "BUILD-INFO" ]] || exit 1
        build_version="$EXPECTED_VERSION"
        build_commit="$EXPECTED_COMMIT"
        [[ "${MOCK_BAD_MIGRATE_METADATA:-}" != "build-version" ]] || build_version="0.2.0-alpha.1"
        [[ "${MOCK_BAD_MIGRATE_METADATA:-}" != "commit" ]] || build_commit="cccccccccccccccccccccccccccccccccccccccc"
        cat > "$dest" <<JSON
{
  "schemaVersion": 1,
  "releaseVersion": "$build_version",
  "sourceCommit": "$build_commit"
}
JSON
        ;;
      *:/opt/mem/migrate/current/wwwroot)
        mkdir -p "$dest/assets"
        printf '<!doctype html><html></html>\n' > "$dest/index.html"
        printf 'MEM Migrate v%s\n' "$EXPECTED_VERSION" > "$dest/assets/app.js"
        if [[ "${MOCK_ALPHA_UI:-0}" == "1" ]]; then
          printf 'MEM Migrate v0.2.0-alpha.1\n' >> "$dest/assets/app.js"
        fi
        ;;
      *)
        exit 94
        ;;
    esac
    ;;
  rm)
    exit 0
    ;;
  *)
    echo "unsupported mock docker invocation: $*" >&2
    exit 93
    ;;
esac
MOCK
chmod 0755 "$MOCK_BIN/docker"

export EXPECTED_VERSION="0.2.0"
export EXPECTED_COMMIT="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
export EXPECTED_CREATED="2026-09-05T00:00:00Z"
export EXPECTED_DIGEST="sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
export EXPECTED_REPOSITORY="ghcr.io/message-easy-mode/mem-control-plane"
IMAGE="ghcr.io/message-easy-mode/mem-control-plane:${EXPECTED_VERSION}@${EXPECTED_DIGEST}"

if PATH="$MOCK_BIN:$PATH" "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED" >/dev/null; then
  pass "image verifier accepts exact digest, platform, native tools and embedded executable/provenance identity"
else
  fail_test "image verifier accepts exact digest, platform, native tools and embedded executable/provenance identity"
fi


LOCAL_REPOSITORY="registry.vs4.one/message-easy-mode/mem-control-plane"
LOCAL_IMAGE="${LOCAL_REPOSITORY}:${EXPECTED_VERSION}@${EXPECTED_DIGEST}"
if PATH="$MOCK_BIN:$PATH" EXPECTED_REPOSITORY="$LOCAL_REPOSITORY" "$VERIFIER" \
  --image "$LOCAL_IMAGE" \
  --expected-repository "$LOCAL_REPOSITORY" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED" >/dev/null; then
  pass "image verifier accepts an explicit private staging repository while preserving release metadata checks"
else
  fail_test "image verifier accepts an explicit private staging repository while preserving release metadata checks"
fi

expect_reject \
  "image verifier keeps canonical GHCR as the default repository" \
  "expected repository with exact version tag and digest" \
  env PATH="$MOCK_BIN:$PATH" EXPECTED_REPOSITORY="$LOCAL_REPOSITORY" \
  "$VERIFIER" \
  --image "$LOCAL_IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects a mutable/non-digest image reference" \
  "expected repository with exact version tag and digest" \
  env PATH="$MOCK_BIN:$PATH" \
  "$VERIFIER" \
  --image "ghcr.io/message-easy-mode/mem-control-plane:0.2.0" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects OCI revision drift" \
  "org.opencontainers.image.revision" \
  env PATH="$MOCK_BIN:$PATH" MOCK_BAD_REVISION=1 \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects embedded Migrate version drift" \
  "mem-migrate-web reports" \
  env PATH="$MOCK_BIN:$PATH" MOCK_BAD_WEB_VERSION=1 \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects a Control Plane image missing age-keygen" \
  "Embedded age-keygen --version failed" \
  env PATH="$MOCK_BIN:$PATH" MOCK_MISSING_NATIVE_TOOL=age-keygen \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects a Control Plane image missing Docker CLI" \
  "Embedded Docker CLI --version failed" \
  env PATH="$MOCK_BIN:$PATH" MOCK_MISSING_NATIVE_TOOL=docker \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects a Control Plane image missing embedded Migrate VERSION" \
  "Embedded MEM Migrate VERSION is missing" \
  env PATH="$MOCK_BIN:$PATH" MOCK_MISSING_MIGRATE_METADATA=VERSION \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects embedded Migrate provenance source-commit drift" \
  "BUILD-INFO.json sourceCommit expected" \
  env PATH="$MOCK_BIN:$PATH" MOCK_BAD_MIGRATE_METADATA=commit \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

expect_reject \
  "image verifier rejects the old alpha Source Assistant identity in a stable image" \
  "still expose development identity" \
  env PATH="$MOCK_BIN:$PATH" MOCK_ALPHA_UI=1 \
  "$VERIFIER" \
  --image "$IMAGE" \
  --version "$EXPECTED_VERSION" \
  --source-commit "$EXPECTED_COMMIT" \
  --created "$EXPECTED_CREATED"

printf '\nControl Plane image release tests: %d passed, %d failed\n' "$PASSED" "$FAILED"
[[ "$FAILED" -eq 0 ]]
