---
title: Installation troubleshooting
description: Diagnose bootstrap, access, host-check, certificate, install, verification, and first-owner failures.
section: Installation
order: 90
---

# Installation troubleshooting

Troubleshoot the stage that failed. Do not erase durable state before collecting evidence.

## Bootstrap does not start

Run:

```bash
sudo ./install.sh --dry-run
sudo docker info
sudo docker compose version
sudo docker ps -a --filter name=mem-control-plane
sudo docker ps -a --filter name=mem-installer
```

Common causes include:

- unsupported Ubuntu version;
- insufficient memory or disk;
- missing package repository access;
- Docker daemon unavailable;
- selected Control Plane port already in use;
- Control Plane image pull failure.

When the Control Plane container exists but is stopped, inspect its logs before restarting it:

```bash
sudo docker logs mem-control-plane
```

For a **real** bootstrap run, also inspect the root-only bootstrap evidence under:

```text
/var/log/mem/bootstrap/
```

`latest.log` points to the most recent transcript. A failed run also leaves `mem-bootstrap-<UTC-run-id>-failure.txt` with the failed phase, safe operation, mutation state, retained volume, bounded/redacted runtime evidence, and suggested recovery commands. Collect this report before deleting a failed Control Plane container.

A `--dry-run` intentionally creates no persistent transcript because dry-run must remain non-mutating.

## Setup code is missing

Retrieve it from the host:

```bash
sudo ./install.sh --show-setup-token
```

If the Control Plane data volume does not exist or contains no token, do not invent one in the browser. Re-run the official bootstrap and review why persistence was unavailable.

## Browser cannot reach the Control Plane

Check:

```bash
sudo docker ps --filter name=mem-control-plane
sudo ss -ltnp | grep 8443
sudo docker logs mem-control-plane
```

Confirm the selected port, firewall, VPN, or SSH tunnel. A self-signed certificate warning is expected; a connection refusal is not.

## Legacy Control Plane runtime name detected

A container named `mem-installer` with the reviewed volume `mem-installer-data` is an earlier MEM 0.2.0 Control Plane identity, not a MEM 0.1.0 source. Run the current bootstrap dry run and review the migration plan:

```bash
sudo ./install.sh --dry-run
```

The real run reuses the existing volume and certificates, verifies the canonical runtime, and rolls back on failure. Do not manually rename the volume. If both `mem-control-plane` and `mem-installer` exist, or only `mem-installer-data` remains, bootstrap stops without mutation and requires investigation.

## Setup reports a legacy installation

If `mem-api` or `mem-web` is detected, stop. Use MEM Migrate.

Do not rename or delete those containers to bypass detection. They identify the old application boundary and may point to data required for migration.

## Host checks show unexpected MEM resources

Record:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

Use the repair/investigation path and Diagnostics. Determine whether the resources belong to a failed current install, a test stack, a restore workspace, a migration staging runtime, or a legacy deployment.

## Certificate operation fails

Check:

- base domain and deSEC zone;
- token correctness and zone permission;
- outbound HTTPS and DNS;
- ACME staging versus production;
- DNS propagation;
- existing conflicting TXT records;
- NPM readiness and credentials.

Do not paste the deSEC token into a support report.

A browser timeout can occur while backend cleanup continues. Refresh the domain page and inspect the recorded last result before retrying.

## Installation waits for user

Open the linked domain or ingress action, correct the readiness issue, and resume the existing installation.

`WaitingForUser` is a durable pause, not a reason to create a second install plan.

## Installation fails

Use the persistent failure panel and **Run diagnostics**. When the Control Plane is available, use **Open diagnostics** to review the correlated incident and technical-event evidence before falling back to raw logs.

Record:

- installation ID;
- failed step;
- incident ID;
- trace or correlation ID;
- expected and observed state;
- bounded support report.

The active Diagnostics programme stores safe operator evidence separately from raw technical logs. When the API is unavailable, fall back to:

```bash
sudo docker logs mem-control-plane
```

and the configured persistent control-plane log directory.

## Verification warns or fails

Open each verification check. Confirm:

- install run and all steps;
- Docker;
- `mem-postgres`;
- `mem-npm`;
- NPM initialization and API access;
- active main certificate;
- private-key match;
- NPM certificate visibility.

Do not continue to public stack creation while a required platform check is failed.

## First-owner bootstrap fails

Confirm that:

- no completed Platform Owner already exists;
- the setup code is exact;
- the bootstrap grant has not expired;
- username and password validation passed;
- TOTP time is correct on both server and authenticator device;
- recovery codes were stored before finishing.

Never include passwords, TOTP secrets, recovery codes, cookies, setup codes, or private keys in support material.

## Installation support report

When Setup or installation fails after the Control Plane is running, prefer the bounded MEM installation support report over copying raw logs.

Open **Setup troubleshooting** from the Setup activity page when available. It keeps the exact Installation ID, shows the runtime-correct host command, provides the bounded report download, and keeps raw Docker logs as a last-resort fallback.

From the Setup activity, Setup troubleshooting, or Finish page, use **Download support report**. The report includes the reviewed plan fingerprint, safe server-check snapshot, persisted installation timeline, related diagnostic events, recovery guidance, runtime identity, and bounded Docker evidence when it is available.

The report deliberately omits credentials, passwords, DNS provider tokens, private keys, raw authorization data, recovery codes, connection strings, complete container environment arrays, unrestricted command output, and raw CLEF files.

If the browser route is unavailable but the Control Plane container can run commands, generate the same report directly from the host.

Latest installation in production:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report --latest \
  > "mem-install-report-$(date -u +%Y%m%dT%H%M%SZ).json"
```

Known installation:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --installation-id "<installation-id>" \
  --format json \
  > mem-install-report.json
```

Containerized development:

```bash
docker exec mem-control-plane-dev \
  dotnet Api.dll support install-report --latest
```

If a failure happened before an Installation ID was available but a technical trace reference exists:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --trace-id "<trace-id>" \
  --format text
```

Docker evidence is included by default. Use `--no-docker-evidence` when only the durable Setup and diagnostic records are required. The command writes the report to stdout, writes operational errors to stderr, does not prompt for credentials, and will not create a missing Control Plane database.

As a last-resort raw fallback:

```bash
sudo docker logs --timestamps --tail 500 mem-control-plane
```

or during containerized development:

```bash
docker logs --timestamps --tail 500 mem-control-plane-dev
```

Raw logs can contain operational metadata and should be reviewed before external sharing. The generated support report is the preferred artefact.
