# Message Easy Mode

**Message Easy Mode (MEM)** is an open-source, self-hosted control plane for deploying and operating Matrix/Element communication servers with guided setup, domains and certificates, TURN, backups and restore, migration, diagnostics, and operator tooling.

MEM is designed for operators who want to own the infrastructure and data while avoiding the amount of manual integration normally required to assemble and maintain a production-shaped Matrix deployment.

**Project lineage:** Message Easy Mode 0.2.0 succeeds Matrix Easy Mode 0.1.x. The project was renamed from Matrix Easy Mode to Message Easy Mode for the 0.2.x line. Version 0.2.0 is the first public source release in `message-easy-mode/mem` under the Message Easy Mode name, so the version number continues the existing product history rather than restarting at 0.1.0.

## Project links

| Surface | Location |
|---|---|
| Website | https://messageeasymode.com/ |
| Operator documentation | https://messageeasymode.com/docs |
| Public source | https://github.com/message-easy-mode/mem |
| Release artifacts | https://github.com/message-easy-mode/mem-releases |
| Control Plane images | `ghcr.io/message-easy-mode/mem-control-plane` |

The GitHub `mem` repository is the public, release-aligned source surface. Day-to-day development is maintained through the project development workflow before approved source states are published publicly. Release artifacts remain separate in `mem-releases`.

## What MEM provides

MEM brings the main operational surfaces for a self-hosted Matrix/Element service into one control plane, including:

- guided first-time platform setup;
- Matrix and Element stack lifecycle management;
- domain registration, DNS-backed certificate issuance and renewal workflows;
- shared Coturn/TURN management and validation;
- Matrix user administration;
- native backups, portable exports, restore rehearsal and recovery workflows;
- migration tooling for supported legacy MEM installations;
- diagnostics and support-report generation;
- a host-installed `mem` CLI for supported operator workflows; and
- English and German embedded operator documentation.

The public operator manuals are the authority for supported workflows and current operational guidance. This README intentionally stays focused on the source tree and developer entry points.

## Supported release host

The MEM 0.2.x release line is qualified for:

```text
Ubuntu Server 24.04 LTS
amd64 / x86_64
Docker + Docker Compose
```

Additional host operating systems or architectures should not be assumed supported unless they are explicitly added to the release contract and qualification programme.

For normal installation, start with the official website and deployment documentation rather than cloning this repository merely to install MEM.

## Get the source

Clone the public released-source repository:

```bash
git clone https://github.com/message-easy-mode/mem.git
cd mem
```

For a specific release, check out its exact tag:

```bash
git checkout v0.2.0
```

Release tags are intended to provide the source snapshot corresponding to that published MEM release. Built installers, CLI binaries, Migrate bundles, SBOMs and release manifests are distributed separately through `mem-releases`.

## Developer quick start

`dev/mem-env` is the supported development harness for the MEM Control Plane. Start by checking the local toolchain and runtime state:

```bash
./dev/mem-env doctor
./dev/mem-env status
./dev/mem-env authority status
```

### Managed local source development

```bash
./dev/mem-env local up
./dev/mem-env status
```

The normal local development surfaces are:

```text
Web / Vite    http://127.0.0.1:5173
API           http://127.0.0.1:7105
```

Backend source changes are handled by the managed source-watch workflow; Vite provides normal frontend HMR.

Stop the local environment with:

```bash
./dev/mem-env local down
```

### Production-shaped container development

Build and run the local Control Plane image:

```bash
./dev/mem-env container build
./dev/mem-env container up
./dev/mem-env status
```

The normal containerized development surface is private loopback HTTPS:

```text
https://127.0.0.1:8443
```

If containerized development is already running and you rebuild the image, recreate the container before treating browser proof as evidence for the new build:

```bash
./dev/mem-env container restart
./dev/mem-env status
```

See [`dev/README.md`](dev/README.md) for the full runtime, authority, reset and E2E development contract.

## Build and test from source

The commands below are the normal broad gates for the major source areas. Run focused tests first when changing a specific feature.

### Control Plane backend

```bash
cd installer/src

dotnet build MemInstaller.sln
dotnet test MemInstaller.sln
```

### Web application

```bash
cd installer/src/Web

npm ci
npm run docs:check
npm run test
npm run build
```

### MEM CLI

From the repository root:

```bash
dotnet test \
  ./cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj
```

Release-grade CLI packaging is owned by `scripts/release/build-cli-release.sh` rather than by an ad-hoc local `dotnet publish` command.

### MEM Migrate

```bash
cd migrate

dotnet restore Mem.Migrate.sln
dotnet build Mem.Migrate.sln --no-restore
dotnet test Mem.Migrate.sln --no-build
```

MEM Migrate also contains the browser-based Source Assistant under `migrate/src/Mem.Migrate.Web/ClientApp` and dedicated bootstrap/release tests beneath `migrate/bootstrap/tests`.

## Repository layout

```text
bootstrap/          host bootstrap and release-install entry points
cli/                MEM operator CLI source and tests
dev/                supported local/container development harness
images/             source-controlled image assets used by the product
installer/          Control Plane container/build boundary
installer/src/      API, HostAgent, domain modules, shared code and Web UI
migrate/            MEM Migrate CLI, Source Assistant, bootstrap and tests
scripts/release/    deterministic release producers and supply-chain gates
scripts/public-source/
                    deterministic public-source preparation and verification
```

Important root files include:

```text
CHANGELOG.md         release/change history
CONTRIBUTING.md      contribution expectations
GOVERNANCE.md        project governance
SECURITY.md          private vulnerability-reporting policy
SUPPORT.md           community support boundaries
LICENSE.txt          GNU AGPLv3 license text
LICENSING-NOTE.md    plain-language licensing rationale
```

## Release and source provenance

MEM deliberately separates source publication from binary release distribution:

```text
approved source state
    ├── public source snapshot -> github.com/message-easy-mode/mem
    └── release producers      -> github.com/message-easy-mode/mem-releases
                                  + ghcr.io/message-easy-mode/mem-control-plane
```

The release tooling binds public artifacts to an exact source commit, release identity and digest-bearing Control Plane image. The public-source tooling separately prepares a deterministic, reviewed source snapshot and verifies it before publication.

Useful entry points:

- [`scripts/public-source/README.md`](scripts/public-source/README.md) — public-source preparation and verification;
- [`scripts/release/README.md`](scripts/release/README.md) — product release producers;
- [`scripts/release/RELEASE-CONTRACT.md`](scripts/release/RELEASE-CONTRACT.md) — release manifest and identity contract;
- [`scripts/release/SUPPLY-CHAIN.md`](scripts/release/SUPPLY-CHAIN.md) — release provenance and publication boundary.

## Documentation

Operator documentation is maintained through the Message Easy Mode website and synchronized into the Control Plane as a reviewed embedded snapshot for offline/product use.

Use the public documentation for installation, networking, domains/certificates, TURN, backup/restore, migration, security and troubleshooting guidance:

https://messageeasymode.com/docs

## Security

Do not report suspected vulnerabilities through public issues or pull requests. Follow [`SECURITY.md`](SECURITY.md) for the private reporting process and current security scope.

Never commit credentials, recovery codes, TOTP secrets, private keys, production databases, runtime state, local `.env` files, or development output containing sensitive material.

## Contributing and governance

MEM is a maintainer-led open-source project. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before investing in substantial changes and [`GOVERNANCE.md`](GOVERNANCE.md) for the decision-making model.

The public source repository is intended to make released MEM source inspectable and independently buildable. The active development workflow may use a separate development forge; the contribution documents define the current path for proposing and reviewing changes.

## License

Message Easy Mode is released under the **GNU Affero General Public License v3.0 (AGPLv3)**.

See [`LICENSE.txt`](LICENSE.txt) for the license and [`LICENSING-NOTE.md`](LICENSING-NOTE.md) for the project's plain-language licensing rationale.
