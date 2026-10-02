---
id: "chat-servers/create"
translationKey: "chat-servers/create"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Create a chat server"
description: "Create a new Matrix + Element Runtime Stack and verify the resources MEM owns."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["create stack", "Matrix", "Element", "domain", "container images"]
route: "/docs/chat-servers/create"
aliases: []
outputPath: "docs/chat-servers/create.md"
preserveLegacyBranding: false
---
# Create a chat server

## Outcome

MEM creates a new Matrix homeserver and Element web client under a selected platform domain, publishes their Nginx Proxy Manager routes, records the runtime manifest, and runs readiness checks.

## Before you begin

Confirm that:

- MEM installation and Platform Owner enrolment are complete;
- at least one active platform domain is available;
- the selected domain has the intended wildcard DNS and certificate setup;
- the proposed stack slug is unique and suitable for long-term use;
- you have enough storage for the database, media, backups, and future growth.

Use **Chat servers → Create stack**.

## Choose the stack identity

**Display name** is the friendly label entered during creation. The current runtime list and workspace primarily identify the stack by its slug.

**Stack slug** becomes the stable runtime token. The UI converts it to lowercase and keeps letters, numbers, and dashes. Review the normalized value before submitting.

For a slug such as `family`, MEM derives names similar to:

- Matrix: `matrix-family.example.org`
- Element: `chat-family.example.org`
- containers: `mem-matrix-family` and `mem-element-family`

The actual base domain comes from the selected active MEM domain.

> [!CAUTION]
> Treat the slug and Matrix server name as durable identity. Do not create a temporary name for a production community and assume it can later be renamed without migration consequences.

## Choose the domain and images

Select the domain that should serve Matrix and Element. MEM defaults to the main platform domain when one is available.

The form also exposes the Synapse and Element image names and versions. Keep the approved defaults unless you are deliberately testing a specific image. Choosing an arbitrary tag can change compatibility and may cause candidate validation or readiness failure.

## What MEM creates

The server operation:

- resolves the selected platform domain and certificate reference;
- provisions a per-stack PostgreSQL database and role;
- creates or reuses the stack registration secret;
- prepares Matrix and Element data directories;
- writes Synapse and Element configuration;
- starts the Matrix and Element containers on `mem-gateway`;
- publishes HTTPS routes through Nginx Proxy Manager;
- writes platform TURN settings when coturn is ready;
- verifies internal HTTP, public HTTPS, NPM, and route readiness;
- persists the runtime manifest and durable operation evidence.

When coturn is not ready, creation can finish with a warning and without TURN. Voice and video may then require a later reviewed connection.

## Submit and track

Select **Create stack**. MEM accepts the request as a durable server-side operation and shows the current creation stage while Docker, files, ingress, PostgreSQL, Matrix, and Element are prepared.

The browser no longer owns the mutation lifetime. You may refresh or leave the page after the operation has been accepted; returning to the create page in the same browser profile resumes tracking of the accepted operation.

If the durable operation reaches a failed terminal state, MEM shows the failed stage and operation reference and does not automatically start another attempt. Open Diagnostics before submitting a new stack request.

## Verify success

After navigation to the stack workspace:

1. confirm Matrix and Element public addresses are present;
2. run **Doctor** if the latest readiness is not clearly healthy;
3. open **Network & domains** and confirm the expected hosts and route identifiers;
4. open **Users**, synchronize the inventory, and create the first Matrix administrator;
5. open Element and sign in.

Do not treat container existence alone as proof that the public chat service is ready.
