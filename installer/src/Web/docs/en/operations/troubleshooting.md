---
id: "operations/troubleshooting"
translationKey: "operations/troubleshooting"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Troubleshooting"
description: "Diagnose MEM 0.2.x using runtime truth, Diagnostics, support reports, and bounded Docker evidence instead of legacy stack.sh workflows."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["troubleshooting", "Diagnostics", "runtime", "support report", "Docker"]
route: "/docs/operations/troubleshooting"
aliases: []
outputPath: "docs/operations/troubleshooting.md"
preserveLegacyBranding: false
---
# Troubleshooting

Start with MEM's server-owned evidence. Do not begin by deleting containers, changing network membership, or editing generated files.

## Establish runtime truth

Use the Control Plane UI and, when required, the bounded runtime endpoint:

```text
GET /health/runtime
```

Confirm `runtimeMode`, Control Plane identity, API process identity, validation state, and private exposure before diagnosing a mutation.

## Use Diagnostics

For a failed operation, capture:

- operation ID;
- incident ID/event code;
- failed stage;
- current resource identity;
- redacted support report;
- current Docker evidence when available.

An occurrence count can represent multiple events for one failed operation; it does not necessarily mean the operator attempted the action multiple times.

## Control Plane unreachable

Check the supported private administration path and current container health. A healthy container is not permission to expose 8443 publicly.

## Platform service failure

Open the relevant Services page. Use its current readiness/functional evidence before choosing Restart & Verify or Repair. Do not repair simply because an old incident remains in history.

## Stack creation failure

Use the failed stage to narrow the problem. `generate-synapse-config`, route publication, readiness, and public verification are distinct stages. Preserve a partial operation before retrying.

For current production storage and NPM route details, see [Troubleshoot a chat server](../chat-servers/troubleshooting.md).

## Support evidence safety

Never include passwords, TOTP/recovery codes, TURN secrets, signing keys, private keys, unrestricted `.env`/configuration dumps, or raw database exports in support material.
