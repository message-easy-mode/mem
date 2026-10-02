---
id: "migrate/upload-and-review"
translationKey: "migrate/upload-and-review"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Upload and review the old server"
description: "Upload the encrypted package to MEM 0.2.0, validate it, and review the source identity and warnings before conversion."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["package upload", "validation", "old server", "compatibility", "step-up"]
route: "/docs/migrate/upload-and-review"
aliases: []
outputPath: "docs/migrate/upload-and-review.md"
preserveLegacyBranding: false
---
# Upload and review the old server

Return to the target MEM 0.2.0 Control Plane with the encrypted migration package created for that intake.

## Outcome

The target has accepted and validated the package, bound the Migration Session to its single source stack, and shown the operator the old-server identity, compatibility findings, and warnings before conversion.

## Upload the package

Open the matching migration intake and choose the package upload action. Complete step-up authentication if requested, then select the `.memmigration.zip.age` file.

MEM verifies the intake binding, recipient identity, package structure, archive integrity, source profile, selected-stack identity, and supported contracts. It decrypts working material only inside the target migration boundary.

A package produced for another intake or target must fail closed. Do not create a new target selection to work around a mismatch; return to the source workflow and create the correct package.

## Review the old server

Use **Review the old server** to confirm:

- legacy product and version;
- selected stack identity;
- Matrix and Element hostnames;
- source and package fingerprints;
- captured configuration and storage scope;
- compatibility result;
- warnings, limitations, or operator acknowledgements;
- whether the package represents the normal preview/simplified path or an advanced final-frozen handoff.

Resolve blocking findings before conversion. Preserve the package report and the target validation evidence.

## What MEM stores

The encrypted package, validated source archive, hashes, and safe provenance remain migration evidence in protected target storage according to the session lifecycle. They do not appear as ordinary Backup Catalog entries.

The selected source stack is already authoritative. Intake does not offer a second stack choice.

## Verify success

- The upload belongs to the intended intake.
- Package validation is complete.
- The old-server identity matches the operator’s plan.
- No blocking compatibility issue remains.
- The session can advance to **Prepare and test**.

If validation fails, do not manually alter the package. Compare the request, intake ID, recipient fingerprint, package report, source assessment, and transfer checksum, then recreate the package where necessary.

Next: [Convert and run the private test](private-test.md).
