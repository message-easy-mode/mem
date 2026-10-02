---
title: Install or migrate?
description: Choose the safe path for a fresh host, a partially prepared target, or a supported MEM 0.1.0 source.
section: Start here
order: 40
---

# Install or migrate?

Choose the installation path before MEM changes the host. A fresh installation and a migration are different workflows with different safety boundaries.

## Fresh installation

Use the normal setup flow on a clean or deliberately prepared target without a legacy MEM 0.1.0 application.

The current setup journey determines installation or repair mode, checks the host and Docker, reviews storage and ports, configures domain and certificate, reviews planned resources, creates or verifies the MEM network and persistent storage, starts PostgreSQL and Nginx Proxy Manager, runs verification, and hands control to the operator dashboard.

The Control Plane is already the private application running setup. New plans do not deploy separate `mem-api` and `mem-web` application containers.

## Existing legacy MEM 0.1.0

If MEM detects old `mem-api` or `mem-web` containers without a current installation record, it directs the operator to MEM Migrate. Do not run a fresh installation over the legacy server.

The safe path is to prepare a clean target, create a target request, assess and capture one source stack, create the encrypted package, import and validate it, convert and privately stage the candidate, then review, adopt, verify, and finish.

The current adapter is for supported MEM 0.1.0 sources. It is not a universal importer for every manually assembled Synapse server.

## Partial MEM resources

Existing MEM-owned services or stacks without a completed installation record are treated as a repair or review condition, not automatically as a clean installation.

Inspect the reported resources and diagnostics. Do not delete a database, volume, network, or container merely because it is not yet represented correctly in the UI.

## Protect encrypted-message recovery

Server migration and restore preserve encrypted events stored on the server, but cannot recreate end-to-end encryption keys that existed only on user devices.

Before password resets, device replacement, or migration, confirm that important users have a recovery key or key backup and, where possible, another verified device.

| Current situation | Correct starting point |
|---|---|
| Clean target host | Normal MEM setup |
| Current installation with a failed dependency | Repair and diagnostics |
| Old `mem-api` / `mem-web` source | MEM Migrate |
| Arbitrary non-MEM Synapse server | Not supported by the current migration adapter |
