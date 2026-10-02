# SEC-AUTH-00 — Authority Inventory and Contract Freeze

**Status:** Baseline inventory; no production authority behaviour changes in this slice.  
**Baseline reviewed:** 3 July 2026 applied source snapshots.  
**Purpose:** Freeze the current authority map before `SEC-AUTH-01A` removes browser-held Host Agent authority and `SEC-AUTH-02/03` introduces ASP.NET Core Identity and scoped first-owner bootstrap.

## 1. Baseline sources

| Source archive | SHA-256 |
|---|---|
| `mem-web-source-20260703-191821.zip` | `1f47cad8219b9e871b86221d14c86bdc1273a69135e61e6ab50ef10a8017c6a7` |
| `mem-installer-source-20260703-191941.zip` | `e6ff82e4c89f3ddcf14c21b1bb7e6eed21e85f09e84f899d2219361df1f0fed3` |
| `mem-cli-source-20260703-192007.zip` | `1c89a76181989efb7b93fd2225b41fb972178fa6f5f6ee09abb4463944dfd58b` |
| `bootstrap(2).zip` | `cef65dce0d4809306a545ba460e3ea0d50fde4311daa115fa92322bebe49a2fd` |

This document describes the snapshot above. Do not assume that an unlisted later local edit is included in the authority decision below.

## 2. Current trust model — observed, not approved

The current runtime authority chain is:

```text
Browser local storage / Vite environment / fallback string
        ↓
X-MEM-Agent-Secret browser request header
        ↓
MEM API process (same process hosts Host Agent code)
        ↓
Docker socket mounted into browser-facing API container
        ↓
Docker and host-level operational authority
```

A separate generic installer cookie can be obtained by posting the raw installer setup token to `/api/installer-auth/unlock`.

```text
Raw mem_ setup token
        ↓
POST /api/installer-auth/unlock
        ↓
12-hour sliding cookie session: mem_installer_auth
        ↓
Fallback authenticated-user policy for most API routes
```

These current paths are **legacy transition mechanisms**. They are not the SEC-AUTH target design.

## 3. Target contract after SEC-AUTH

```text
Approved private path (Tailscale / trusted management LAN / approved VPN)
        ↓
Named MEM operator
        ↓
Password + TOTP MFA
        ↓
Server-side role/capability policy
        ↓
Session-bound recent-authentication step-up for high-risk work
        ↓
Control-plane workflow/API
        ↓
Narrow local Host Agent boundary
```

The browser must never carry a credential that authorizes Host Agent or Docker operations.

## 4. Authority inventory

### 4.1 Browser-held shared-secret sources

Three browser transports use identical precedence today:

```text
window.localStorage["mem.agentSecret"]
  → import.meta.env.VITE_MEM_AGENT_SECRET
  → "dev-only-change-me"
```

| Browser caller | Source path | Calls / scope | Current authority |
|---|---|---|---|
| Backup and restore transport | `Web/src/features/operator/backups/api/transport/host-agent.ts` | Backup catalog, import, restore workspace, private test, standard recreate, cutover, downloads | Adds `X-MEM-Agent-Secret` to GET, POST, DELETE, upload, and download calls |
| Runtime stacks transport | `Web/src/features/operator/stacks/api/stacks.api.ts` | Stack list/inspect/doctor, users, storage, backup, create command, destroy | Adds `X-MEM-Agent-Secret` to GET and POST calls |
| Coturn transport | `Web/src/features/operator/services/api/coturn.api.ts` | Coturn inspect and ensure | Adds `X-MEM-Agent-Secret` to GET and POST calls |

All three also set `credentials: "include"`, but the shared header is still browser-supplied infrastructure authority.

#### Browser testing and E2E injection

| Location | Current role | SEC-AUTH-01A decision |
|---|---|---|
| `Web/tests/e2e/backup-catalog.dev.spec.ts` | Writes `mem.agentSecret` using Playwright `addInitScript` | Remove |
| `Web/tests/e2e/backup-catalog.deployed.spec.ts` | Writes `mem.agentSecret` using Playwright `addInitScript` | Remove |
| `Web/scripts/assert-e2e-env.mjs` | Requires `MEM_E2E_AGENT_SECRET` | Remove / replace with named-session fixture contract |
| `Web/scripts/assert-e2e-live-env.mjs` | Requires `MEM_E2E_AGENT_SECRET` | Remove / replace with named-session fixture contract |
| `Web/tests/e2e/README.md` | Documents `MEM_E2E_AGENT_SECRET` | Remove / update |
| `Web/src/features/operator/backups/api/transport/host-agent.test.ts` | Characterizes current local-storage header use | Replace with no-secret transport tests in SEC-AUTH-01A |

### 4.2 Current installer setup token and generic cookie session

| Stage | Source | Current behaviour | Security transition decision |
|---|---|---|---|
| Token generation | `bootstrap/bootstrap/lib/installer.sh` → `generate_setup_token` | Generates `mem_` token using `openssl rand -hex 24`; falls back to timestamp/`$RANDOM` if OpenSSL is absent | Replace fallback with fail-closed secure generation; final bootstrap code is one-time/scoped |
| Raw token persistence | `bootstrap/bootstrap/lib/installer.sh` → `persist_setup_token` | Writes raw token to Docker volume path `/data/setup-token`, `0600` | Persist only a verifier/hash or protected grant state after final bootstrap design is implemented |
| Token re-display | `bootstrap/install.sh` + `bootstrap/bootstrap/lib/installer.sh` → `--show-setup-token` | Root command reads and prints historic raw token | Retire for completed systems; replace with fresh host-armed bootstrap/recovery grants |
| Container environment | `bootstrap/bootstrap/lib/installer.sh` → `run_installer_docker_container` | Sends raw `MEM_INSTALLER_SETUP_TOKEN` to container environment | Avoid raw long-lived token in environment; use protected short-lifecycle material |
| API token load | `Modules/Modules/Auth/Services/InstallerSetupTokenStore.cs` | Reads token from `MEM_INSTALLER_SETUP_TOKEN`, configured token path, or development token | Replace with scoped bootstrap grant source in SEC-AUTH-03 |
| Token validation | `Modules/Modules/Auth/Services/InstallerTokenValidator.cs` | Constant-time comparison against raw expected token | Replace contract after scoped bootstrap grant exists |
| Browser unlock | `Modules/Modules/Auth/Endpoints/InstallerAuthEndpoints.cs` → `POST /api/installer-auth/unlock` | Valid raw token creates generic `MEM Installer Operator` cookie session | Bootstrap code must not create normal dashboard authority |
| Cookie configuration | `Modules/Modules/Auth/InstallerAuthServiceCollectionExtensions.cs` | Cookie `mem_installer_auth`, `HttpOnly`, `SameSite=Strict`, `SameAsRequest`, 12-hour sliding expiry | Replace/augment with Identity-backed named session, security-stamp validation, session revocation, and explicit cookie policy |
| Browser UX | `Web/src/features/auth/*` | Unlock page and route guard trust generic installer cookie session | Replace with bootstrap state, named sign-in, MFA, recovery, and current-session UX |
| CLI unlock | `cli/Mem.Cli/Clients/HostAgentClient.cs` | Optional installer token triggers CLI unlock request before calls | Retire as normal authority; decide local-only or named-device CLI design after SEC-AUTH-07 |

### 4.3 Host Agent and control-plane header guards

The code currently uses **two divergent shared-secret implementations**.

| Guard style | Configuration source | Endpoints currently using it | Notes |
|---|---|---|---|
| `HostAgentSharedSecretValidator` | `HostAgent:InternalCommandSecret` bound through `HostAgentOptions` | Command creation, storage endpoint, Coturn endpoints, and much of the backup/restore route family through `HostAgentEndpointSecretGuard` | Uses a fixed-time comparison; logs whether a secret is configured and its length |
| Inline `Authorize` / direct comparison | `HostAgent:Secret`, defaulting to `dev-only-change-me` | Status, runtime-stack list/inspect/operations/doctor/destroy, runtime users, and legacy `ControlPlaneIdentity` endpoints | Duplicates guard behaviour and retains an insecure fallback |

`HostAgentEndpointSecretGuard.SharedSecretHeaderName` defines the shared header name: `X-MEM-Agent-Secret`.

#### Explicit endpoint families mapped in this baseline

| Endpoint family | Current route examples | Current browser/client caller | Current authority | SEC-AUTH target |
|---|---|---|---|---|
| Host status | `GET /internal/host-agent/status` | CLI / diagnostics | Inline `HostAgent:Secret` or fallback | Named policy; eventual local Host Agent boundary |
| Runtime stacks | `GET /internal/host-agent/runtime-stacks`, `GET /internal/host-agent/runtime-stacks/{slugOrId}`, `GET .../operations`, `POST .../doctor`, `POST .../destroy` | Browser stacks transport; CLI | Inline `HostAgent:Secret` or fallback | Named role/capability policies; step-up for destructive operation |
| Runtime users | `GET /internal/host-agent/runtime-stacks/{slugOrId}/users`, `POST .../users`, `POST .../users/first-admin` | Browser stacks transport | Inline `HostAgent:Secret` or fallback | Named policy; later sensitive-user-operation review |
| Stack storage | `GET /internal/host-agent/runtime-stacks/{slugOrId}/storage` | Browser stacks transport | `HostAgentSharedSecretValidator` | Named policy |
| Create runtime command | `POST /internal/host-agent/commands/create-chat-stack-runtime` | Browser stacks transport | `HostAgentSharedSecretValidator` | Named policy |
| Coturn | `GET /internal/host-agent/platform/coturn`, `POST .../ensure` | Browser services transport | `HostAgentSharedSecretValidator` | Named policy; step-up policy decision required for destructive/recreate path |
| Backup and restore | `/internal/host-agent/backups/**` | Browser backup transport; CLI | Existing Host Agent shared-header guard | Named policy; explicit step-up for deletion, production restore, and cutover |
| Legacy local users | `/internal/control-plane/users/**` | No active browser UI found | Inline `HostAgent:Secret` or fallback | Retire/replace with ASP.NET Core Identity management APIs |

The complete backup/restore family is intentionally not copied route-by-route into this document because it is already extensive. Before `SEC-AUTH-01A` is applied, the implementer must run the grep audit in section 8 and confirm every `/internal/host-agent/backups/**` endpoint is covered by the server-side transition policy.

### 4.4 Swagger / OpenAPI authority path

`Api/Program.cs` adds a development-only OpenAPI API-key definition named `HostAgentSecret`:

```text
Header: X-MEM-Agent-Secret
Scheme type: apiKey
```

Swagger currently combines this header mechanism with the generic same-origin installer cookie. `SEC-AUTH-01A` must remove this API-key security definition and requirement. Swagger must not become an alternate authority channel.

### 4.5 CLI authority path

| Location | Current behaviour | SEC-AUTH decision |
|---|---|---|
| `cli/Mem.Cli/Config/CliOptions.cs` | Reads `--agent-secret`, then `MEM_AGENT_SECRET`, then `dev-only-change-me`; default Host Agent URL is `http://localhost:7105` | Remove fallback immediately when CLI work begins; do not retain shared secret long-term |
| `cli/Mem.Cli/Clients/HostAgentClient.cs` | Adds `X-MEM-Agent-Secret` to default headers for all calls; optional installer token unlocks generic cookie | Document as temporary non-browser contract; replace after identity and local Host Agent strategy are chosen |
| `cli/Mem.Cli.Tests/**` | Many transport/command tests intentionally pass `test-agent-secret` and assert the header | Update as a planned CLI authority migration, not as part of this installer-only ZIP |
| `Shared/Mem.Localization/Resources/CliMessages*.resx` | Documents `MEM_AGENT_SECRET` and fallback | Update with the CLI change |

### 4.6 Legacy control-plane identity implementation

| Component | Current role | SEC-AUTH-02/05 decision |
|---|---|---|
| `Infrastructure.Data.Entities.ControlPlane.ControlPlaneUserEntity` | Legacy local user profile persistence | Reuse data/profile migration concepts only |
| `ControlPlaneUserRoleEntity` | Legacy roles | Map intent only to ASP.NET Core Identity roles/policies |
| `HostAgent.Identity.ControlPlanePasswordHasher` | Custom password hashing | Retire; `UserManager` owns password hashing and validation |
| `HostAgent.Identity.ControlPlaneIdentityService` | Legacy local user management | Retire as normal control-plane identity authority |
| `HostAgent/Endpoints/ControlPlaneIdentityEndpoints.cs` | Legacy user management endpoints guarded by shared header | Replace with named Identity management APIs and Platform Owner policy |

The existing role naming (`power_admin`, `admin`, `viewer`) is not the approved future role model. SEC-AUTH must use `platform_owner`, `operator`, and `auditor`.

### 4.7 Deployment boundary observations

| Location | Current behaviour | Follow-on programme |
|---|---|---|
| `bootstrap/bootstrap/lib/installer.sh` | Publishes installer port using `-p ${INSTALLER_PORT}:8443` on normal host interfaces | Tailscale/private binding programme |
| Same bootstrapper | Prints host IP and public-server-IP access guidance | Tailscale/private access documentation programme |
| Same bootstrapper | Mounts `/var/run/docker.sock` into `mem-installer` | Host Agent isolation programme |
| `Api/Program.cs` and `HostAgentServiceCollectionExtensions.cs` | API process creates Docker client for `unix:///var/run/docker.sock` and registers `AddHostAgent` in-process | Host Agent process/socket separation programme |

SEC-AUTH does not claim to have resolved these boundaries. It must not make them less explicit.

## 5. Contract freeze rules

Until a later SEC-AUTH slice intentionally changes an item:

1. Do not add a new browser caller of `X-MEM-Agent-Secret`.
2. Do not add a new `VITE_MEM_AGENT_SECRET`, `mem.agentSecret`, or `MEM_E2E_AGENT_SECRET` use.
3. Do not add an API-key/swagger authorization escape hatch.
4. Do not add another inline `HostAgent:Secret` guard. New server work must use an explicit agreed policy/guard strategy.
5. Do not add a new generic `mem_`-based login/session path.
6. Do not add a new `dev-only-change-me` fallback.
7. Do not treat a successful browser cookie as proof that a future named operator is authorized.
8. Do not implement a direct database-edit recovery procedure.

## 6. Required next-slice changes

### SEC-AUTH-01A — browser secret transport removal

- Remove browser `getAgentSecret` helpers and header injection from all three transports.
- Remove `VITE_MEM_AGENT_SECRET`, `mem.agentSecret`, `MEM_E2E_AGENT_SECRET`, and `dev-only-change-me` browser usage.
- Replace current characterization tests with assertions that browser calls carry **no** Host Agent secret.
- Remove Swagger API-key definition/requirement.
- Make existing installer unlock cookie a narrow transition only; do not extend it.
- Keep residual non-browser CLI/internal header paths documented until their dedicated migration.

### SEC-AUTH-02/03 — Identity and first owner

- Use the existing `MemDbContext` migration strategy only after a focused migration decision record confirms the safe model.
- Add ASP.NET Core Identity + SQLite, roles, server-managed sessions, audit events, and scoped bootstrap grants.
- Do not seed a default owner account.
- First owner setup must require password, TOTP verification, one-time recovery codes, and bootstrap consumption.

### SEC-AUTH-04/05 — normal named operators

- Retire generic installer cookie as normal operator authority.
- Add login, MFA, current session, role policies, operator lifecycle, lockout, session revocation, and audit.
- Retire the legacy local user/password path from normal authority.

### SEC-AUTH-06/07 — step-up and host-armed recovery

- Bind recent authentication to the current server session.
- Require fresh password + TOTP for high-risk actions.
- Add `memctl auth arm-recovery` as a local host authority path only.
- Do not expose recovery arming over normal remote browser/API/CLI paths.

## 7. Tests added in this SEC-AUTH-00 slice

This slice deliberately adds **characterization tests**, not approval tests for the legacy architecture.

| Test | Purpose | Required later change |
|---|---|---|
| `Web/.../host-agent.test.ts` | Makes the current backup transport secret/header behavior explicit | Rewrite to assert no secret/header in SEC-AUTH-01A |
| `Web/.../stacks.api.test.ts` | Covers a second browser transport currently using local storage/header | Rewrite to assert no secret/header in SEC-AUTH-01A |
| `Web/.../coturn.api.test.ts` | Covers the third browser transport currently using local storage/header | Rewrite to assert no secret/header in SEC-AUTH-01A |
| `HostAgentEndpointSecretGuardContractTests` | Characterizes the canonical shared-header validator contract | Replace/retire when typed local Host Agent authority is implemented |
| `InstallerTokenValidatorContractTests` | Characterizes the current raw setup-token validator | Replace/retire when scoped bootstrap grants exist |

These tests prevent an accidental, unreviewed security transition. They must be deliberately updated—not silently left behind—when the approved replacement exists.

## 8. Mandatory authority audits

Run from the repository root after applying this slice and again before completing each subsequent SEC-AUTH workstream.

```bash
# Browser-held authority sources: all must disappear in SEC-AUTH-01A.
grep -RInE 'VITE_MEM_AGENT_SECRET|mem\.agentSecret|MEM_E2E_AGENT_SECRET|dev-only-change-me' \
  installer/src/Web \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=coverage

# Browser header transport: all browser matches must disappear in SEC-AUTH-01A.
grep -RInE 'X-MEM-Agent-Secret' \
  installer/src/Web \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=coverage

# Residual API/Host Agent/CLI shared-secret paths: inspect each result deliberately.
grep -RInE 'X-MEM-Agent-Secret|MEM_AGENT_SECRET|HostAgent:Secret|InternalCommandSecret|dev-only-change-me' \
  installer/src/Api installer/src/HostAgent installer/src/Api.IntegrationTests \
  cli/src/Mem.Cli cli/src/Mem.Cli.Tests \
  --exclude-dir=bin --exclude-dir=obj

# Bootstrap, generic unlock, and legacy identity paths: review before SEC-AUTH-02/03 and SEC-AUTH-04/05.
grep -RInE 'InstallerAuth|SetupToken|MEM_INSTALLER_SETUP_TOKEN|show-setup-token|ControlPlaneUser|ControlPlanePasswordHasher|ControlPlaneIdentity' \
  installer/src/Api installer/src/Modules installer/src/HostAgent installer/src/Infrastructure \
  installer/src/Web bootstrap cli \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules
```

A match is not automatically a failure during transition. It is acceptable only when it is intentionally listed in this inventory, has a named replacement workstream, and is not browser-held authority after SEC-AUTH-01A.

## 9. Open decisions to resolve before code is written

1. **Identity schema ownership:** extend `MemDbContext` or add a coordinated Identity context? The decision must avoid competing migration histories and preserve the current SQLite backup/restore story.
2. **Bootstrap grant handoff:** how does the pre-control-plane installer bootstrap code create a scoped first-owner grant without retaining a permanent raw token?
3. **Server-managed session storage:** what persistence and cleanup policy holds session revocation state, expiry, and current-session display safely?
4. **CSRF/anti-forgery:** select the exact cookie-authenticated mutation protection approach before normal named browser sessions are exposed.
5. **CLI long-term role:** local host tool over a future socket, or named operator/device client? Do not decide by preserving `--agent-secret`.
6. **Recovery-arm integration:** how can a root-local command arm recovery without direct database editing, a permanent raw token, or a new network listener?
7. **Development harness:** specify a disposable Identity/TOTP fixture mechanism that does not inject infrastructure secrets into a browser.

## 10. Completion statement for SEC-AUTH-00A

This workstream is complete when this inventory has been reviewed, the included characterization tests pass, the audit commands have been reviewed, and the next code slice is explicitly limited to `SEC-AUTH-01A` browser secret transport removal.
