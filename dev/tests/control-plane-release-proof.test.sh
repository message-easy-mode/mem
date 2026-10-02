#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROOF="${REPO_ROOT}/dev/control-plane-release-proof"

fail_test() { echo "FAIL: $*" >&2; exit 1; }
assert_contains() { grep -Fq -- "$2" "$1" || fail_test "${1#${REPO_ROOT}/} missing expected text: $2"; }
assert_not_contains() { ! grep -Fq -- "$2" "$1" || fail_test "${1#${REPO_ROOT}/} contains retired text: $2"; }

[[ -x "$PROOF" ]] || fail_test "release-proof helper is not executable"
bash -n "$PROOF"
assert_contains "$PROOF" 'capture-legacy'
assert_contains "$PROOF" 'verify-rollback'
assert_contains "$PROOF" 'verify-canonical'
assert_contains "$PROOF" 'verify-private-admin'
assert_contains "$PROOF" 'classify_private_admin_binding'
assert_contains "$PROOF" 'certificate_sha256'
assert_contains "$PROOF" "sha256sum \"\$path\" 2>/dev/null | awk '{print \$1}'"
assert_not_contains "$PROOF" "awk '{print \\\$1}'"
assert_contains "$PROOF" 'json_field_optional'
assert_contains "$PROOF" 'instance_id="$(json_field_optional "$runtime" controlPlaneInstanceId)"'
assert_contains "$PROOF" 'No setup token, password, private key, recovery code or database content was recorded.'
assert_not_contains "${REPO_ROOT}/install.sh" 'Matrix Easy Mode root installer entrypoint.'
assert_contains "${REPO_ROOT}/install.sh" 'Message Easy Mode root installer entrypoint.'

"$PROOF" source-audit >/dev/null

# Reproduce a legacy runtime whose /health/runtime request returns HTTP 200 but
# a non-JSON body. Capture must still succeed because legacy instance identity
# is optional evidence; canonical verification remains strict.
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/bin" "$TMP/evidence"
REAL_PYTHON="$(command -v python3)"
REAL_AWK="$(command -v awk)"
REAL_DATE="$(command -v date)"
REAL_MKDIR="$(command -v mkdir)"
REAL_CHMOD="$(command -v chmod)"
ln -s "$REAL_PYTHON" "$TMP/bin/python3"
ln -s "$REAL_AWK" "$TMP/bin/awk"
ln -s "$REAL_DATE" "$TMP/bin/date"
ln -s "$REAL_MKDIR" "$TMP/bin/mkdir"
ln -s "$REAL_CHMOD" "$TMP/bin/chmod"
cat > "$TMP/bin/curl" <<'CURL'
#!/usr/bin/env bash
printf 'Healthy'
CURL
chmod +x "$TMP/bin/curl"
cat > "$TMP/bin/docker" <<'DOCKER'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  info) exit 0 ;;
  container)
    [[ "$2" == inspect ]]
    if [[ "$3" == mem-installer ]]; then exit 0; fi
    if [[ "$3" == mem-control-plane && "${FIXTURE_CANONICAL:-false}" == true ]]; then exit 0; fi
    exit 1
    ;;
  inspect)
    if [[ "$2" == --format ]]; then
      fmt="$3"; name="$4"
      if [[ "$name" == mem-installer ]]; then
        case "$fmt" in
          '{{.State.Running}}') printf 'true\n' ;;
          '{{.Config.Image}}') printf 'mem-installer:local\n' ;;
          *'.Destination "/data"'*) printf 'mem-installer-data\n' ;;
          *'NetworkSettings.Ports'*) printf '%s\n' "${FIXTURE_LEGACY_BINDINGS:-0.0.0.0|8443}" ;;
          *) exit 1 ;;
        esac
        exit 0
      fi
      if [[ "$name" == mem-control-plane && "${FIXTURE_CANONICAL:-false}" == true ]]; then
        case "$fmt" in
          '{{.State.Running}}') printf 'true\n' ;;
          *'.Destination "/data"'*) printf 'mem-installer-data\n' ;;
          *'NetworkSettings.Ports'*) printf '%b\n' "${FIXTURE_BINDINGS:-127.0.0.1|8443}" ;;
          *) exit 1 ;;
        esac
        exit 0
      fi
    fi
    exit 1
    ;;
  run)
    if [[ "$*" == *'sha256sum'* ]]; then
      printf 'abc123  /data/certs/mem-installer.crt\n'
    fi
    exit 0
    ;;
  image) exit 1 ;;
  volume) exit 1 ;;
  ps) exit 0 ;;
  *) exit 1 ;;
esac
DOCKER
chmod +x "$TMP/bin/docker"

PATH="$TMP/bin:/usr/bin:/bin" "$PROOF" capture-legacy "$TMP/evidence/legacy.tsv" >/dev/null
[[ -f "$TMP/evidence/legacy.tsv" ]] || fail_test "capture did not write evidence"
grep -Fxq $'schema\t1' "$TMP/evidence/legacy.tsv" || fail_test "capture evidence schema missing"
grep -Fxq $'https_host_ip\t0.0.0.0' "$TMP/evidence/legacy.tsv" || fail_test "legacy HostIp evidence missing"
grep -Fxq $'https_binding\t0.0.0.0:8443' "$TMP/evidence/legacy.tsv" || fail_test "legacy HTTPS binding evidence missing"
grep -Fxq $'control_plane_instance_id\t' "$TMP/evidence/legacy.tsv" || fail_test "non-JSON legacy runtime should yield blank optional instance identity"

# Private-administration release proof uses synthetic Docker inspection data.
# It must accept the two supported private modes and fail closed for wildcard,
# public and multiple bindings without ever exposing a real development port.
FIXTURE_CANONICAL=true FIXTURE_BINDINGS='127.0.0.1|8443' \
  PATH="$TMP/bin:/usr/bin:/bin" "$PROOF" verify-private-admin mem-control-plane ssh-tunnel 127.0.0.1 >/dev/null
FIXTURE_CANONICAL=true FIXTURE_BINDINGS='192.168.10.20|8443' \
  PATH="$TMP/bin:/usr/bin:/bin" "$PROOF" verify-private-admin mem-control-plane trusted-lan 192.168.10.20 >/dev/null

for unsafe in '0.0.0.0|8443' '8.8.8.8|8443' $'127.0.0.1|8443\n0.0.0.0|8443'; do
  set +e
  FIXTURE_CANONICAL=true FIXTURE_BINDINGS="$unsafe" \
    PATH="$TMP/bin:/usr/bin:/bin" "$PROOF" verify-private-admin mem-control-plane >/dev/null 2>&1
  status=$?
  set -e
  [[ $status -ne 0 ]] || fail_test "unsafe private-administration fixture unexpectedly passed: $unsafe"
done

echo "PASS: Control Plane runtime and private-administration release-proof contracts"
