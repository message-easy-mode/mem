---
title: MEM product packages
description: Understand the MEM Control Plane, MEM CLI, mem-migrate CLI, and MEM Migrate Source Assistant as separate deliverables.
section: Start here
order: 30
---

# MEM product packages

MEM 0.2.0 is delivered as a small set of cooperating products rather than one universal executable.

## MEM Control Plane

The Control Plane is the authoritative server-side package on the target host. It provides the Web UI, ASP.NET Core API, operator identity, setup and operational workflows, privileged HostAgent runtime access, SQLite state, diagnostics, and the local documentation reader.

## MEM CLI

The installed operator command is:

```bash
mem
```

MEM CLI is a remote client for the Control Plane. It does not reimplement Docker or recovery logic locally. It supports profiles, browser-approved named-device login, English or German human output, and stable English JSON fields.

Normal CLI credentials are stored through the operating-system secret service. Local or SSH execution does not bypass server-side roles, audit, or step-up rules.

## MEM Migrate CLI

The migration command is:

```bash
mem-migrate
```

MEM Migrate is a separate, version-specific product containing knowledge of the supported MEM 0.1.0 source layout. It assesses and captures a legacy source, verifies artifacts, creates packages, and performs worker-side conversion operations.

On the target, the Control Plane invokes the installed executable as a worker and validates its structured events, reports, paths, and output hashes. Legacy migration code is not linked into the normal control-plane runtime.

## MEM Migrate Source Assistant

The Source Assistant is a temporary local ASP.NET Core and React application on the old server. It guides access-code login, source assessment, stack selection, target-request import, capture, encrypted-package creation, download, and local lifecycle.

Its safe default is loopback-only access. It is not the permanent management interface for the target server.

## Which package do I need?

| Goal | Package |
|---|---|
| Install and operate MEM 0.2.0 | MEM Control Plane |
| Script or inspect the Control Plane | MEM CLI |
| Migrate from supported MEM 0.1.0 | MEM Migrate |
| Use a guided browser on the old server | MEM Migrate Source Assistant |
