---
title: Use Portainer for advanced container diagnostics
description: Move safely from MEM's interpreted incidents to owner-only Portainer inspection using pinned runtime and server-authored links.
section: Tools
order: 20
---

# Use Portainer for advanced container diagnostics

MEM explains a failure; Portainer provides advanced low-level container inspection. Portainer does not replace MEM incidents, support reports, or workflow guidance, and MEM does not become a general Docker administration console.

## Open Portainer from MEM

A Platform Owner can open `/diagnostics/portainer` when the server has an authoritative private Portainer URL and environment ID. MEM may provide:

- **Open Portainer**;
- **Open local environment**;
- **Open containers**.

The browser does not construct these links from `localhost`, published ports, container names, or browser origin.

Portainer owns its own authentication session. MEM does not proxy or store a Portainer password, session token, or API key in browser storage.

## First-time setup and the five-minute window

A fresh Portainer 2.39.5 installation requires a one-time setup token before the first administrator can be created. Portainer prints that token to the server logs. On the Docker host, retrieve the latest token line with:

```bash
docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1
```

Copy the value after `setup_token=` into Portainer's **Setup token** field. The token is one-time use. The username defaults to `admin` but can be changed, and Portainer requires a password of at least 12 characters.

The first administrator must still be created within five minutes. If the window expires, restart only the Portainer container, reopen the UI immediately, then run the token command again so you use the newest setup token:

```bash
docker restart portainer
```

MEM never reads or stores the Portainer setup token or administrator password. Once the first administrator is created, Portainer's setup wizard detects the local Docker environment. In host-native development, MEM may open the running local Portainer home before an exact environment ID is configured; exact container handoff remains unavailable until that server-owned environment information exists.

## Open the current incident container

When an incident has current MEM-owned Docker evidence, **Open this container in Portainer** calls a MEM redirect endpoint. The server resolves the logical resource and current container identity at request time.

This avoids stale browser links when a container is recreated. Unsupported, removed, or unresolved resources fall back to the configured containers list instead of a known-broken detail page.

The Seq workspace uses the same model for **Open Seq container in Portainer**.

## Ownership boundary

Only MEM-owned logical resources are eligible for contextual handoff. The browser cannot submit an arbitrary Docker container ID. An unmanaged or identity-mismatched resource remains read-only and is not presented as MEM-owned.

MEM exposes no container start, stop, restart, remove, exec, prune, network mutation, or volume deletion action through Diagnostics.

## Approved runtime policy

New MEM-managed installations use the exact approved Portainer CE image:

```text
portainer/portainer-ce:2.39.5
```

The installation workflow resolves the local immutable `sha256:` image identity and creates the container from that identity. Ordinary status, start, and stop operations do not pull an image.

The runtime uses:

- the persistent `portainer_data` volume at `/data`;
- the Docker socket at `/var/run/docker.sock`;
- private HTTPS UI port `9443`;
- no Edge-agent port `8000` unless a future explicit workflow adds it;
- no automatic public NPM route.

An existing unowned Portainer installation, including a working 2.39.1 instance, is observed but not automatically adopted, replaced, relabelled, restarted, or upgraded.

## Exact-link compatibility

Exact resource links are a convenience tested against the approved Portainer 2.39 route contract. The containers list is the stable fallback. After changing the approved Portainer version, re-run direct-link compatibility proof before enabling exact handoff for the new release.

Official references:

- [Portainer documentation](https://docs.portainer.io/)
- [Install Portainer CE with Docker on Linux](https://docs.portainer.io/start/install-ce/server/docker/linux)
- [Portainer initial setup](https://docs.portainer.io/start/install-ce/server/setup)

## Rollback

To roll back contextual handoff, hide MEM's Portainer links; Portainer remains independently accessible. To roll back the runtime provider, use the documented previous approved immutable image only after checking data compatibility. Preserve `portainer_data` and never downgrade blindly.

Fresh-server release validation should confirm immutable image identity, data persistence, port `9443`, absence of port `8000`, ownership labels, no public NPM route, and exact-link fallback behavior.

## Related documentation

- [Use the Diagnostics command centre](../operations/diagnostics-command-centre.md)
- [Use Seq with MEM](../operations/seq-with-mem.md)
