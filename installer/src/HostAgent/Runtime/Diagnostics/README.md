# MEM Diagnostics Backend Operations

This directory contains the MEM-native safe diagnostic event store and bounded Docker evidence implementation used by the operator Diagnostics workspace.

## Storage layers

MEM diagnostics deliberately uses two independent local storage layers:

1. The control-plane CLEF recorder configured under `Diagnostics:Logging`.
2. The browser-safe NDJSON event store configured under `Diagnostics:SafeEvents`.

The API and HostAgent must continue operating when either optional local store is unavailable. Console logging remains the emergency fallback for the control-plane process. Seq is optional and is not required by the MEM-native Diagnostics APIs or UI.

## Independent release switches

The following switches are independent:

```json
{
  "Diagnostics": {
    "Logging": {
      "PersistentFileEnabled": true
    },
    "SafeEvents": {
      "Enabled": true
    },
    "DockerEvidence": {
      "Enabled": true
    },
    "Seq": {
      "SinkEnabled": false,
      "ManagementEnabled": false
    }
  }
}
```

Disabling the safe event store prevents new browser-safe events from being written but must not prevent API requests or workflow operations from completing. Disabling Docker evidence leaves incident and support-report data available without a container tail. Disabling Seq has no effect on the core Diagnostics experience.

## Disk health

The local CLEF recorder and safe event store expose bounded storage-capacity facts through Logging Health. They report `ready`, `low`, `critical`, or `unknown` without returning a filesystem path to non-owner roles.

Default warning thresholds are:

- low: 1 GiB available;
- critical: 256 MiB available.

These thresholds are warnings only. They do not delete data outside the configured retention policies and do not stop the API.

## Support-report bounds

Support-report requests and generated JSON are bounded independently. The backend:

- rejects oversized request bodies before JSON deserialization;
- limits Docker log characters included in reports;
- reduces technical events when the final serialized report would exceed its configured byte limit;
- records explicit truncation warnings and omissions;
- never includes raw CLEF files, runtime-operation JSON, credentials, private keys, connection strings, or arbitrary host paths.

## Total API outage fallback

When the API process is unavailable, the Diagnostics browser cannot load. Use one of these fallback paths:

1. Open the `mem-api` container logs in Portainer.
2. Run `docker logs --tail 500 mem-api` on the MEM host.
3. Inspect the persistent CLEF directory configured by `Diagnostics:Logging:FilePath`.

Treat downloaded or copied technical logs as sensitive operational data. They can contain application-emitted identifiers or addresses even after MEM's secret redaction rules are applied.

## Release verification

Before release:

- verify Auditor, Operator, and Platform Owner capability projections;
- seed known passwords, tokens, URI credentials, and private-key markers and confirm they do not appear in safe API JSON or support reports;
- prove local recorder failure, safe-event-store failure, Docker evidence timeout, and disabled Seq do not stop normal API operations;
- restart the API and confirm safe event history remains queryable;
- verify no diagnostics endpoint accepts an arbitrary container ID or host path;
- rehearse disabling each diagnostics subsystem independently;
- retain the diagnostic files during application rollback unless an operator deliberately removes them.

## Developer event rules

New diagnostic events must:

- use a stable event code;
- include only safe, bounded message text;
- attach operation and logical resource context when known;
- avoid raw command output, environment values, request bodies, authorization headers, cookies, and unrestricted exception data;
- use the best-effort writer so a diagnostics failure cannot change workflow success or failure;
- preserve the owning workflow as the authoritative detailed evidence source.

## Browser outage fallback

The Diagnostics Web workspace presents a fallback card when its overview request cannot reach the API. The fallback deliberately points outside the control plane:

- Portainer `mem-api` logs;
- `docker logs --tail 500 mem-api`;
- the persistent CLEF recorder configured under `Diagnostics:Logging:FilePath`.

This card can help while an already-loaded browser page remains open. A total API/Kestrel outage may also prevent the installed SPA from loading, so Portainer and host-shell access remain the authoritative emergency path.

## Development-only release fixture

The deterministic browser proof uses:

```text
POST /api/operator/diagnostics/test-fixture/incident
```

The route is mapped only when both conditions are true:

1. `ASPNETCORE_ENVIRONMENT=Development`;
2. `Diagnostics:TestFixture:Enabled=true`.

It also requires `ManagePlatform`. Production never maps this endpoint, even if configuration is incorrect. The fixture records only a bounded synthetic event and contains no deliberate exception, host path, credential, or Docker target.

## Core programme close-out boundary

The MEM-native Diagnostics first cut is complete when the following evidence is green:

- structured console and persistent CLEF logging;
- safe NDJSON event storage, retention, cursor integrity, and redaction;
- correlated Problem Details and incident creation;
- authorized overview, incident, event, health, report, and Docker-evidence APIs;
- Web incident, event, health, support-report, Docker-evidence, and outage-fallback UX;
- critical workflow instrumentation;
- backend failure, role, disk, report-bound, and no-cache tests;
- deterministic development browser proof;
- read-only deployed browser proof;
- API restart persistence and total-outage manual checks.

Seq remains optional follow-on work. The core Diagnostics experience must remain fully useful with Seq disabled.
