---
id: "chat-servers/network-and-domains"
translationKey: "chat-servers/network-and-domains"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Inspect network and domains"
description: "Understand recorded Matrix and Element hosts, NPM routes, certificate references, and internal delivery."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["network", "domains", "Nginx Proxy Manager", "routes", "certificate"]
route: "/docs/chat-servers/network-and-domains"
aliases: []
outputPath: "docs/chat-servers/network-and-domains.md"
preserveLegacyBranding: false
---
# Inspect network and domains

## Outcome

Confirm which public and internal addresses MEM recorded for the stack, then use Doctor when you need a fresh route check.

Open the stack workspace and select **Network & domains**.

## Public naming

For a stack slug such as `family` under `example.org`, the normal creation plan uses:

- `matrix-family.example.org` for the Matrix homeserver;
- `chat-family.example.org` for the Element web client.

These hosts are derived from the selected active platform domain. Matrix identity is tied to the Matrix server name, so hostname changes are not ordinary cosmetic edits.

## What the page shows

For Matrix and Element, MEM can report:

- public domain and HTTPS address;
- Nginx Proxy Manager route ID;
- NPM certificate ID;
- internal container hostname and URL;
- the last recorded verification time.

These values come from the runtime manifest. MEM does not invent a route when the manifest is incomplete.

## Read-only boundary

The page does **not** directly inspect:

- current DNS-provider records;
- certificate expiry;
- the complete live Nginx Proxy Manager configuration;
- reachability from every external network.

Select **Run doctor** to check the current NPM service, route configuration, internal HTTP, and public HTTPS path. When external users still fail, verify authoritative DNS, firewall and NAT rules, and certificate status outside this read-only projection.

## Avoid unmanaged changes

Do not casually change Matrix or Element hosts directly in NPM or Synapse. An out-of-band route, certificate, or server-name change can cause manifest drift, break Element discovery, or change Matrix identity expectations.

Use the dedicated migration or recovery workflow when the intended outcome is a new hostname or adopted server identity.
