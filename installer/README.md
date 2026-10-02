# MEM Control Plane installer and backend development

This directory contains the production Control Plane container/build boundary and the main .NET/Web source tree used by Message Easy Mode.

For normal day-to-day development, use the repository-level `dev/mem-env` harness rather than assembling ad-hoc local service commands. See [`../dev/README.md`](../dev/README.md).

## Source layout

```text
installer/
├── Dockerfile
├── docker-compose.yml
└── src/
    ├── Api/
    ├── Api.IntegrationTests/
    ├── HostAgent/
    ├── HostAgent.Tests/
    ├── Infrastructure/
    ├── Modules/
    ├── Shared/
    ├── Web/
    └── MemInstaller.sln
```

## Backend build and test

From the repository root:

```bash
cd installer/src

dotnet build MemInstaller.sln
dotnet test MemInstaller.sln
```

Use focused project/test filters first when working in a specific feature area, then return to the full solution gate before close-out where required.

## Web application

```bash
cd installer/src/Web

npm ci
npm run docs:check
npm run test
npm run build
```

For interactive Web/API development, prefer:

```bash
cd "$(git rev-parse --show-toplevel)"
./dev/mem-env local up
./dev/mem-env status
```

The normal local surfaces are Vite on `127.0.0.1:5173` and the API on `127.0.0.1:7105`.

## Production-shaped container build

The supported development harness builds the real Control Plane image boundary and supplies the canonical Migrate source context:

```bash
cd "$(git rev-parse --show-toplevel)"

./dev/mem-env container build
./dev/mem-env container up
./dev/mem-env status
```

If containerized development is already running after a rebuild, recreate it before browser/live proof:

```bash
./dev/mem-env container restart
./dev/mem-env status
```

## Entity Framework migrations

Create migrations deliberately and review the generated files before applying them. From `installer/src`:

```bash
dotnet ef migrations add <MigrationName> \
  --project ./Infrastructure/Infrastructure/Infrastructure.csproj \
  --startup-project ./Api/Api.csproj \
  --context AioDbContext \
  --output-dir Persistence/Migrations
```

Apply the current migrations to the selected development database with:

```bash
dotnet ef database update \
  --project ./Infrastructure/Infrastructure/Infrastructure.csproj \
  --startup-project ./Api/Api.csproj \
  --context AioDbContext
```

Do not generate or apply an EF migration merely because a source slice happens to touch persistence code. Follow the active engineering workflow and inspect the resulting migration explicitly.

## Local state and cleanup

Repository-local development state is managed beneath `dev/.state/` by `dev/mem-env`. Do not commit development databases, Data Protection keys, logs, generated certificates, browser test output, credentials, or other runtime state.

Use the harness reset commands instead of hard-coded workstation paths:

```bash
cd "$(git rev-parse --show-toplevel)"

./dev/mem-env reset interactive --confirm DELETE_MEM_DEV_STATE
```

For a broader clean-room development-host reset, inspect the plan first:

```bash
./dev/mem-env reset host --dry-run
```

See [`../dev/README.md`](../dev/README.md) for the exact authority, reset, E2E and container-development contracts.
