---
id: "backups-restores/evidence-logs-support"
translationKey: "backups-restores/evidence-logs-support"
locale: "en"
groupId: "backups-restores"
groupKey: "backups-restores"
groupLabel: "Back up and restore"
groupOrder: 18
title: "Use restore evidence, logs, and support reports"
description: "Collect redacted, durable recovery evidence without exposing dumps, keys, credentials, or raw command output."
order: 110
status: "supported"
appliesTo: ["0.2.x"]
tags: ["restore logs", "evidence", "support report", "redaction", "event code"]
route: "/docs/backups-and-restores/evidence-logs-support"
aliases: []
outputPath: "docs/backups-and-restores/evidence-logs-support.md"
preserveLegacyBranding: false
---
# Use restore evidence, logs, and support reports

The Restore Workspace keeps separate views for operator progress, safe evidence, structured logs, configuration, and support handoff.

## Activity

**Activity** is the durable timeline. Use it to identify stage transitions, operation IDs, event codes, timestamps, and whether work was requested, started, completed, failed, or cancelled.

## Evidence

**Evidence** groups curated results such as backup readiness, private-test outcome, Standard Recreate, public verification, and handover. It is the fastest place to find the latest recorded success or failure without reading every log line.

## Logs

Restore logs are append-only structured events. They can be filtered by severity, stage, and search text. Each safe event can include:

- timestamp;
- restore-session and operation identity;
- stage;
- severity;
- stable event code;
- redacted message and allowlisted details.

Logs must not contain credentials, private keys, signing keys, tokens, connection strings, or unrestricted command output. The redactor is a final safety net, not a reason to paste secrets into an operator note.

## Configuration

**Configuration** shows safe source snapshots, target values, target claims, and recorded operation facts. Use it to distinguish what the operator requested from what a later runtime inspection happens to show.

## Generate a support report

Use **Generate support report** from the workspace rail. MEM downloads a formatted JSON report containing:

- MEM version and restore-session ID;
- attempt state and latest safe error summary;
- source and target identity;
- log counts and recent redacted events;
- operation summaries;
- warnings.

The report deliberately excludes database dumps, configuration files, credentials, private keys, and unrestricted raw logs.

## Before sharing

Review even a redacted report before sharing it. Hostnames, stack names, timings, event codes, user counts, and topology can still be sensitive in some environments.

Never attach the portable backup ZIP, PostgreSQL dump, signing key, `homeserver.yaml`, raw container logs, access tokens, or TOTP recovery material to an ordinary support request.

When the Control Plane UI is unavailable, preserve local logs and Docker evidence through the documented diagnostics fallback, then correlate them with the restore-session ID rather than inventing a new restore attempt.
