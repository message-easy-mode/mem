# MEM RUNTIME-PG-C03 — Product MEM Migrate installation

The product runtime is installed as immutable versioned releases beneath:

```text
/opt/mem/migrate/releases/<version>
```

The active release is selected atomically through:

```text
/opt/mem/migrate/current
```

## Build the combined publish payload

MM-WEB-01A publishes two product executables:

```text
mem-migrate
mem-migrate-web
```

Create one installable payload with:

```bash
./scripts/publish-current.sh \
  --output /tmp/mem-migrate-publish \
  --runtime linux-x64 \
  --configuration Release
```

The script:

1. runs `npm ci` for the Source Assistant client;
2. builds the production React assets;
3. publishes the CLI as a self-contained application;
4. publishes the Source Assistant as a self-contained application;
5. combines both outputs without silently overwriting conflicting files;
6. verifies the required executables, static assets, and SQLite companion.

The resulting payload includes:

```text
mem-migrate
mem-migrate-web
wwwroot/
libe_sqlite3.so
```

The projects currently request single-file publishing. A multi-file
self-contained publish is also accepted when the runtime emits companion files.

Node.js is a build-time dependency only. It is not required on the installed
MEM 0.1.0 source host.

## Install

```bash
sudo ./scripts/install-current.sh \
  --publish-dir /tmp/mem-migrate-publish \
  --version <release-version>
```

Before activating the release, the installer proves:

- the CLI runs under a minimal environment with no `DOTNET_ROOT` or user-local
  runtime;
- the conversion-worker help contract remains available;
- the Source Assistant executable runs under the same minimal environment;
- the Source Assistant production `wwwroot/index.html` exists;
- the Source Assistant starts on a temporary loopback port and returns HTTP 200
  from `/api/health`.

A failed validation leaves `/opt/mem/migrate/current` unchanged.

## Start the temporary Source Assistant

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

The safe default listens only on:

```text
127.0.0.1:7391
```

For a browser on another computer, create an SSH tunnel instead of opening a
public firewall port:

```bash
ssh -L 7391:127.0.0.1:7391 <operator>@<source-host>
```

Then open `http://localhost:7391` and use the access code printed by the source
process. Stop the process with Ctrl+C when the temporary management surface is
no longer required.
