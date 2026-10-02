---
id: "start/is-mem-right-for-you"
translationKey: "start/is-mem-right-for-you"
locale: "en"
groupId: "start"
groupKey: "start"
groupLabel: "Start here"
groupOrder: 0
title: "Is MEM right for you?"
description: "Decide whether MEM's Linux, Docker, private-control-plane, and self-hosting model fits your skills and operating needs."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["audience", "self-hosting", "Docker", "Linux", "ESS Community", "Kubernetes"]
route: "/docs/start/is-mem-right-for-you"
aliases: []
outputPath: "docs/start/is-mem-right-for-you.md"
preserveLegacyBranding: false
---
# Is MEM right for you?

MEM is designed for technically capable people who are willing to operate a Linux server but do not want to assemble and remember the complete Matrix platform by hand.

## MEM is likely a good fit when

You are comfortable with most of the following:

- maintaining an Ubuntu or similar Linux server;
- working with Docker containers, networks, mounted data, and logs;
- controlling DNS for a domain;
- securing required network paths;
- monitoring storage and applying host security updates;
- keeping backup copies outside the main host.

Typical uses include private family communication, a gaming community, a local club, a small organisation, or one operator hosting several independent Matrix stacks.

## MEM is not a zero-administration service

MEM removes repeated integration work, but the operator still owns the Linux host, DNS, connectivity, disk capacity, off-host backups, operating-system security, operator-account security, upstream updates, and user education around encryption recovery keys.

A dashboard cannot compensate for an unmaintained host or a backup retained only on the failed disk.

## MEM and ESS Community

ESS Community is Element's official open-source Matrix distribution and uses Kubernetes and Helm. It may be the better choice when you want the official Element deployment model or a direct path into the wider ESS family.

MEM serves a different preference: a Docker-first, host-level control plane with explicit backup, restore, migration, diagnostics, and multi-stack workflows.

The deciding question is not only which system is quickest to install. It is which operating model you are prepared to understand and recover when something goes wrong.

## Consider another approach when

MEM 0.2.0 may not be the right choice when you require multi-node high availability, automatic cluster scheduling, enterprise vendor support, a Kubernetes-native deployment, a fully managed service, or a messaging platform other than Matrix.
