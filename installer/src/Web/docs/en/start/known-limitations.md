---
id: "start/known-limitations"
translationKey: "start/known-limitations"
locale: "en"
groupId: "start"
groupKey: "start"
groupLabel: "Start here"
groupOrder: 0
title: "Known limitations"
description: "Understand the deliberate MEM 0.2.0 capability boundary before relying on the platform."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["limitations", "support", "security", "migration", "high availability", "Matrix"]
route: "/docs/start/known-limitations"
aliases: []
outputPath: "docs/start/known-limitations.md"
preserveLegacyBranding: false
---
# Known limitations

MEM 0.2.0 has a deliberately bounded first substantial release. Review these limits before placing important communications on the platform.

## Matrix only

MEM 0.2.0 manages Matrix and Element environments. It does not currently manage XMPP, IRC, email, Mattermost, or other messaging systems. Multi-protocol support is not a 0.2.0 feature.

## Single-host Docker model

The release is designed around a conventional Docker host. It does not provide multi-node clustering, automatic failover, Kubernetes scheduling, a high-availability PostgreSQL cluster, or geographic replication. Separate stacks on one host still share that host's failure domain and selected platform services.

## Migration scope

The current migration adapter supports a defined legacy MEM 0.1.0 source profile. It is not a general importer for every Synapse version, arbitrary Compose layout, ESS Community, manually assembled Matrix installation, or unrelated proxy convention.

## Client encryption keys

MEM can back up and restore server-side Matrix data. It cannot recreate end-to-end encryption keys that users failed to preserve on devices or in key backup. Password resets and server moves can therefore leave old encrypted history unreadable for a user with missing recovery material.

## Control-plane privilege

The Control Plane can change Docker resources and MEM-owned files. A compromise of the Control Plane can become a compromise of the managed environment. Keep the administration surface private.

## Opinionated ingress

The current guided installer supports Nginx Proxy Manager and a deSEC-based DNS-01 workflow. Arbitrary existing proxies and DNS providers are not yet first-class guided options.

## General Services console

The broad Services Operations Console is not part of the normal 0.2.0 surface. Specialist coturn and stack-level controls remain available; general Docker emergency access may still require Portainer or host commands.

## Sizing and upstream behaviour

Preflight thresholds are not a universal sizing calculator. Federation, public rooms, media retention, backups, migration workspaces, and multiple stacks can raise resource requirements substantially.

MEM also depends on upstream Matrix, Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn, and Docker behaviour. Consult release notes and capability status for the exact supported build.
