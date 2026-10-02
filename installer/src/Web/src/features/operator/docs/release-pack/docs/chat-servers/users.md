---
title: Manage Matrix users
description: Synchronize the authoritative Synapse inventory and create the first administrator or later Matrix users.
section: Operate chat servers
order: 30
---

# Manage Matrix users

## Outcome

Synchronize the stack's local Synapse account inventory, create the first Matrix administrator, and add later Matrix users.

Open the stack workspace and select **Users**.

## Matrix users are separate from operators

MEM Control Plane users administer the platform. Matrix users belong to one homeserver and sign in through Element or another Matrix client.

A Platform Owner account does not automatically create a Matrix account, and a Matrix administrator is not automatically a MEM operator.

## Synchronize before changing accounts

MEM reads the local account inventory from the stack's Synapse database. This inventory is the authority used to decide whether an active administrator exists and whether user creation is safe.

User creation remains disabled while inventory is unavailable, unsynchronized, synchronizing, or failed. Select **Synchronize users** and wait for a successful result before treating an empty table as an empty homeserver.

The table can include:

- accounts created through MEM;
- accounts discovered directly from Synapse;
- active, deactivated, pending, failed, or missing projections;
- administrator and first-administrator markers.

## Create the first Matrix administrator

When Synapse reports no active local administrator, MEM presents **Create the first Matrix admin**. The first account is forced to administrator status and is created through the stack's local shared-secret registration path.

Choose a durable administrator username and a strong password. Record the password in an appropriate password manager, then sign in to Element and establish the user's Matrix encryption recovery material.

## Create later users

After inventory confirms at least one active administrator, use **Create Matrix user**.

The form accepts:

- username;
- password of at least eight characters;
- optional display name;
- optional email;
- optional Matrix administrator role.

Usernames are normalized to lowercase and retain letters, numbers, `.`, `_`, `-`, and `=`. Review the normalized username before submission. The resulting Matrix ID uses the stack's Matrix server name, for example `@alice:matrix-family.example.org`.

## Verify success

After creation:

1. confirm the account appears as Active or Synced;
2. confirm the Matrix user ID and role;
3. sign in through the stack's Element URL;
4. for encrypted use, configure recovery and verify another device before relying on password-reset recovery.

A successful account creation followed by an inventory refresh warning can leave the Matrix account created while the MEM projection needs resynchronization. Synchronize again before creating a duplicate.
