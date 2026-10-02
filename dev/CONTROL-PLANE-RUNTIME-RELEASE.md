# MEM Control Plane runtime and private-administration release proof

This guide is the production-shaped acceptance procedure for the MEM 0.2.0 Control Plane runtime and its private-administration boundary.

The release guarantee is:

> **MEM 0.2.0 does not support directly exposing the Control Plane on a publicly routable network interface.**
>
> **On explicitly selected trusted private networks, direct LAN administration is supported. Everywhere else, SSH tunnelling is the supported remote-management method.**

Matrix, Element, federation and TURN may be public because their workloads require public reachability. The MEM Control Plane is operator tooling and remains private.

## Supported administration modes

### SSH tunnel / local-only — default

Docker publishes the Control Plane only on host loopback:

```text
127.0.0.1:8443 -> 8443/tcp
```

Remote administration uses an SSH local forward:

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:8443:127.0.0.1:8443 \
  <operator>@<server>
```

Then open:

```text
https://127.0.0.1:8443
```

### Trusted LAN — explicit opt-in

MEM may bind the Control Plane to one concrete RFC1918 address assigned to the host, for example:

```text
192.168.10.20:8443 -> 8443/tcp
```

This is intended for a trusted on-premises management LAN, home lab, or management VLAN. Do not forward this port through Internet-facing NAT, public reverse proxying, or public DNS.

### Unsupported

The supported bootstrap must not produce:

```text
0.0.0.0:8443
[::]:8443
<public-ip>:8443
```

No public NPM route or public MEM administration hostname is required.

## Safety rules

- Do not rename, copy or delete the Control Plane data volume during this proof.
- Do not remove the previous Control Plane image until the acceptance window is complete.
- Do not run `mem-control-plane-dev`, local API/Vite or E2E while production migration is in progress.
- Never record setup tokens, passwords, TOTP secrets, recovery codes or private keys as release evidence.
- A failed migration must retain the previous container record for recovery.
- An unsafe previous Control Plane binding must **not** be restarted automatically after a failed hardening migration.
- Do not deliberately expose the ordinary development Control Plane on a wildcard/public address for testing. Use automated synthetic fixtures or a disposable isolated release host.

## 1. Source and regression gates

Run from the repository root:

```bash
./dev/control-plane-release-proof source-audit
bash dev/tests/control-plane-release-proof.test.sh
bash dev/tests/mem-env.test.sh

bash bootstrap/tests/control-plane-access-policy.test.sh
bash bootstrap/tests/control-plane-access-migration.test.sh
bash bootstrap/tests/control-plane-access-handoff.test.sh
bash bootstrap/tests/control-plane-runtime-name.test.sh
bash bootstrap/tests/control-plane-host-data.test.sh
bash bootstrap/tests/control-plane-migration.test.sh
bash bootstrap/tests/bootstrap-evidence.test.sh

cd installer/src
dotnet build MemInstaller.sln
dotnet test MemInstaller.sln

cd Web
npm run test
npm run build
cd ../../..

./dev/mem-env e2e all
```

The private-administration source audit verifies that containerized development remains loopback-bound and that bootstrap uses the reviewed address-aware publication helper rather than Docker's wildcard publication default.

The automated negative fixtures must prove that wildcard, public and multiple Control Plane bindings fail the release verifier without creating a real public listener on the developer host.

## 2. Build the release-shaped local image

The build is safe while an existing production/legacy Control Plane remains running:

```bash
./dev/mem-env container build

docker image inspect mem-control-plane:local \
  --format 'Image={{.RepoTags}} Created={{.Created}}'
```

Do not start the development container while production migration is in progress.

## 3. Inspect current runtime state

```bash
./dev/control-plane-release-proof status
```

When a canonical Control Plane exists, status also reports the observed administration mode and exact host binding.

For a healthy loopback installation:

```bash
./dev/control-plane-release-proof verify-private-admin \
  mem-control-plane \
  ssh-tunnel \
  127.0.0.1
```

For a Trusted-LAN installation:

```bash
./dev/control-plane-release-proof verify-private-admin \
  mem-control-plane \
  trusted-lan \
  192.168.10.20
```

The verifier requires exactly one Docker publication for container port 8443, validates the host address class, checks the expected address when supplied, and health-checks the private endpoint.

## 4. Capture safe legacy evidence

For a legacy `mem-installer` runtime, capture evidence before migration:

```bash
./dev/control-plane-release-proof capture-legacy \
  dev/.state/release/legacy-before-runtime-migration.tsv
```

The evidence file is mode `0600` and records only safe runtime identity, volume, HTTPS host binding, certificate fingerprint, and optional Control Plane instance identity. It does not contain setup tokens, passwords, private keys, recovery codes or database content.

Review the recorded fields:

```text
https_host_ip
https_port
https_binding
```

An existing `0.0.0.0:8443` or public binding is historical evidence to be corrected, not configuration to preserve into the canonical runtime.

## 5. Review the bootstrap migration plan

```bash
sudo ./install.sh \
  --dry-run \
  --control-plane-image mem-control-plane:local \
  --skip-mem-cli-host-command
```

Without an explicit Trusted-LAN selection, the migration target must be:

```text
Access:      ssh-tunnel
Host bind:   127.0.0.1:8443
```

A deliberately selected Trusted-LAN release proof must use both options:

```bash
sudo ./install.sh \
  --dry-run \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20 \
  --control-plane-image mem-control-plane:local \
  --skip-mem-cli-host-command
```

Do not continue if the plan proposes a wildcard/public binding, a new data volume for an upgrade, or an address that is not the reviewed private administration endpoint.

## 6. Rollback rehearsal

A rollback rehearsal may intentionally supply an image that cannot become a healthy MEM Control Plane.

For a **previously private** legacy/runtime binding, rollback may restart that known-private runtime.

For an **unsafe** previous wildcard/public binding, rollback must preserve the old container/state but leave it stopped. The hardening attempt must never silently reopen the exposure.

Use a disposable/release fixture for unsafe legacy proof. Do not turn the normal developer runtime into a public service.

## 7. Health-verified canonical migration

After the rollback contract is proven, perform the real migration with the reviewed image and administration mode.

Then verify the retained runtime state:

```bash
./dev/control-plane-release-proof verify-canonical \
  dev/.state/release/legacy-before-runtime-migration.tsv
```

`verify-canonical` now includes a private-administration binding gate. The accepted canonical runtime must be healthy, production-shaped, retain the reviewed data/certificate identity, retire the legacy container record, and have one supported private host binding.

## 8. Operator acceptance

Open the Control Plane through the selected private path.

### SSH mode

The direct public server address must not provide a supported MEM Control Plane endpoint. Establish the SSH tunnel printed by bootstrap and open `https://127.0.0.1:<port>`.

### Trusted LAN mode

Open the exact selected RFC1918 address from a device on the trusted LAN.

A browser warning for the MEM self-signed Control Plane certificate is expected. Compare the SHA-256 fingerprint with the fingerprint printed by bootstrap before accepting the exception.

In either mode, confirm:

- normal named MEM login still applies;
- TOTP MFA still applies;
- roles and recent step-up still protect high-risk actions;
- Home → Host status reports the expected private Control Plane access state;
- System Information reports the same access mode/address;
- Diagnostics → Control Plane runtime reports the same access mode/address;
- Diagnostics does not contain `control_plane.exposure.unsupported` for the healthy private runtime.

## 9. Internet/VPS acceptance

On a disposable supported Internet-hosted Ubuntu server with a publicly routable primary interface:

1. run bootstrap without a Trusted-LAN override;
2. prove Docker publishes `127.0.0.1:<port>` only;
3. prove `verify-private-admin mem-control-plane ssh-tunnel 127.0.0.1` passes;
4. from another host, prove direct `<public-ip>:<port>` access is unavailable;
5. prove the bootstrap-generated SSH tunnel command reaches MEM;
6. prove MEM login/MFA are still required through the tunnel;
7. restart the Control Plane and reboot the host;
8. prove the loopback binding remains unchanged;
9. confirm no public MEM admin DNS record or NPM proxy host is required.

## 10. Trusted-LAN acceptance

On a disposable supported on-premises host with a stable RFC1918 management address:

1. select Trusted LAN explicitly;
2. prove Docker publishes exactly `<selected-private-ip>:<port>`;
3. prove `verify-private-admin mem-control-plane trusted-lan <selected-private-ip>` passes;
4. access MEM from another device on that trusted network;
5. verify the self-signed certificate fingerprint;
6. prove MEM login/MFA remain required;
7. restart the Control Plane and reboot the host;
8. prove the exact selected binding remains unchanged;
9. confirm the Control Plane is not published through NPM or public DNS.

Use a static address or DHCP reservation for the selected Trusted-LAN address. MEM must fail visibly if that address disappears; it must never fall back to `0.0.0.0`.

## 11. Unsafe-drift acceptance

Automated API integration tests are the normal proof for unsupported runtime drift:

```bash
cd installer/src

dotnet test \
  ./Api.IntegrationTests/Api.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ControlPlaneExposureTests"

dotnet test \
  ./Api.IntegrationTests/Api.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ControlPlaneStartupDiagnosticPublisherTests"
```

They must prove:

```text
0.0.0.0 / public / multiple binding
→ Needs attention
→ control_plane.exposure.unsupported
→ Diagnostics incident
```

If a manual unsafe-drift proof is required, perform it only on a disposable isolated release host with external firewalling already blocking the chosen test port. Do not perform it on the ordinary developer or production host.

## 12. Fresh-host release gate

Before publishing MEM 0.2.0, repeat bootstrap on a disposable supported Ubuntu host with no MEM runtime present.

The fresh Internet/VPS path must create:

```text
container: mem-control-plane
volume: mem-control-plane-data
runtime: containerized-production
UI: embedded-spa
Control Plane: 127.0.0.1:<port>
administration: SSH tunnel
```

A second on-premises acceptance must prove the explicit Trusted-LAN mode.

Complete first-owner bootstrap, verify runtime exposure truth, then run the normal platform installation journey.

## Release decision

Do not accept the release if any supported path can silently produce `0.0.0.0:<port>`, `[::]:<port>`, or a publicly routable Control Plane host binding.

The release boundary is simple:

> Matrix is public because it has to be. The MEM Control Plane is private because it does not.
