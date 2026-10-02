# MM-01 Source Baseline — MEM v0.1.0

MM-01 was designed against the operator-supplied source archives available on
12 July 2026:

- `matrix-easy-mode-api.zip`
- `Archive(90).zip`
- `bootstrap(10).zip`
- `mem-installer-source-20260712-065013.zip`
- `mem-web-source-20260712-065008.zip`

The supplied v0.1.0 application source establishes these supported-source
facts:

- product name: `MatrixEasyMode`;
- product version: `0.1.0`;
- application PostgreSQL schema: `app`;
- EF migration: `20260507085953_InitialApplicationSchema`;
- legacy service keys: `matrix` and `element-web`;
- legacy runtime labels: `mem.instanceId` and `mem.serviceKey`;
- legacy Docker network: `mem-gateway`;
- Synapse image was provisioned from `matrixdotorg/synapse:latest`;
- Synapse data was mounted at `/data`;
- the generated Synapse configuration used the built-in SQLite
  `homeserver.db` unless an operator subsequently changed it;
- Element configuration was mounted at `/app/config.json`;
- the legacy public API exposed anonymous `GET /api/system/config` product
  information.

## Exact supported database tables

```text
__EFMigrationsHistory
guest_chat_sessions
identity_refresh_tokens
inbox_requests
platform_ingress_bootstrap_state
platform_settings
provisioning_jobs
service_instances
space_user_password_reset_requests
stack_users
stacks
user_account_roles
user_accounts
```

MM-01 treats any different migration history, missing table, or additional
application table as an unsupported schema until reviewed. This is deliberate:
the first migrator release supports one known source fingerprint rather than a
best-effort import.

## Data intentionally not read

The assessment queries do not select:

- `AdminAccessToken` values;
- password hashes;
- refresh-token hashes;
- guest-token hashes;
- password-reset-token hashes;
- JWT signing material.

Only the Boolean presence of Matrix administrator authority is recorded.
