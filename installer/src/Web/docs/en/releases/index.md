---
id: "releases"
translationKey: "releases"
locale: "en"
groupId: "releases"
groupKey: "releases"
groupLabel: "Releases"
groupOrder: 70
title: "Releases"
description: "Understand the current MEM 0.2.x release line and access preserved legacy release notes."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["releases", "0.2.x", "release candidates", "legacy"]
route: "/docs/releases"
aliases: []
outputPath: "docs/releases/index.md"
preserveLegacyBranding: false
---
# Releases

MEM 0.2.x uses a release-candidate qualification process before final publication. Release-specific artifacts, image digests, upgrade instructions, and known limitations are authoritative for the candidate or release being installed.

## Current product line

The documentation in the normal `en/` and `de/` trees targets MEM **0.2.x**. Historical 0.1.0 material is preserved separately and is marked legacy.

## Before installing a candidate or release

Confirm:

- exact version and source commit;
- immutable Control Plane image digest;
- release artifact checksums;
- supported Ubuntu/architecture;
- release-specific upgrade or fresh-install instructions;
- open qualification observations that affect your deployment.

Do not apply an old Compose/`stack.sh` procedure to MEM 0.2.x merely because it appears in historical notes.

## Legacy release notes

- [Version 0.1.0 legacy notes](0.1.0.md) — historical first public release.
