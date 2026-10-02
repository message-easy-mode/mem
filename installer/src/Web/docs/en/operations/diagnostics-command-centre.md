---
id: "operations/diagnostics-command-centre"
translationKey: "operations/diagnostics-command-centre"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Use the Diagnostics command centre"
description: "Understand MEM incidents, safe events, CLEF recording, pipeline verification, alert attention, support reports, and outage fallback."
order: 30
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Diagnostics", "Incidents", "Logging health", "Support report", "Alert bell"]
route: "/docs/operations/diagnostics-command-centre"
aliases: []
outputPath: "docs/operations/diagnostics-command-centre.md"
preserveLegacyBranding: false
---
# Use the Diagnostics command centre

MEM Diagnostics is the interpreted operator layer for control-plane failures and operational evidence. Open **Diagnostics** from the operator navigation or use the alert bell in the header when an incident needs attention.

## Read the health strip correctly

The health strip reports independent states for:

- overall Diagnostics readiness;
- incidents that currently need attention;
- the persistent local CLEF recorder;
- the browser-safe event store;
- optional Seq delivery and runtime state.

A neutral Seq state does not make MEM unhealthy. A green overall state must not hide a disabled or unavailable recorder or event store.

## Understand the three evidence layers

### Incidents

Incidents are grouped warning, error, or critical events that require operator attention. They contain a safe summary, feature, timestamps, correlation references, and links back to the owning workflow when MEM can establish one.

### Safe events

The safe event store contains browser-readable, redacted operational events. Information events can prove normal activity without creating an incident. The store deliberately excludes unrestricted exception, secret, and host-path data.

### CLEF technical recorder

The local CLEF recorder is the broader structured black-box log. It remains useful when the browser-safe event store has no matching event or when the API is unavailable. Do not distribute a complete recorder file without reviewing it for sensitive operational metadata.

## Find and filter technical events

The summary separates **Attention** from **Technical activity**. A warning-level technical event can be useful evidence without creating an incident that requires operator action.

Select the Incidents, Warning, Error, Critical, Information, or Events counter to open the corresponding view. Event severity, feature, and search filters are stored in the URL, so the result can be refreshed or bookmarked.

The Technical events view shows the newest safe events first and does not apply a severity filter by default. Select **Load 50 more events** to retrieve the next bounded cursor page. MEM uses deliberate cursor loading rather than automatic infinite scroll so keyboard focus, browser memory, and evidence boundaries remain predictable.

When no incident requires attention but safe events exist, the incident empty state links directly to Technical events.

## Verify the Diagnostics pipeline

A Platform Owner can run **Verify diagnostics pipeline** from `/diagnostics` or Logging Health.

The verification:

1. checks that the local recorder is writable;
2. writes one harmless Information event;
3. reads that exact safe event back;
4. verifies the correlation round trip;
5. reports Seq separately as passed, failed, disabled, or not configured.

It does not create a warning or error incident and does not mutate Docker.

## Use the alert bell

The header bell uses server-provided incident state. It shows at most five recent warning, error, or critical incidents and links to exact incident records. It is not a notification inbox: there is no browser-local read state, dismissal history, assignment, or acknowledgement workflow.

If the attention endpoint is unavailable, the bell must not present the system as all clear.

## Build a support handoff

Open an incident and use **Copy support JSON** or **Download support report**. Docker evidence is bounded and optional. Review the report before sharing it because hostnames, stack names, timestamps, event codes, and topology may still be sensitive.

Never attach credentials, access tokens, recovery codes, signing keys, private keys, database dumps, unrestricted configuration files, or complete raw logs to an ordinary support request.

## When the API is unavailable

If an already-loaded Diagnostics page cannot reach the API, use the external fallback paths:

```bash
sudo docker logs --tail 500 mem-control-plane
```

You can also inspect the canonical `mem-control-plane` container in Portainer and review the configured persistent CLEF location from the host. The installed single-page application cannot guarantee availability during a complete Kestrel outage, so external inspection is intentional.

## Roles

- **Auditor:** safe overview, attention summaries, and incident summaries.
- **Operator:** technical events, support reports, Logging Health, and incident-owned Docker evidence.
- **Platform Owner:** Operator capabilities plus pipeline verification, Seq management, and Portainer handoff.

Hidden controls are not authorization. Every operation is re-authorized on the server.

## Rollback and recovery

If a new Diagnostics surface causes trouble, hide or roll back that Web surface while retaining the existing incident, event, recorder, and support-report APIs. Disabling optional Seq or Portainer integration must not disable MEM-native Diagnostics.

## Related documentation

- [Use Seq with MEM](seq-with-mem.md)
- [Use Portainer for advanced container diagnostics](../tools/optional-portainer.md)
- [Use restore evidence, logs, and support reports](../backups-and-restores/evidence-logs-support.md)
