---
id: "migrate/install-source-assistant"
translationKey: "migrate/install-source-assistant"
locale: "en"
groupId: "migrate"
groupKey: "migrate"
groupLabel: "Migrate from MEM 0.1.0"
groupOrder: 19
title: "Install and open the Source Assistant privately"
description: "Install MEM Migrate on the legacy host, start the Source Assistant, and reach it through loopback or an SSH tunnel."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Source Assistant", "bootstrap", "SSH tunnel", "private access", "age"]
route: "/docs/migrate/install-source-assistant"
aliases: []
outputPath: "docs/migrate/install-source-assistant.md"
preserveLegacyBranding: false
---
# Install and open the Source Assistant privately

The MEM Migrate bootstrap installs a verified release on the legacy source host without requiring Git, .NET, Node.js, npm, or a source checkout.

## Outcome

MEM Migrate is installed beneath `/opt/mem/migrate`, its durable working state remains beneath `/var/lib/mem-migrate`, and the Source Assistant is reachable only through a private local connection.

## Supported bootstrap host

The current bootstrap supports Ubuntu Server 24.04 on amd64/x86_64. It requires an existing reachable Docker daemon. It validates Docker but does not install Docker, change its repositories, enable services, restart containers, or mutate the legacy stack.

It preserves a working `age` command or installs the supported Ubuntu package when required.

## Run the hosted bootstrap

Use the MEM Migrate release host supplied with the MEM 0.2.0 release artifacts. Do not invent or substitute an untrusted download location.

First run the non-modifying plan:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host> \
      --dry-run
```

Review the reported operating system, architecture, Docker reachability, `age` state, requested release, and installation plan. Then apply the same release:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host>
```

The installer verifies the release manifest, outer and inner checksums, archive root, entry types, and payload before atomically changing the current release link. Existing state under `/var/lib/mem-migrate` is preserved.

## Start the Source Assistant

Run it in a terminal on the source host:

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

The safe defaults listen on `127.0.0.1:7391`. The terminal prints the startup access code. Keep that terminal available while using the assistant.

The current release does not promise stable `mem-migrate-start`, `mem-migrate-stop`, or `mem-migrate-status` host commands. Do not document or rely on those names until the installed release provides them. Stop the foreground process with `Ctrl+C` when you have finished the source-side work.

## Open it through an SSH tunnel

From your operator workstation:

```bash
ssh -N \
  -L 7391:127.0.0.1:7391 \
  <operator>@<source-host>
```

Then open:

```text
http://localhost:7391
```

Enter the access code printed by the Source Assistant process.

> [!WARNING]
> Public internet exposure is unsupported. Do not bind the Source Assistant to a wildcard address or create a public reverse-proxy route for it.

## Verify success

- The browser shows the Source Assistant access page.
- The access code opens the workspace.
- The first normal action is source assessment.
- Refreshing the browser does not erase durable assessment, capture, package, or journal state.

If the browser cannot connect, verify that the source process is still running, the SSH session remains open, and local port `7391` is not already in use.

Next: [Assess the source and select one stack](assess-and-select.md).
