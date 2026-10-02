---
title: Assess the source and select one stack
description: Run the read-only source assessment, resolve blockers, and bind the migration to exactly one legacy stack.
section: Migrate from MEM 0.1.0
order: 30
---

# Assess the source and select one stack

The Source Assistant must understand the old installation before it is allowed to capture data.

## Outcome

The source assessment confirms a supported MEM 0.1.0 profile, any blockers are understood, and exactly one source stack is selected for this migration.

## Run the source assessment

In the Source Assistant, start the assessment. It inspects the legacy application database, Docker runtime, Matrix stack files, and other required source evidence.

Assessment is read-only. It does not stop containers, publish routes, change the old database, or rewrite the source configuration.

Review the classification and recommendation. A confirmed supported MEM 0.1.0 result can proceed. A probable, repairable, blocked, or unsupported result requires the operator to read the detailed findings and resolve the stated condition before capture.

Do not treat a warning as permission to bypass a blocker. Preserve the assessment ID and source fingerprint in your migration evidence.

## Select the intended stack

If multiple source stacks are discovered, use **Select stack** on the one you intend to migrate. Verify:

- stack slug;
- Matrix hostname;
- Element hostname;
- container and data identity;
- source fingerprint;
- expected users, rooms, and media scope where the assessment exposes it.

The selection is authoritative. Capture and package creation are bound to this stack, and the target Control Plane must not select a different one during intake.

## Safety and impact

Selecting a stack does not mutate it. It defines the boundary of the future capture. The package contains only the selected stack’s required Matrix data, configuration, signing identity, media, Element configuration, and minimal provenance.

Do not select a test stack merely to continue the workflow. A package for the wrong Matrix identity must be discarded and recreated from a new, correct selection.

## Verify success

You should see:

- a supported assessment state;
- one clearly selected source stack;
- no unresolved capture blocker;
- a stable assessment and selection after browser refresh.

If the source changes materially after assessment, run a fresh assessment before capture.

Next: [Import the target request, capture, and create the package](create-package.md).
