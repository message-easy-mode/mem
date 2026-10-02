# MEM Portainer runtime provider

This module owns the exact Portainer runtime contract used by the explicit MEM
installation workflow.

## Approved release

- Edition: Portainer Community Edition
- Release line: 2.39 LTS
- Exact approved new-install tag: `portainer/portainer-ce:2.39.5`
- Approved architectures: `amd64`, `arm64`
- Container name: `portainer`
- Persistent data volume: `portainer_data`
- HTTPS container port: `9443`
- Edge tunnel port `8000`: not published
- Public NPM ingress: not created

The exact tag may be pulled only during explicit setup preparation. After that,
MEM resolves Docker's local `sha256:` image ID and creates the container from
that immutable local identity. Status and ordinary start/stop paths never pull.

## Existing installations

An existing same-name Portainer container without a matching MEM runtime record
is observed as unmanaged and remains read-only. MEM does not adopt, replace, or
upgrade it automatically. A working Portainer CE 2.39.1 LTS installation is
therefore preserved as-is; 2.39.5 is the exact policy only for new MEM-managed
installs. A future explicit upgrade workflow remains separate.

## Storage and replacement

The provider mounts the named `portainer_data` volume at `/data` and the Docker
socket at `/var/run/docker.sock`. Container removal or replacement must keep
`removeVolumes=false`; deleting Portainer data is not part of this provider.

## Rollback

Before an explicit future upgrade:

1. back up Portainer configuration/data;
2. retain the previous exact image locally;
3. record its immutable local image ID;
4. stop and replace only the managed container;
5. reuse `portainer_data`;
6. verify the configured UI authority and local environment before discarding the newer
   container/image.

Do not downgrade Portainer without checking data compatibility. This module does
not perform automatic upgrades or downgrades.

## Diagnostics handoff

The normal operator-facing Portainer authority and environment ID remain
server-owned under `Diagnostics:Portainer`. MEM never derives either value from
browser input.

In the interactive development runtimes (`local-development` and
`containerized-development`), Diagnostics may additionally project a bounded
`https://localhost:<published-port>` home-page fallback from the live MEM-observed
Portainer HTTPS binding. The browser still runs on the developer host in both
modes, so the published host port is the appropriate browser authority. This is
a developer/onboarding escape hatch for first-time setup and manual recovery
before the private handoff authority is configured. It does not create an
environment ID, does not enable exact resource routes, is not used in
`containerized-production`, and is never derived from browser origin.

The Diagnostics handoff layer may provide:

- the Portainer home page;
- the configured local environment;
- the environment's containers list;
- a best-effort exact container route for the approved 2.39 LTS UI contract;
- in interactive development only, the local Portainer home when exact handoff
  configuration is not yet available.

Exact links are resolved from logical MEM resources at request time. If a safe
Portainer home exists but no environment ID is configured, a logical handoff may
fall back to the home page rather than failing closed on an unavailable exact
container route. The browser
never supplies a Docker container ID. When a current container cannot be
resolved, or the approved route contract is unavailable, MEM falls back to the
configured containers list. Portainer authentication remains Portainer-owned.
