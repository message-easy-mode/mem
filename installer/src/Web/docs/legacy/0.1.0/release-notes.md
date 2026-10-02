---
id: "releases/0.1.0"
translationKey: "releases/0.1.0"
locale: "en"
groupId: "releases"
groupKey: "releases"
groupLabel: "Releases"
groupOrder: 70
title: "MatrixEasyMode v0.1.0"
description: "First public MatrixEasyMode release establishing the initial self-hosted deployment model and platform foundation."
order: 20
status: "legacy"
appliesTo: ["0.1.0"]
tags: ["v0.1.0", "Initial Release", "Self Hosted", "Docker Compose"]
route: "/docs/releases/0.1.0"
aliases: []
outputPath: "docs/releases/0.1.0.md"
preserveLegacyBranding: true
---
# MatrixEasyMode v0.1.0

> [!IMPORTANT]
> **First public release**
>
> MatrixEasyMode v0.1.0 is the first public release of the project.
>
> This release establishes the initial deployment model, runtime structure, installation flow, and documentation foundation for MatrixEasyMode.

**Topics:** v0.1.0 · Initial Release · Self Hosted · Docker Compose

## Release overview

v0.1.0 is the first public release of MatrixEasyMode.

This release establishes the initial platform foundation for deploying and operating Matrix + Element on infrastructure you control using Docker Compose, PostgreSQL, and Nginx Proxy Manager.

The goal of this release is not to present a finished platform with every feature already implemented.

The goal is to establish:

- the deployment model
- the runtime structure
- the operational workflow
- the ingress model
- the documentation posture
- the foundation future releases will build on

## What this release includes

The v0.1.0 runtime model includes:

- PostgreSQL
- Nginx Proxy Manager
- MatrixEasyMode API
- MatrixEasyMode web frontend

This release also introduces:

- staged startup flow
- installer-driven `.env` generation
- Docker Compose deployment helpers
- NPM-backed ingress bootstrap
- public hostname configuration
- wildcard certificate integration
- local and registry image modes
- initial operations and troubleshooting documentation

## Deployment model

v0.1.0 establishes the current MatrixEasyMode deployment flow:

1. start infrastructure
2. configure ingress and certificates
3. start the application layer
4. verify runtime state

Typical install flow:

```bash
./install.sh
./stack.sh up infra
./stack.sh up app
```

This deployment model intentionally keeps infrastructure visible rather than hiding it behind a fully managed abstraction layer.

## Runtime shape

The standard runtime in this release consists of:

### Infrastructure layer

- `postgres`
- `npm`

### Application layer

- `api`
- `web`

The staged startup order matters because the MatrixEasyMode API integrates with Nginx Proxy Manager and expects ingress prerequisites to already exist before application startup.

## Documentation introduced in this release

v0.1.0 also establishes the first public documentation set for MatrixEasyMode.

That includes:

- Get Started
- Installation Guide
- Advanced Install
- Configuration
- Architecture
- Network Behavior
- Operations
- Troubleshooting
- Release Notes

The documentation is intended to help users understand how MatrixEasyMode works, how the deployment behaves, and how the different runtime components fit together.

## Registry mode and local mode

This release supports two image modes:

### Registry mode

Uses published application images from a container registry.

This is the normal deployment path for most installations.

### Local mode

Uses locally built application images.

This is mainly intended for contributors or development-oriented workflows.

Both modes share the same overall runtime architecture.

## What users should expect from v0.1.0

This release should be viewed as an early platform foundation rather than a fully mature product.

Users should expect:

- a hands-on deployment flow
- visible infrastructure setup
- explicit ingress configuration
- real hostname and DNS requirements
- staged startup behavior
- Docker Compose-based operations
- log and status inspection during bring-up

The intended mindset for this release is:

review, deploy, verify.

## Current platform assumptions

The current deployment model assumes:

- Linux host environment
- Docker Engine and Docker Compose
- operator-controlled DNS
- HTTPS certificate management
- Nginx Proxy Manager as the ingress layer
- a self-hosted environment you control

These are part of the current product phase and deployment philosophy.

## Upgrade impact

For most users, v0.1.0 will be a fresh installation rather than an upgrade target.

Because this is the first public release:

- there is no earlier public release line
- this version establishes the baseline deployment posture
- future upgrades should be compared against this release

## Recommended first-run workflow

Recommended flow for first-time users:

1. read [Get Started](https://matrixeasymode.com/get-started)
2. review the [Installation Guide](../installation/index.md)
3. configure DNS and certificates
4. deploy infrastructure first
5. deploy the application layer second
6. verify logs, ingress, and public URLs
7. keep the operations and troubleshooting docs nearby during bring-up

## Foundation release

v0.1.0 is primarily a foundation release.

Its purpose is to establish the correct long-term shape of the project:

- self-hosted
- infrastructure-aware
- deployment-focused
- explicit runtime behavior
- documentation-first
- built on existing open-source foundations
- designed to evolve over time

## Related documentation

- [Get Started](https://matrixeasymode.com/get-started)
- [Installation Guide](../installation/index.md)
- [Advanced Install](../installation/advanced-install.md)
- [Configuration](../operations/configuration.md)
- [Operations Guide](../operations/operations-guide.md)
- [Troubleshooting](../operations/troubleshooting.md)
- [Upgrading](../operations/upgrading.md)
- [Architecture](../architecture/index.md)
- [Network Behavior](../operations/network-behavior.md)
