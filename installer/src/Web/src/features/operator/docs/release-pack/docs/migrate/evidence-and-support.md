---
title: Use migration evidence, logs, and support information
description: Collect the safe assessment, package, conversion, staging, route, verification, and completion evidence needed to diagnose a migration.
section: Migrate from MEM 0.1.0
order: 130
---

# Use migration evidence, logs, and support information

A migration crosses two hosts and several durable operations. Good evidence identifies the exact source, package, target attempt, route state, and verification result without exposing secrets.

## Outcome

You can collect the minimum safe evidence needed to diagnose a failed or uncertain migration and retain the records needed for later audit or support.

## Source-side evidence

Retain:

- Source Assistant version and listener information;
- source assessment ID, classification, recommendation, and fingerprint;
- selected stack identity;
- target request intake ID and public recipient fingerprint;
- capture ID and completion state;
- package filename, kind, size, SHA-256, and package report;
- cancellation or local-deletion records where applicable.

Do not share the startup access code, raw signing key, passwords, room-key exports, tokens, or private source files.

## Target-side evidence

Retain or download the safe information available for:

- intake and package validation;
- source identity and compatibility review;
- conversion attempts and verified candidate hashes;
- private staging creation and health checks;
- target preflight and ownership claims;
- readiness or preview revision;
- Nginx Proxy Manager route snapshot and apply outcome;
- production verification;
- acceptance and legacy retention;
- first native backup;
- staging cleanup;
- completion report and session lifecycle actions.

Use the Migration ID, operation ID, timestamps, and stable error or problem codes when asking for help.

## Safe support summary

A useful report states:

```text
Source assessment ID: <id>
Selected source stack: <slug>
Source fingerprint: <safe fingerprint>
Target Migration ID: <id>
Current guided stage: <stage>
Last operation: <kind / status / timestamp>
Package SHA-256: <hash>
Private test: <not started / passed / failed>
Route ownership: <old / new / restored / unknown>
Production verification: <status>
First native backup: <status>
Observed error code: <code>
```

Redact public user identities where they are not required. Never paste database connection strings, authorization headers, access codes, recovery secrets, signing-key content, or decrypted package content into a support request.

## When route ownership is uncertain

Prioritize the recorded pre-cutover snapshot, apply result, rollback result, current Nginx Proxy Manager targets, and public readiness checks. Do not guess based only on which browser tab loaded.

## Evidence retention

Keep the completion report and first native backup evidence with your release and change records. An archived session remains the durable target record; archiving is not evidence deletion.

Related: [Rollback boundaries](rollback.md) and [Retain or clean up migration resources](cleanup.md).
