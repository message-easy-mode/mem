---
id: "installation/review-and-install"
translationKey: "installation/review-and-install"
locale: "en"
groupId: "installation"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Review and run the install plan"
description: "Freeze the exact reviewed plan, cross the explicit mutation boundary, and follow the durable platform installation."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["install plan", "Review", "Postgres", "NPM", "Diagnostics"]
route: "/docs/installation/review-and-install"
aliases: []
outputPath: "docs/installation/review-and-install.md"
preserveLegacyBranding: false
---
# Review and run the install plan

Review and installation are deliberately separate stages.

## Review before mutation

Review presents the exact canonical platform plan, including the selected domain, wildcard certificate intent, Docker network, PostgreSQL, Nginx Proxy Manager, ports, support tools, and the actions that will occur after installation starts.

The protected DNS, PostgreSQL, and NPM credentials do not belong in the frozen reviewed JSON. Saving the protected NPM administrator credential therefore does not rewrite the plan fingerprint.

Accepting Review:

- freezes the exact plan and fingerprint;
- records the operator's reviewed intent;
- does **not** yet mutate Docker, DNS, ACME, certificate storage, or NPM.

If plan-affecting input changes later, Review must be invalidated rather than silently reused.

## Install platform

Continue to **Install platform**. The page explicitly identifies this action as the external-mutation boundary.

When you choose **Install platform**, the durable server-owned worker can:

- create or verify `mem-gateway`;
- create persistent volumes;
- install the host `mem` command when the release payload is available;
- start or verify `mem-postgres` and wait for readiness;
- start or verify `mem-npm`;
- create and verify the first NPM administrator on a fresh NPM runtime;
- create the DNS-01 challenge and request the reviewed wildcard certificate;
- store and validate the certificate;
- import or resolve it in NPM;
- start selected support tools;
- run final platform verification;
- persist every durable step and attempt.

New plans do not install or route separate `mem-api` or `mem-web` containers. Old persisted step names are accepted only as retirement no-ops for compatibility.

## Waiting for operator action

A step can enter `WaitingForUser` rather than fail. Follow the action shown by MEM, correct the reported condition, and resume the same installation.

## Failure and Retry

When a step fails:

1. read the failed stage and safe error;
2. use **Run diagnostics** or download the bounded support report when useful;
3. correct the specific cause;
4. Retry the same durable installation when MEM says Retry is allowed.

Completed steps remain authoritative. Do not repeatedly delete containers, volumes, installation records, or certificate state between attempts.

## Optional support tools

The selected-support-tools stage may install release-approved support tooling such as Portainer. Seq remains optional, and Diagnostics must continue to work without Seq.
