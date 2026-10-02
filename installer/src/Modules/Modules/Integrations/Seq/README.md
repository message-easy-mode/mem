# Optional Seq integration

Seq is an optional advanced structured-log backend for MEM. The local CLEF
black-box recorder, safe diagnostic event store, incident APIs, support reports,
and Diagnostics workspace remain authoritative and continue to work when Seq is
disabled or unavailable.

## Security and deployment posture

- Seq is disabled by default.
- MEM does not publish Seq through NPM.
- The operator-facing URL is optional and must be configured explicitly; MEM
  never invents a localhost or published-port URL.
- The MEM API runs on the Docker host. Managed health checks, administration,
  and delivery therefore use the server-owned loopback authority derived from
  the verified published Seq port; browser-supplied URLs are never used for
  service-to-service traffic.
- The Seq API key and first-run administrator password hash are read from an
  environment variable or protected server-owned secret file. They are never
  returned by Diagnostics.
- Ordinary lifecycle requests never pull an image.
- The explicit guided-setup boundary may prepare only the approved exact image
  and resolves it to Docker's immutable local image ID.
- Removing the managed container retains the configured `/data` host directory.

## Approved runtime

The current approved release is:

```text
datalust/seq:2026.1.17044
```

`AllowOperationalPull` must remain `false`. `AllowSetupPull` controls only the
reviewed first-time setup preparation path.

## Guided setup foundation

`SEQ-BOOTSTRAP-01A` provides the backend foundation for guided first-time setup:

- approved-image preparation restricted to the setup boundary;
- non-secret bootstrap state stored atomically;
- protected administrator-hash and ingestion-key file writers;
- password hashing through the approved immutable image using standard input;
- effective configuration that merges static policy with guided setup state;
- secret-free bootstrap overview and review endpoints.

`SEQ-BOOTSTRAP-01B` adds the guided execution boundary:

- explicit EULA acceptance;
- one-time administrator password and confirmation;
- a frozen, secret-free deployment review;
- recent operator step-up;
- approved-image preparation;
- password hashing through standard input;
- protected administrator-hash persistence;
- server-owned storage preparation;
- managed runtime deployment;
- Docker and Seq `/health` verification;
- durable, secret-free setup progress and safe retry evidence;
- authenticated HTTP ingestion from first run.

When a configured administrator password hash already exists, guided setup
reuses it and does not request or overwrite another plaintext password.
Initialized Seq data without its existing administrator secret fails closed;
first-run authority is never reset by a retry.

`SEQ-CONNECT-01A` provisions one dedicated ingest-only **MEM Control Plane**
credential, stores its token only in the protected server-owned secret file,
sends one authenticated verification event, and reads that exact event back
through the bounded administrator session. Ongoing MEM event delivery remains
disabled after this connection proof.

Existing manually configured Seq installations remain supported. Operators may
continue to provide secrets through:

```text
MEM_SEQ_ADMIN_PASSWORD_HASH
Diagnostics:Seq:AdminPasswordHashFilePath
MEM_SEQ_API_KEY
Diagnostics:Seq:ApiKeyFilePath
```

## Server-owned files

Default production locations are:

```text
/data/diagnostics/seq-bootstrap.json
/data/diagnostics/seq-delivery.json
/data/secrets/seq/admin-password-hash
/data/secrets/seq/ingestion-api-key
/data/seq
```

The bootstrap-state file contains no password hash, API-key token, cookie, or
authorization header. Secret files are written atomically beneath the configured
secret root with owner-only permissions on supported Unix hosts.

## Private access

A missing explicit `UiUrl` does not prevent a healthy private Seq runtime from
being prepared or deployed. After a verified guided deployment, MEM asks the
central managed-service authority resolver for a browser authority. Local and
containerized development use the actual published Seq port on loopback.
Containerized production may use a bootstrap-supplied private/overlay host IPv4
plus the actual published port. Public or ambiguous host addresses fail closed.

An operator-approved UI authority remains the highest-precedence override. It
must be an absolute HTTP or HTTPS URL without embedded credentials, query, or
fragment, and should normally be reachable only through a private network, VPN,
or SSH tunnel.

## Delivery behavior

The delivery preference remains staged separately from the running process:

```text
Current delivery state
Desired delivery state after restart
Restart required
Active-process verification
```

`SEQ-CONNECT-01B` enables delivery only after the managed runtime is healthy and
the dedicated connection proof from `SEQ-CONNECT-01A` is available.

At API startup, MEM reads the desired preference before Serilog is created. A
desired enabled state is accepted only when the protected bootstrap state proves:

- MEM owns and previously verified the managed runtime;
- the selected published host port is valid;
- the dedicated ingestion credential exists;
- the initial authenticated connection event was read back.

The running API then configures both Seq health and ingestion through:

```text
http://127.0.0.1:<server-owned-published-port>/
```

A missing or invalid prerequisite fails safely: the Seq sink remains disabled,
MEM-native Diagnostics remain active, and the browser reports the bounded
startup warning rather than claiming delivery is active. The current process
also retains the exact secret-free result of Serilog sink attachment; valid
configuration alone is never treated as proof that the sink was installed.

After a successful restart, MEM separately reports:

- desired delivery enabled;
- the logging bootstrap actually attached the Seq sink in the current process;
- effective sink enabled in the current process;
- API-key secret resolved;
- managed Seq runtime running on the approved image;
- Seq health reachable.

A Platform Owner can then emit a uniquely identified event through the normal
`ILogger`/Serilog pipeline. The returned query can be searched in Seq to confirm
receipt. MEM binds this activation proof to a random per-process identity, so a
later API restart requires a fresh proof and cannot reuse stale success.

Disabling delivery is also staged. The current process may continue sending
events until the API restarts. Runtime removal remains blocked until both the
desired and effective delivery states are disabled.

## Rollback

The MEM-native Diagnostics system remains fully functional when Seq is disabled
or unavailable.

A failed stage records the exact safe operation step and retains the Seq data
directory. A health-verification failure may leave a proven MEM-managed runtime
available for inspection and retry, but it must not be reported as healthy or
connected. Removing a managed runtime preserves the Seq data directory.
Permanent deletion of Seq data remains a separate high-impact workflow.
