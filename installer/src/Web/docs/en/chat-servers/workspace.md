---
id: "chat-servers/workspace"
translationKey: "chat-servers/workspace"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Use the stack workspace"
description: "Interpret stack readiness, services, actions, navigation areas, and recorded operation evidence."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["stack workspace", "readiness", "Doctor", "operations", "Element"]
route: "/docs/chat-servers/workspace"
aliases: []
outputPath: "docs/chat-servers/workspace.md"
preserveLegacyBranding: false
---
# Use the stack workspace

## Outcome

Use the stack workspace as the normal operating surface for one Matrix + Element stack.

Open **Chat servers**, then select the stack name or **Inspect**.

## Header actions

The workspace header provides stack-scoped actions:

- **Open Element** opens the recorded public Element URL in a new tab.
- **Matrix API** opens the recorded homeserver address.
- **Refresh** reloads the current projections.
- **Run doctor** checks Nginx Proxy Manager, configured routes, internal HTTP, and public HTTPS.
- **Create backup** starts the stack backup workflow.

A recorded URL is not itself a live health check. Use Refresh and Doctor when current state matters.

## Overview

The Overview page combines:

- the last known runtime status and verification time;
- Matrix and Element public hosts;
- the latest Doctor result retained in the current browser session;
- the most recent locally recorded backup operation;
- recent control-plane operations with requested, current, and completed states.

The **last verified** value is historical evidence. It does not mean the endpoint was checked at the moment you opened the page.

## Workspace sections

**Services** shows Matrix and Element runtime-manifest facts, including containers, internal addresses, public routes, certificate identifiers, database metadata, and paths. Most values are read-only evidence.

**Storage & media** shows recovery-relevant files and media usage.

**Users** synchronizes Matrix accounts and provides guarded account workflows.

**Backups** and **Recovery** enter the dedicated recovery plane.

**Federation** manages Public, Restricted, and Local-only federation through reviewed operations.

**Network & domains** shows recorded public and internal delivery facts.

**Voice & video** inspects and manages the stack TURN association.

**Diagnostics** presents Doctor evidence for the current browser session and links into the broader diagnostics surface.

## Read-only evidence versus mutation

Many workspace panels intentionally do not expose generic edit, start, stop, or restart controls. MEM uses dedicated reviewed workflows for changes that can affect identity, availability, federation, calls, or recovery.

Do not edit Synapse YAML, Element config, NPM routes, or MEM-owned database records merely because a workspace card shows their paths or identifiers. An out-of-band change can create drift and disable safe automation.
