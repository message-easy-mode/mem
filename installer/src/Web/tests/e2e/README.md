# MEM browser smoke tests

## Canonical dual-environment proof

```bash
./dev/mem-env e2e local
./dev/mem-env e2e container
./dev/mem-env e2e all
```

The harness creates disposable authority/state, verifies `/health/runtime` before browser mutation, runs the same shared journey against Vite and the embedded SPA, restarts the API, verifies stable Control Plane identity plus rotated process identity, and deletes E2E state. Mode mismatch aborts before bootstrap.

Playwright browser verification now uses the named-operator security model.
It never uses the retired browser `/unlock` route or the generic installer
transition cookie.

The browser test runner does **not** keep screenshots, traces, or video:
the fresh-dev suite momentarily renders one-time recovery codes as part of the
real first-owner workflow. Never share or commit `test-results/`, Playwright
reports, browser profiles, environment files, TOTP secrets, or recovery codes.

## Repository path used by manual examples

From anywhere inside the MEM checkout, initialize the repository-root variable once in each shell that will run commands from this guide:

```bash
export MEM_REPO_ROOT="$(git rev-parse --show-toplevel)"
```

The examples below use `MEM_REPO_ROOT` so they remain portable across developer workstations.

## 1. Development browser smoke — current Vite source tree

This suite is deliberately a full auth-and-proxy proof:

```text
fresh disposable no-owner database
  → scoped first-owner bootstrap code
  → random named Platform Owner
  → calculated local TOTP verification
  → recovery-code acknowledgement
  → Home dashboard via real Vite /api proxy
  → visible sign out
  → password + TOTP login
  → Home dashboard again via real Vite /api proxy
  → Platform Owner operator inventory
  → Backup Catalog via real Vite /api and /internal proxy
```

It creates a first owner and recovery codes. Therefore it **must only** target
a new disposable database that is destroyed after the run.

### Terminal 1 — disposable API database

From the repository root:

```bash
rm -rf installer/data/sec-auth-e2e-browser

ASPNETCORE_ENVIRONMENT=Development \
Aio__SqlitePath="data/sec-auth-e2e-browser/mem.db" \
DataProtection__KeyRingPath="data/sec-auth-e2e-browser/keys" \
ASPNETCORE_URLS="http://127.0.0.1:7105" \
dotnet run --project ./installer/src/Api/Api.csproj
```

### Terminal 2 — current Vite source

```bash
cd installer/src/Web
npm run dev
```

### Terminal 3 — browser smoke

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

MEM_E2E_BASE_URL="http://localhost:5173" \
MEM_E2E_SETUP_TOKEN="mem_test123" \
npm run test:e2e:dev
```

The test creates its own random owner username and strong password. It
calculates the TOTP from the ephemeral first-owner setup screen but does not
log or save the TOTP secret or recovery codes.

When the test is complete, stop the API and remove the entire disposable state:

```bash
rm -rf "$MEM_REPO_ROOT/installer/data/sec-auth-e2e-browser"
rm -rf "$MEM_REPO_ROOT/installer/src/Web/test-results"
rm -rf "$MEM_REPO_ROOT/installer/src/Web/playwright-report"
```

Do this even though screenshots, traces, and video are disabled: the disposable
identity database/key ring and any browser-test output are not project artifacts.

## 2. Deployed browser smoke — rebuilt installed bundle

Run this only on a **non-production test environment** after its MEM web bundle
has been rebuilt from the current source.

Use a dedicated named test operator. The credentials and Base32 TOTP secret are
provided through the process environment only; do not place them in shell
history, repository files, screenshots, test reports, or chat messages.

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

MEM_E2E_BASE_URL="https://127.0.0.1:8443" \
MEM_E2E_OPERATOR_USERNAME="dedicated-e2e-operator" \
MEM_E2E_OPERATOR_PASSWORD="<dedicated-test-password>" \
MEM_E2E_OPERATOR_TOTP_SECRET="<base32-totp-secret>" \
npm run test:e2e:deployed
```

The deployed suite signs in through the real password and TOTP UI, proves the
Home dashboard receives its authenticated no-store overview through the deployed
browser route, opens the Backup Catalog, opens the first detail page when one
exists, and signs out. It performs no backup mutation.

## General commands

```bash
npm run test:e2e:dev       # fresh disposable local auth + Vite proxy proof
npm run test:e2e:deployed  # explicit named-operator deployed test proof
npm run test:e2e:ui        # Playwright UI runner; never use with real secrets
npx playwright show-report # inspect only a report known to contain no secrets
```

Do not run `npm run test:e2e` blindly: the explicit environment-backed suites
are designed to run through their dedicated commands.

## 3. Live TURN lifecycle proof — explicit mutating run

`STACK-TURN-LIVE-01` is a deliberately mutating proof for a named test stack.
It validates the real platform Coturn check and then drives the stack through:

```text
current safe state
  → Connected + MEM-managed
  → connected-state native backup
  → Backup Catalog record and original migration baseline still present
  → Not connected
  → explicit requested final state
```

The proof may rewrite `homeserver.yaml`, restart Synapse, and interrupt active
calls. Use it only on the approved `tester` or another non-production proof
stack, outside active use. It refuses external, unknown, and unreconcilable
drift states rather than attempting to remove settings MEM does not own.

The command requires an exact mutation acknowledgement and an explicit final
state. It may target the current Vite source through the real `/api` and
`/internal` proxy or a rebuilt installed bundle.

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

MEM_E2E_BASE_URL="http://localhost:5173" \
MEM_E2E_OPERATOR_USERNAME="dedicated-e2e-operator" \
MEM_E2E_OPERATOR_PASSWORD="<dedicated-test-password>" \
MEM_E2E_OPERATOR_TOTP_SECRET="<base32-totp-secret>" \
MEM_E2E_TURN_STACK_SLUG="tester" \
MEM_E2E_TURN_BASELINE_BACKUP_ID="<accepted-migration-baseline-backup-id>" \
MEM_E2E_TURN_FINAL_STATE="connected" \
MEM_E2E_TURN_MUTATION_ACK="I_UNDERSTAND_TURN_PROOF_MUTATES_STACK" \
npm run test:e2e:turn-live
```

`MEM_E2E_TURN_FINAL_STATE` must be exactly `connected` or `not-connected`.
The test prints only sanitized evidence identifiers: stack slug, initial and
final states, operation IDs, connected backup ID, baseline backup ID, and
platform readiness. It does not print credentials, recovery codes, TURN
shared secrets, complete YAML, host secret paths, screenshots, traces, or
video.

If the run fails after a mutation, inspect the authoritative Stack Services
TURN state before retrying. The test does not blindly force cleanup from an
external, unknown, or unresolved drift state.

A passing local allocation probe proves Coturn accepted short-lived
credentials on the local path. An optional external-network allocation or
client-call proof remains manual evidence and should be recorded separately.


## 4. Guided Migration baseline proof — explicit two-server mutating run

`MIG-UX-E2E-01` preserves the current guided Migration behaviour before the
Migration Session persistence and list contracts are changed. It is a live,
human-assisted, two-server proof. It creates a normal target server, publishes
its Matrix and Element routes, accepts the migration, and creates the first
native Backup Catalog item.

Do not run this against a production source or target. Use an approved MEM
0.1.0 source server and a non-production MEM 0.2.0 target during a maintenance
window. The target stack slug must be unused. The Matrix hostname must match the
source identity, the intended Element hostname must be correct, and DNS,
certificate, NPM, Docker, PostgreSQL, and backup prerequisites must already be
ready. Existing public-route state must be understood and safe for the planned
cutover. The operator must be prepared to inspect durable state if any
operation fails.

The test deliberately has no retry. Repeating a failed live migration blindly
could create a second runtime or repeat a public route mutation.

### Proof boundary

The browser drives the real operator journey:

```text
create secure Migration Session
  → display the source-side mem-migrate command
  → wait for the operator to transfer the encrypted package
  → upload and validate the package
  → review the detected MEM 0.1.0 source
  → prove reload/re-entry and the Guide/Advanced boundary
  → convert the package
  → run the private test
  → create the private normal server
  → make the server live and pass public verification
  → finish and accept the migration
  → wait for the baseline native backup
  → download and inspect the completion report
  → open the linked Backup Catalog item
  → reopen the completed Migration Session from the list
```

The proof uses the operator-attested simplified assurance path. It does not
freeze the source or create a second final package. The old source server and
source-side `mem-migrate` journal remain outside target-side deletion or cleanup.

### Prepare a consistent database backup

The default local development database is
`installer/data/mem-installer.dev.db`. If `Aio:SqlitePath` is overridden, use
the actual configured path instead.

```bash
cd "$MEM_REPO_ROOT"

STAMP=$(date -u +%Y%m%d-%H%M%S)
DB="installer/data/mem-installer.dev.db"
EVIDENCE="$HOME/Downloads/mig-ux-e2e-01-$STAMP"
mkdir -p "$EVIDENCE"

sqlite3 "$DB" ".timeout 5000" ".backup '$EVIDENCE/mem-installer-before.db'"
sha256sum "$EVIDENCE/mem-installer-before.db" \
  | tee "$EVIDENCE/mem-installer-before.db.sha256"
```

Before starting the test, manually capture the current `/migrations` list as a
screenshot. Do not capture a screen containing credentials, recovery codes,
TOTP secrets, an age private identity, or decrypted package contents.

### Prepare the package handoff directory

The directory must already exist and contain no `.memmigration.zip.age` file.
The test refuses an ambiguous or stale package handoff.

```bash
PACKAGE_DROP="/tmp/mem-migration-e2e-package"
rm -rf "$PACKAGE_DROP"
mkdir -m 700 -p "$PACKAGE_DROP"
```

### Run the focused live proof

Use either the current Vite source through its real `/api` and `/internal`
proxies or a rebuilt installed bundle. The example below uses Vite.

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"
set -o pipefail

MEM_E2E_BASE_URL="http://localhost:5173" \
MEM_E2E_OPERATOR_USERNAME="dedicated-e2e-operator" \
MEM_E2E_OPERATOR_PASSWORD="<dedicated-test-password>" \
MEM_E2E_OPERATOR_TOTP_SECRET="<base32-totp-secret>" \
MEM_E2E_MIGRATION_EXPECTED_MATRIX_HOST="matrix.example.test" \
MEM_E2E_MIGRATION_TARGET_STACK_SLUG="migration-proof" \
MEM_E2E_MIGRATION_ELEMENT_HOST="element.example.test" \
MEM_E2E_MIGRATION_PACKAGE_DROP_DIR="/tmp/mem-migration-e2e-package" \
MEM_E2E_MIGRATION_RETENTION_DAYS="14" \
MEM_E2E_MIGRATION_PACKAGE_WAIT_MINUTES="30" \
MEM_E2E_MIGRATION_DISPLAY_NAME="MIG-UX-E2E-01 baseline proof" \
MEM_E2E_MIGRATION_MUTATION_ACK="I_UNDERSTAND_MIGRATION_PROOF_CREATES_AND_PUBLISHES_A_SERVER" \
npm run test:e2e:migration-live \
  | tee "$EVIDENCE/playwright-migration-live.log"
```

When the test prints `SOURCE PACKAGE HANDOFF`, run the exact generated command
on the approved MEM 0.1.0 source server. Copy the newly generated encrypted
`.memmigration.zip.age` file into `MEM_E2E_MIGRATION_PACKAGE_DROP_DIR` on the
browser-test host. Do not place the age private identity, decrypted ZIP, source
credentials, or operator credentials in the handoff directory.

The optional retention value must be `7`, `14`, `21`, or `30`. The optional
package wait must be an integer from `1` through `120` minutes.

### Evidence emitted on success

The terminal output contains a sanitized JSON summary with:

- Migration Session ID and display name;
- source Matrix hostname and encrypted package filename;
- conversion attempt and candidate IDs;
- private staging run ID;
- target stack slug and Element hostname;
- production verification ID and check count;
- baseline backup and Backup Catalog IDs;
- completion report ID and payload SHA-256;
- Session counts by status before and after the proof.

It does not print passwords, TOTP secrets, age private identities, decrypted
package data, database passwords, Matrix signing keys, TURN secrets, or route
advanced configuration.

After success, manually capture:

1. the completed Migration detail showing the final handoff state;
2. the `Open baseline backup` relationship;
3. the linked Backup Catalog detail and Restore readiness;
4. the updated `/migrations` list;
5. the exact focused-test and build output used for this baseline.

### Failure handling

A failure after conversion, staging, private-server creation, cutover, or
acceptance may have left durable resources. Do not rerun immediately. Record
the first failing operation and inspect the Migration Workspace, Activity,
Evidence, Advanced state, Runtime Stacks, public routes, and Backup Catalog.
Use the operation-specific recovery action that the product exposes; do not
manually delete Docker resources or database rows merely to make the test green.

After the proof, securely remove the transferred encrypted package from the
browser-test host once its required evidence and retention obligations are
understood:

```bash
rm -rf /tmp/mem-migration-e2e-package
rm -rf ./test-results ./playwright-report
```

## 5. Diagnostics release proof

The Diagnostics programme has two dedicated browser proofs.

### 5.1 Deterministic development proof

This proof requires a new disposable no-owner database. It enables a Development-only, explicitly configured fixture endpoint that is never mapped in Production. The browser:

```text
creates a disposable Platform Owner
  → proves the command-centre health strip and implemented capability cards
  → runs the harmless Diagnostics pipeline self-test
  → reviews the optional Seq and Portainer product boundaries
  → creates one safe deterministic diagnostic incident
  → proves the global attention bell and exact incident handoff
  → proves correlation and support-report copy/download
  → requests bounded Docker evidence
  → proves unavailable evidence leaves a partial usable report
  → opens technical events and logging health
  → switches between English and German
  → proves the browser never calls HostAgent directly
```

Start the disposable API with the fixture explicitly enabled:

```bash
cd "$MEM_REPO_ROOT"
rm -rf installer/data/diagnostics-e2e-browser

ASPNETCORE_ENVIRONMENT=Development \
Aio__SqlitePath="data/diagnostics-e2e-browser/mem.db" \
DataProtection__KeyRingPath="data/diagnostics-e2e-browser/keys" \
Diagnostics__TestFixture__Enabled=true \
Diagnostics__Logging__FilePath="data/diagnostics-e2e-browser/logs/mem-control-plane-.clef" \
Diagnostics__SafeEvents__RootPath="data/diagnostics-e2e-browser/events" \
Diagnostics__Seq__ManagementEnabled=true \
Diagnostics__Seq__EulaAccepted=true \
ASPNETCORE_URLS="http://127.0.0.1:7105" \
dotnet run --project ./installer/src/Api/Api.csproj
```

Run Vite in a second terminal, then:

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

MEM_E2E_BASE_URL="http://localhost:5173" \
MEM_E2E_SETUP_TOKEN="mem_test123" \
npm run test:e2e:diagnostics-dev
```

Destroy the disposable database, key ring, log files, event files, and browser output after the run:

```bash
rm -rf "$MEM_REPO_ROOT/installer/data/diagnostics-e2e-browser"
rm -rf "$MEM_REPO_ROOT/installer/src/Web/test-results"
rm -rf "$MEM_REPO_ROOT/installer/src/Web/playwright-report"
```

### 5.2 Deployed read-only proof

Use a dedicated named operator on a rebuilt non-production deployment:

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

MEM_E2E_BASE_URL="https://127.0.0.1:8443" \
MEM_E2E_OPERATOR_USERNAME="dedicated-e2e-operator" \
MEM_E2E_OPERATOR_PASSWORD="<dedicated-test-password>" \
MEM_E2E_OPERATOR_TOTP_SECRET="<base32-totp-secret>" \
npm run test:e2e:diagnostics-deployed
```

The deployed proof performs no mutation. It verifies authenticated no-store Diagnostics command-centre delivery, the incident workspace and Logging Health, English/German switching, absence of direct browser HostAgent calls, and a 404 response from the deterministic fixture route in Production.

The Development fixture endpoint must return 404 on a Production deployment. Never enable it on a shared development host, and never expose a Development API publicly.

### 5.3 Manual restart-persistence and outage proof

After the deterministic development proof has created its incident, keep the disposable data directories and browser tab temporarily:

1. Open the incident in `/diagnostics/logs` and note only its non-secret incident ID.
2. Stop the API process without deleting the disposable state.
3. In the already-loaded browser page, refresh Diagnostics data and confirm the external fallback card names Portainer, `docker logs --tail 500 mem-api`, and the CLEF recorder.
4. Confirm the same recent API shutdown/failure evidence is available from the host-side CLEF file or container console.
5. Restart the API with the same SQLite path, Data Protection key ring, CLEF path, and safe-event root.
6. Reopen `/diagnostics/logs` and confirm the previously recorded incident remains queryable.
7. Confirm Seq is still disabled and did not affect the proof.
8. Remove all disposable state and browser output.

Do not stop Postgres, NPM, or a production control plane for this proof.


### 5.4 Fresh-server Seq and Portainer proof

This proof is intentionally deferred until a disposable clean server is available. Do not attempt it against an established operator host merely to obtain a green release checklist.

On the fresh server, record and verify:

1. Portainer is installed from the approved 2.39.5 reference and the created container uses the resolved local immutable `sha256:` identity.
2. `portainer_data` survives container replacement; private HTTPS port 9443 is present; Edge port 8000 and public NPM ingress are absent.
3. An existing unowned Portainer container is not adopted, relabelled, restarted, replaced, or upgraded.
4. Generic Portainer links use the configured private authority and environment ID.
5. Exact incident and Seq container handoffs work for the approved route contract; destroyed or recreated resources fall back to the containers list.
6. Seq setup review exposes presence and readiness only, never secret values or host paths.
7. Seq deploy/start/restart verifies health; stop is intentional; delivery state remains distinct from runtime state; remove retains data.
8. Disabling Seq or making Portainer unavailable does not disable MEM-native incidents, safe events, CLEF recording, support reports, or pipeline verification.
9. No production test-fixture route, deterministic fault route, moving Portainer tag, or operational image pull exists.

Retain screenshots, focused command output, container inspection output, and rollback notes in the release evidence bundle. Do not retain secrets, recovery codes, Seq keys, Portainer credentials, or unrestricted raw logs.

## 7. Coturn resilience release acceptance — human-assisted host/Docker lifecycle proof

`PLATFORM-SERVICES-01F` is the final destructive/non-destructive lifecycle proof for the
shared platform Coturn service. The browser verifier deliberately **does not** execute
Docker or host mutations. The operator performs each approved local-development fault
in a separate terminal, then runs the verifier against the resulting authoritative MEM
state.

This separation is intentional:

```text
operator owns disruptive host/Docker action
        ↓
MEM startup supervision owns recovery/fail-closed decision
        ↓
Playwright verifies server-owned truth and safe browser projection
```

The verifier refuses anything except `containerized-development` on the HTTPS `8443`
origin. Do not use this acceptance suite on a production host.

### Common verifier environment

```bash
cd "$MEM_REPO_ROOT/installer/src/Web"

export MEM_E2E_BASE_URL="https://127.0.0.1:8443"
export MEM_E2E_OPERATOR_USERNAME="<dedicated-test-operator>"
export MEM_E2E_OPERATOR_PASSWORD="<dedicated-test-password>"
export MEM_E2E_OPERATOR_TOTP_SECRET="<base32-totp-secret>"
export MEM_E2E_COTURN_RESILIENCE_ACK="I_UNDERSTAND_COTURN_RESILIENCE_PROOF_MUTATES_LOCAL_DOCKER"
```

The verifier prints only bounded operational evidence such as scenario, runtime identity,
Coturn container identity, startup-supervision status/decision, restart policy, current
check status and allocation status. It does not print the Coturn secret, protected config,
operator credentials, recovery codes, screenshots, traces, or videos.

### Capture the pre-mutation baseline

Before an identity-preserving scenario, record:

```bash
cd "$MEM_REPO_ROOT"

docker inspect mem-coturn \
  --format 'ID={{.Id}} StartedAt={{.State.StartedAt}} Restart={{.HostConfig.RestartPolicy.Name}} Status={{.State.Status}}'

curl --insecure https://127.0.0.1:8443/health/runtime
```

For scenarios that compare identity, export the exact pre-mutation values:

```bash
export MEM_E2E_COTURN_EXPECTED_CONTAINER_ID="<full-container-id>"
export MEM_E2E_COTURN_EXPECTED_STARTED_AT="<StartedAt>"
export MEM_E2E_COTURN_EXPECTED_CONTROL_PLANE_INSTANCE_ID="<controlPlaneInstanceId>"
export MEM_E2E_COTURN_PREVIOUS_API_PROCESS_INSTANCE_ID="<apiProcessInstanceId>"
```

### Baseline / normal exact startup

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="baseline"
npm run test:e2e:coturn-resilience-live
```

Expected: startup supervision `verified`, no mutation, exact Runtime ready, and a fresh
usable functional result. The normal automatic external-IP posture may remain Warning
provided the local allocation probe passes; warning evidence must not carry a failure
Incident.

### Normal host reboot

Record the baseline identity, then reboot the disposable development host:

```bash
sudo reboot
```

After the host returns, confirm Docker and the Control Plane are running. Do not manually
repair Coturn. Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="host-reboot"
npm run test:e2e:coturn-resilience-live
```

Expected: same persistent Control Plane identity when provided, new API process identity,
same Coturn container identity when provided, exact Runtime ready, and fresh functional
evidence. `verified` or a bounded safe `recovered` result is acceptable according to the
actual restart ordering observed after boot.

### Docker daemon restart

Record baseline identities, then:

```bash
sudo systemctl restart docker
```

Wait for the Control Plane to return, then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="docker-daemon-restart"
npm run test:e2e:coturn-resilience-live
```

Expected: same Coturn identity, exact runtime, and fresh functional verification. No
uncontrolled recreation is acceptable.

### Exact Coturn stopped before startup

From a healthy baseline:

```bash
docker stop mem-coturn
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="stopped-before-startup"
npm run test:e2e:coturn-resilience-live
```

Expected: decision `start-stopped`, status `recovered`, same Coturn container ID, newer
`StartedAt`, no automatic Restart & Verify, and fresh allocation evidence.

### Restart-policy-only drift

From a healthy baseline:

```bash
docker update --restart=no mem-coturn
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="restart-policy-drift"
npm run test:e2e:coturn-resilience-live
```

Expected: decision `correct-restart-policy`, same container ID, unchanged `StartedAt`,
`Restart=unless-stopped`, exact runtime and fresh functional evidence.

### Persistent restart-loop evidence

This verifier supports a real restart-loop state but deliberately does not invent a
command that corrupts the protected Coturn configuration. Prepare a disposable,
reviewed restart-loop fixture only when its restoration path is known, restart the
Control Plane, then run:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="restart-loop"
npm run test:e2e:coturn-resilience-live
```

Expected: startup supervision `failed` / `unavailable`, no automatic mutation, and Docker
restart/error evidence. Do not reinterpret a crash loop as a clean stopped service.

### Non-zero exit without OOM injection

A safe development approximation of the OOM/non-zero-exit boundary is to disable Docker's
automatic restart and kill the test Coturn process:

```bash
docker update --restart=no mem-coturn
docker kill mem-coturn
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="nonzero-exit"
npm run test:e2e:coturn-resilience-live
```

Expected: failed/unavailable, no automatic start/restart, and non-zero exit evidence.
Restore deliberately afterwards:

```bash
docker update --restart=unless-stopped mem-coturn
docker start mem-coturn
./dev/mem-env container restart
```

### Wrong MEM gateway attachment

Disconnect only the test Coturn container from the expected gateway:

```bash
docker network disconnect mem-gateway mem-coturn
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="wrong-network"
npm run test:e2e:coturn-resilience-live
```

Expected: `repair-required`, no automatic mutation, and gateway/network-alias drift.
Restore the expected attachment and aliases:

```bash
docker network connect --alias coturn --alias mem-coturn mem-gateway mem-coturn
./dev/mem-env container restart
```

### Wrong protected configuration/mount contract

The verifier supports:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="wrong-mount"
npm run test:e2e:coturn-resilience-live
```

but this slice intentionally does not provide a one-line mutation command that recreates
Coturn with a deliberately wrong bind mount. Recreating a secret-bearing service by hand
is more dangerous than the state being tested. Use the existing focused Docker/runtime
policy coverage for the mount-specific mutation unless a disposable, reviewed clone
procedure is prepared. A live wrong-mount fixture must end in `repair-required` with no
automatic destructive repair.

### Missing owned container without destroying it

Preserve the exact owned container by renaming it instead of deleting it:

```bash
docker stop mem-coturn
docker rename mem-coturn mem-coturn-01f-held
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="missing-container"
npm run test:e2e:coturn-resilience-live
```

Expected: `repair-required`, container absent under the canonical name, protected setup
still present, and no automatic recreation/image pull.

Restore:

```bash
docker rename mem-coturn-01f-held mem-coturn
docker start mem-coturn
./dev/mem-env container restart
```

### Foreign-container collision

First hold the real owned Coturn container as above. Create a **stopped** foreign collision
using an already-local image; do not pull an image for this proof. For example, if the
freshly built development Control Plane image is present locally:

```bash
docker create --name mem-coturn mem-control-plane:local
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="foreign-collision"
npm run test:e2e:coturn-resilience-live
```

Expected: `conflict`, ownership false, no automatic mutation or takeover.

Restore immediately:

```bash
docker rm mem-coturn
docker rename mem-coturn-01f-held mem-coturn
docker start mem-coturn
./dev/mem-env container restart
```

### Functional allocation failure

The MEM local split-DNS tool is a useful development-only fault injector because it can
break the TURN hostname path without changing Coturn's protected container/configuration.
Use only a deliberately wrong, non-loopback test target with a known restoration value;
then restart the Control Plane and run:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="functional-allocation-failure"
npm run test:e2e:coturn-resilience-live
```

Expected: functional failure, exactly one automatic Restart & Verify attempt, terminal
startup failure if allocation remains broken, and retained Incident evidence.

Restore the correct split-DNS target before continuing.

### Automatic-recovery cooldown

Immediately after a failed startup pass that actually entered automatic mutation, restart
only the Control Plane again while the 30-minute recovery window is active:

```bash
./dev/mem-env container restart
```

Then:

```bash
export MEM_E2E_COTURN_RESILIENCE_SCENARIO="cooldown"
npm run test:e2e:coturn-resilience-live
```

Expected: `cooldown`, a future cooldown timestamp, and no second automatic host mutation.
Correcting the underlying fault is allowed to produce current healthy/warning evidence on
a later startup; cooldown must not hide stronger current `repair-required` or `conflict`
truth.

### Automatic external-IP mode

The current 0.2.0 source does **not** perform an external public-IP discovery transaction
that can be independently fault-injected. An unset explicit external IP is represented as
a bounded `external-ip` Warning while the local allocation check can still pass. Do not
invent an "external-IP detection failure" test that the current implementation does not
have. The baseline/recovery scenarios above verify the actual source contract instead.

### Acceptance completion

`PLATFORM-SERVICES-01F` closes only after the relevant real host/Docker scenarios are
recorded with exact pass/fail evidence and every deliberately mutated Docker state is
restored. Do not leave a held Coturn container, foreign collision, wrong network, restart
policy drift, failed local DNS target, or non-zero-exit fixture behind after testing.
