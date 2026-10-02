# MEM Control Plane identity matrix

**Programme:** `MEM-CONTROL-PLANE-RUNTIME-01`  
**Slices:** `CP-NAME-01A`, `CP-NAME-01B`, `RUNTIME-CONTEXT-01A`, `RUNTIME-CONTEXT-01B`, `DEV-ENV-01A`
**Decision date:** 6 August 2026  
**Status:** Canonical fresh-install runtime applied; reviewed legacy migration and compatibility aliases retained

## Canonical product and permanent-runtime identity

| Concern | Canonical value | Decision |
|---|---|---|
| Product name | `Message Easy Mode` | `Matrix Easy Mode` is no longer the current product name. |
| Product abbreviation | `MEM` | Stable short product identity. |
| Runtime display name | `MEM Control Plane` | Permanent API, embedded Web application, container and current operator documentation use Control Plane terminology. |
| Publisher/config namespace | `message-easy-mode` | All new runtime-facing configuration, image references and ownership labels use this namespace. |
| Container | `mem-control-plane` | Used by fresh installations. |
| Development container | `mem-control-plane-dev` | Reserved for the supported production-shaped local developer environment; rejected in production runtime mode. |
| Development volume | `mem-control-plane-dev-data` | Dedicated persistent state for containerized development only. |
| Registry publisher | `ghcr.io/message-easy-mode` | Canonical current publisher namespace. |
| Image repository | `ghcr.io/message-easy-mode/mem-control-plane` | Canonical Control Plane repository. Release tags remain `stable` and `dev`. |
| Local image | `mem-control-plane:local` | Reserved for the supported developer build contract. |
| New-install volume | `mem-control-plane-data` | Used by fresh installations. Existing installations retain their current volume. |
| Certificate | `mem-control-plane.crt` | Used by fresh installations. |
| Private key | `mem-control-plane.key` | Used by fresh installations. |
| Certificate subject | `CN=MEM Control Plane` | Used by fresh installations. |

## Canonical resource-label namespace

Later ownership/runtime slices apply these frozen labels:

```text
io.message-easy-mode.managed
io.message-easy-mode.control-plane-instance
io.message-easy-mode.runtime-mode
io.message-easy-mode.resource
```

The former `io.matrix-easy-mode.*` namespace must not be introduced for new resources.

## Canonical environment names

| Purpose | Canonical name | Compatibility behaviour |
|---|---|---|
| Setup token | `MEM_CONTROL_PLANE_SETUP_TOKEN` | Bootstrap writes the canonical name; the API retains a bounded read of `MEM_INSTALLER_SETUP_TOKEN`. |
| Setup-token path | `MEM_CONTROL_PLANE_SETUP_TOKEN_PATH` | The API reads the legacy alias only as a bounded compatibility fallback. |
| Release channel | `MEM_CONTROL_PLANE_CHANNEL` | Bootstrap writes the canonical name only; the legacy alias remains read-only compatibility. |
| Published private port | `MEM_CONTROL_PLANE_PUBLIC_PORT` | Bootstrap writes the canonical name. |
| Host IPv4 candidate | `MEM_CONTROL_PLANE_HOST_IPV4` | Bootstrap discovers the host-side IPv4 before entering the container; used only for validated private browser authorities. |
| Persistent instance ID | `MEM_CONTROL_PLANE_INSTANCE_ID` | Introduced by `RUNTIME-CONTEXT-01A`; not accepted from the browser. |
| Runtime mode | `MEM_RUNTIME_MODE` | Introduced and validated by `RUNTIME-CONTEXT-01A`. |
| State root | `MEM_STATE_ROOT` | Introduced as a server-owned runtime-context input, not a feature-specific guess. |
| Product version | `MEM_PRODUCT_VERSION` | Optional build/runtime override for the running MEM version projected by Diagnostics and structured logs. |
| Commit identity | `MEM_COMMIT_SHA` | Optional build/runtime commit identity; omitted honestly when the build does not provide it. |

## Namespace rule

All new runtime-facing configuration, image references, Docker labels and current documentation use `message-easy-mode`. The former `matrix-easy-mode` name may remain only in an explicitly classified compatibility alias, retained historical evidence, or an existing local/source path that has not yet been migrated.

## Explicit legacy aliases

| Legacy identity | Classification | Required treatment |
|---|---|---|
| `matrix-easy-mode` | Former publisher/project namespace | Never use for new configuration. Retain only where a current legacy image or cryptographic identity requires exact compatibility. |
| `mem-installer` | Legacy permanent-runtime container alias | Detect, never start a competing second Control Plane, migrate only through reviewed `CP-NAME-01B` flow. |
| `ghcr.io/matrix-easy-mode/mem-installer` | Legacy image repository | Accepted only for existing/rollback definitions; new installs use `ghcr.io/message-easy-mode/mem-control-plane`. |
| `mem-installer-data` | Legacy persistent volume | Reuse in place for upgraded installations. Do not copy or rename it for cosmetic consistency. |
| `mem-installer.crt` / `mem-installer.key` | Legacy certificate names | Preserve when upgrading an existing runtime. |
| `MEM_INSTALLER_SETUP_TOKEN` | Setup-authority compatibility alias | Bounded fallback read only; never reintroduce it as normal operator authority. |
| `MEM_INSTALLER_SETUP_TOKEN_PATH` | Setup-authority compatibility alias | Bounded fallback read only. |
| `MEM_INSTALLER_CHANNEL` | Bootstrap compatibility alias | Bounded compatibility only. |
| `MEM_INSTALLER_PUBLIC_PORT` | Bootstrap compatibility alias | Bounded compatibility only. |

## Frozen authority and persistence identities

The runtime rename must not mechanically change these values:

| Value | Reason |
|---|---|
| Data Protection application name `matrix-easy-mode.control-plane` | Legacy cryptographic compatibility identity. A direct rename can invalidate protected cookies, token-provider material and recovery flows; migrate only through a separately tested compatibility plan. |
| Cookie `mem_installer_auth` | Transitional first-run authority; changing it requires an explicit authentication migration. |
| Claim `mem_installer_unlocked` | Transitional compatibility claim used by the existing unlock boundary. |
| Subject `mem-installer-operator` | Existing transitional principal identifier. |
| SQLite schema and migration identifiers | Runtime naming is not a database migration. |

## Source occurrence classification

### Applied by `CP-NAME-01B`

- bootstrap now creates `mem-control-plane` with the canonical image, volume, certificate and environment names;
- existing `mem-installer` containers enter a reviewed, health-verified, rollback-capable migration that retains `mem-installer-data`;
- current installation and troubleshooting documentation is aligned in English and German;
- host checks recognise canonical and legacy Control Plane identities;
- current API/OpenAPI and first-run Web copy use Control Plane terminology.

### Applied by `RUNTIME-CONTEXT-01B`

- Seq and NPM server-side routing use the central purpose-specific managed-service authority resolver;
- restart guidance is projected by the active server runtime instead of being invented by Web features;
- a competing Control Plane blocks Docker-backed mutations by default;
- newly created managed resources carry canonical Message Easy Mode ownership, Control Plane instance, and runtime-mode labels while retaining legacy labels where compatibility requires them;
- guardrail tests prevent feature code from reintroducing direct localhost, Docker service-name, or restart-command assumptions.


### Applied by `DEV-ENV-01A`

- `mem-control-plane-dev` is accepted only with the explicit `containerized-development` runtime mode;
- `installer/Dockerfile` builds the React application and API into `mem-control-plane:local`;
- `dev/mem-env` starts, stops, switches and resets local or containerized development through bounded state and process ownership;
- `dev/compose.control-plane.yml` uses the dedicated `mem-control-plane-dev-data` volume and loopback HTTPS port;
- production bootstrap refuses an existing development Control Plane rather than starting a competing permanent runtime;
- `/health/runtime` provides the bounded server-owned preflight used by the harness without exposing paths, Docker authorities or credentials.

### Retain as real setup/installer concepts

- bootstrap script and first-time setup workflow;
- `InstallerAuth*`, setup-token and installation-plan types where they describe setup authority or installation behaviour;
- `/api/installer-auth/*` compatibility routes until their authority lifecycle is deliberately retired;
- installation records, verification, handoff and maintenance concepts.

### Retain as historical evidence

- security authority inventories;
- retired CLI installer-token documentation;
- migration and release history that names the runtime that actually existed at the time;
- captured source archive names and checksums.

### Defer to source-hygiene work

- current repository checkout path containing `MatrixEasyMode`;
- `MemInstaller.sln`;
- launch-profile names;
- namespaces, project names and classes containing `Installer` when they are internal or still semantically correct;
- development database filename `mem-installer.dev.db` unless a later compatibility-safe state migration is justified.

## Guardrail policy

`MemControlPlaneIdentityGuardrailTests` freezes the currently reviewed production-source files that still contain runtime-facing legacy names or the former project namespace. New occurrences outside those allowlists fail the focused gate. Later slices must reduce or reclassify the allowlists rather than weaken the tests globally.
