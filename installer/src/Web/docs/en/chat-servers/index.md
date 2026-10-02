---
id: "chat-servers"
translationKey: "chat-servers"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Create and operate chat servers"
description: "Create, verify, administer, and safely retire Matrix + Element stacks from the MEM Control Plane."
order: 0
status: "supported"
appliesTo: ["0.2.x"]
tags: ["chat servers", "Matrix", "Element", "operations", "stack workspace"]
route: "/docs/chat-servers"
aliases: []
outputPath: "docs/chat-servers/index.md"
preserveLegacyBranding: false
---
# Create and operate chat servers

A **chat server** in MEM is one managed Matrix homeserver with an Element web client. MEM calls this pair a **Runtime Stack** or simply a **stack**.

## Outcome

After following this section, you can:

- create a new Matrix + Element stack on an installed MEM platform;
- verify public and internal readiness;
- create the first Matrix administrator and later users;
- inspect services, routes, storage, TURN, federation, and operation history;
- run routine checks and create a backup before risky work;
- remove the active runtime without assuming that retained data was erased.

## What belongs to one stack

MEM creates and records a stack-scoped Matrix identity, Synapse configuration, Element configuration, Matrix and Element containers, a PostgreSQL database and role, filesystem storage, Nginx Proxy Manager routes, secrets, readiness evidence, and operation history.

Platform services such as PostgreSQL, Nginx Proxy Manager, and coturn are shared. Matrix users, rooms, messages, media, federation policy, and most runtime evidence belong to the individual stack.

> [!IMPORTANT]
> Matrix accounts are not MEM Control Plane accounts. A Platform Owner signs in to administer MEM. A Matrix user signs in to Element or another Matrix client.

## Recommended first-server sequence

1. [Create a chat server](create.md).
2. [Read the stack workspace and status](workspace.md).
3. [Synchronize and create Matrix users](users.md).
4. Open Element and sign in with the first Matrix administrator.
5. Review [Network and domains](network-and-domains.md), [Voice and video](voice-and-video.md), and [Federation](federation.md).
6. Establish a [daily operating routine](daily-operations.md).

## Guides in this section

- [Create a chat server](create.md)
- [Use the stack workspace](workspace.md)
- [Manage Matrix users](users.md)
- [Reset passwords and manage account lifecycle](passwords-and-accounts.md)
- [Inspect network and domains](network-and-domains.md)
- [Configure voice and video TURN](voice-and-video.md)
- [Manage federation](federation.md)
- [Understand storage and media](storage-and-media.md)
- [Run daily operations safely](daily-operations.md)
- [Remove a chat server](remove.md)
- [Troubleshoot a chat server](troubleshooting.md)

Backup, restore, migration, and full diagnostics have their own bounded workflows. Use the stack workspace links to enter those workflows rather than treating them as ordinary container operations.
