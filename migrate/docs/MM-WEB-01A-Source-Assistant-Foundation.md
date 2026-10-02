# MM-WEB-01A — Source Assistant foundation

## Status

MM-WEB-01A establishes the temporary source-side Web host and React shell. It
does not yet assess a MEM 0.1.0 source, list stacks, create captures, or create
migration packages.

## Safe operating model

The default listener is:

```text
127.0.0.1:7391
```

Use an SSH tunnel for remote browser access:

```bash
ssh -L 7391:127.0.0.1:7391 <operator>@<source-host>
```

Open:

```text
http://localhost:7391
```

The process generates a 140-bit human-readable access code at startup. The
plaintext code is printed once and is not written to the source journal. A
successful login receives an opaque HTTP-only, SameSite=Strict process-local
session cookie. State-changing API calls also require the session CSRF token.

The Web process is temporary. Stop it with Ctrl+C when the source operation is
finished.

## Remote binding

A non-loopback address requires:

```bash
mem-migrate-web \
  --listen-address 192.168.1.20 \
  --port 7391 \
  --allow-remote
```

Use this only on a trusted LAN or VPN. Wildcard addresses such as `0.0.0.0`
and `::` are rejected even with `--allow-remote`. The installer does not open
a firewall port.

## Source build

```bash
cd src/Mem.Migrate.Web/ClientApp
npm ci
npm run test
npm run build

cd ../../..
dotnet restore Mem.Migrate.sln
dotnet build Mem.Migrate.sln --no-restore

dotnet test \
  tests/Mem.Migrate.Web.Tests/Mem.Migrate.Web.Tests.csproj \
  --no-build
```

The Vite build writes production assets into:

```text
src/Mem.Migrate.Web/wwwroot
```

Node.js is a build-time dependency only. The installed source host receives
compiled static files and a self-contained .NET executable.

## Run from source

```bash
dotnet run \
  --project src/Mem.Migrate.Web/Mem.Migrate.Web.csproj
```

Optional configuration:

```text
--listen-address <ip>
--port <1-65535>
--allow-remote
--workspace <private-path>
--artifacts <private-path>
--session-minutes <5-1440>
--session-absolute-minutes <5-1440>
```

Environment equivalents:

```text
MEM_MIGRATE_WEB_LISTEN_ADDRESS
MEM_MIGRATE_WEB_PORT
MEM_MIGRATE_WEB_ALLOW_REMOTE
MEM_MIGRATE_WORKSPACE
MEM_MIGRATE_ARTIFACTS
MEM_MIGRATE_WEB_SESSION_MINUTES
MEM_MIGRATE_WEB_SESSION_ABSOLUTE_MINUTES
```

Command-line options override environment values. The default authenticated session policy is a 240-minute sliding idle timeout with a 1440-minute (24-hour) absolute maximum. The Source Assistant header displays the effective configured policy so operators can see when re-authentication may be required.

## Publish a combined release

```bash
./scripts/publish-current.sh \
  --output /tmp/mem-migrate-publish \
  --runtime linux-x64 \
  --configuration Release
```

The combined payload contains both:

```text
mem-migrate
mem-migrate-web
wwwroot/
libe_sqlite3.so
```

Install atomically:

```bash
sudo ./scripts/install-current.sh \
  --publish-dir /tmp/mem-migrate-publish \
  --version 0.2.0-alpha.1
```

The installer verifies:

- the existing CLI under a minimal environment;
- the conversion-worker help contract;
- the Source Assistant version command;
- the static React entry point;
- a real loopback `/api/health` response;
- atomic release and `current` symlink activation.

Start the installed Web host:

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

## Current API

Anonymous:

```text
GET  /api/health
POST /api/access/login
GET  /api/access/session
```

Authenticated:

```text
POST /api/access/logout
GET  /api/host/status
GET  /api/preflight
```

`/api/preflight` currently reports only Web-foundation readiness. Source
assessment and stack inventory are delivered by MM-WEB-01B.

## Security boundaries

The first foundation deliberately provides no endpoint for:

- shell commands;
- Docker commands;
- arbitrary filesystem paths;
- source assessment;
- source capture;
- package generation;
- package download;
- age private identities;
- legacy MEM credentials.

CORS is not enabled. Restrictive CSP, framing, referrer, permissions, content
type, cross-origin, and no-store headers are returned for browser content and
API responses.
