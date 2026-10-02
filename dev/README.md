# MEM Control Plane developer environments

`dev/mem-env` is the supported contributor command surface for running the MEM
Control Plane from source or as a locally built, production-shaped container.
It keeps the two mutable Control Plane modes exclusive by default and verifies
the server-owned runtime context before reporting success.

## Runtime modes

| Mode | Browser | API | State | Restart boundary |
|---|---|---|---|---|
| `local-development` | Vite on `127.0.0.1:5173` | local .NET process on `127.0.0.1:7105` | `dev/.state/interactive` (shared) | harness-owned local process group |
| `containerized-development` | embedded SPA over `https://127.0.0.1:8443` | `mem-control-plane-dev` | `dev/.state/interactive` (shared) | Docker Compose container |

A production or legacy `mem-control-plane`/`mem-installer` container is not a
developer mode. The harness refuses to start another mutable controller while
one of those runtimes is active.

## First checks

```bash
./dev/mem-env doctor
./dev/mem-env status
./dev/mem-env authority status
```

`status` calls the bounded `/health/runtime` preflight exposed by the active
API. It reports the runtime and process identities returned by the server; it
does not infer the mode from ports alone.

The same status view also reports both context-scoped development Seq runtimes.
The runtime identities are intentionally distinct:

| Control Plane context | Seq container | Preferred host port |
|---|---|---:|
| `local-development` | `mem-seq-local` | `16341` |
| `containerized-development` | `mem-seq-dev` | `17341` |

Use the bounded Seq helpers when investigating which runtime belongs to which
Control Plane context:

```bash
./dev/mem-env seq status
./dev/mem-env seq local inspect
./dev/mem-env seq container inspect
./dev/mem-env seq local logs
./dev/mem-env seq container logs
```

`inspect` deliberately prints only safe runtime evidence: state, observed host
port, `/data` bind source, MEM ownership labels, and the owning Control Plane
instance. It does not dump the container environment or Seq credentials.
Ownership is compared with the server-authoritative `/health/runtime` identity
when the corresponding Control Plane is reachable, including a local API run
from VS Code rather than by this harness. Other MEM-labelled Seq containers,
such as a retained pre-context-scoping `mem-seq`, are listed separately as
observed-only runtimes rather than being mistaken for either active context.

These commands never adopt, relabel, start, stop, or remove a Seq runtime. Seq
lifecycle remains owned by the matching MEM Control Plane. The only development
tooling exception is an explicitly confirmed full environment reset, described
below.

## Local source development

```bash
./dev/mem-env local up
./dev/mem-env local restart
./dev/mem-env local logs
./dev/mem-env local token
./dev/mem-env local down
```

`local up`:

1. refuses recognised competing Control Plane containers;
2. refuses unrelated listeners on ports `7105` or `5173`;
3. opens the shared interactive authority under `dev/.state/interactive`;
4. uses the same SQLite database, Data Protection keys, setup authority, Diagnostics history, secrets, HostAgent MEM data root, instance data and `ControlPlaneInstanceId` as container development;
5. starts the API with `MEM_RUNTIME_MODE=local-development` and runtime-specific service routing;
6. waits for `/health/runtime` to confirm `local-development` and `vite`;
7. starts Vite only after the API identity is proven.

Managed local development also defaults to **full API restart on backend source
save**. The harness runs the API under `dotnet watch --no-hot-reload`, so a
saved .NET source change rebuilds and starts a fresh API process instead of
relying on Hot Reload. Vite continues to provide its normal frontend HMR. This
is the preferred everyday managed-local experience because startup-only MEM
configuration and hosted services are re-evaluated after each successful
backend rebuild.

Use the explicit one-shot escape hatch when you want a stable API process, for
example while manually exercising a long-running mutation:

```bash
./dev/mem-env local up --no-watch
./dev/mem-env local restart --no-watch
```

`--watch` is also accepted explicitly, but is the default for `local up`,
`local restart`, and `switch local`. The disposable E2E local lane remains
one-shot and harness-controlled; this change does not put E2E under source
watch. `./dev/mem-env status` reports whether the harness-owned local API is
running in watch or one-shot mode.

The VS Code launch profiles point at the same shared authority. Before the first
manual debugger launch, initialize or adopt the authority with `./dev/mem-env
local token` or the explicit adoption workflow below.

Only process groups recorded by the harness are stopped. An unrelated process
occupying either port is never killed. Managed background processes are launched
with SIGINT and SIGQUIT reset to their normal dispositions before exec; Bash
otherwise marks those signals ignored for asynchronous commands when job control
is disabled. Watch-mode API shutdown can therefore use SIGINT (Ctrl+C semantics)
so `dotnet watch` exits together with its ASP.NET Core child after normal
application shutdown. One-shot local API processes retain SIGTERM; SIGKILL
remains only the bounded last-resort fallback for an owned process group that
does not terminate gracefully.

## Production-shaped container development

Build the image:

```bash
./dev/mem-env container build
```

Start it:

```bash
./dev/mem-env container up
./dev/mem-env container logs
./dev/mem-env container token
./dev/mem-env container down
```

The build uses `installer/Dockerfile` plus the canonical `migrate/` source tree
and produces:

```text
image:       mem-control-plane:local
container:   mem-control-plane-dev
HTTPS:       https://127.0.0.1:8443
state:       dev/.state/interactive/data (bind-mounted at /data)
MEM data:    dev/.state/interactive/mem-data (bind-mounted at the same absolute host path)
host data:   dev/.state/container/host-data (historical path retained as shared root)
runtime:     containerized-development
UI delivery: embedded-spa
```

The development Compose contract deliberately publishes container port 8443 on
host loopback only. Do not change it to an address-free or wildcard mapping such
as `8443:8443`: the private-administration release guardrails treat that as an
unsupported Control Plane exposure. Use a production-shaped disposable host to
prove explicit Trusted-LAN administration; ordinary container development stays
on `127.0.0.1`.

The final image contains the ASP.NET Core runtime, published API, compiled
React application, and the self-contained MEM Migrate runtime required by the
Control Plane at `/opt/mem/migrate/current`. The packaged migration payload
contains `mem-migrate`, `mem-migrate-web`, `libe_sqlite3.so`, and the compiled
Source Assistant assets. Node.js, npm and the .NET SDK remain build-stage only
and are not shipped in the final image.

The Compose build keeps `installer/` as the primary build context and supplies
`migrate/` as the named `migrate-source` build context. Docker Compose 2.17.0
or later is therefore required. `./dev/mem-env doctor` checks this explicitly.

The setup token and development certificate live in the shared interactive state
and are not placed in the image, Compose file or container environment. The
Control Plane receives that state as a bind mount at `/data`.

HostAgent durable file state (runtime manifests and new backup/restore/migration
workspaces) uses `dev/.state/interactive/mem-data`. Container development bind
mounts that directory at the same absolute host path used by local development,
so persisted paths do not change merely because the runtime mode changes. The
historical `~/mem-data` directory is mounted separately as a compatibility path
for pre-shared backup/migration records, but new runtime manifests are not read
from it.

The shared `data/` and canonical `mem-data/` trees are both Control-Plane-owned
developer state. A containerized Control Plane can create entries in either tree
with container-side ownership, so `container down` and local-source preparation
normalize both trees back to the current developer UID before local mutation.
This ownership normalization is deliberately limited to those two developer
state roots. It does not recurse into Matrix/Element/platform service host data.

Managed Matrix, Element, TURN and Seq bind paths keep using `dev/.state/container/host-data`.
That historical path is deliberately retained as the **shared** ordinary-development
resource root because existing Docker bind mounts already refer to it. Local
development now uses the same root; MEM does not copy or retarget live stack data
just to make the directory name prettier.

Because container workloads can create files under that shared root using their
own numeric UIDs (for example Synapse), local source development has an explicit
development-only POSIX ACL bootstrap:

```bash
./dev/mem-env host-data status
./dev/mem-env host-data access
```

`host-data access` must be run while both ordinary Control Plane modes are
stopped. It performs one bounded privileged ACL reconciliation using `sudo
setfacl`; it does **not** run the local API as root. Container ownership and
normal mode bits are preserved. Only the current developer UID receives the
`rwX` access needed for MEM-owned stack/configuration transactions, and
directories receive a default ACL so future container-created control files
inherit that access.

The reconciliation is deliberately limited to the shared host-data root and
managed stack control trees. Matrix `media_store` payloads are pruned from the
recursive ACL walk. Protected platform-service secret trees such as
`platform/coturn` are deliberately excluded because their existing owner-only
`0700`/`0600` contract must not be weakened by a named-user ACL. Symlinks in
the managed stack control scope cause the operation to fail closed. The harness
never uses `chmod 777` or recursively changes stack ownership merely to support
mode switching.

`local up` never attempts to grant itself privilege. It verifies the current
access contract and, when container-owned data is not writable, stops with
guidance to run `./dev/mem-env host-data access`. The successful bootstrap is
recorded under the shared interactive authority for that developer UID.

`switch local` is different because it is an explicit cross-runtime transition.
After the harness has stopped containerized development, it verifies the same
bounded host-data ACL contract. If access has drifted, the switch automatically
runs the existing `host-data access` reconciliation before starting the local
API. A valid ACL causes no privilege prompt; a required repair may prompt once
for `sudo`, and any failed or unsafe repair leaves local development stopped.

The developer host therefore requires `setfacl` (Ubuntu/Kubuntu package:
`acl`) only when shared container-owned host data needs reconciliation. This is
a developer-host requirement only; it does not change production MEM filesystem
ownership or runtime mutation semantics.

## Safe switching

```bash
./dev/mem-env switch container
./dev/mem-env switch local
```

Switching stops only the opposite Control Plane environment owned by this
harness. During `switch local`, once containerized development is stopped, the
harness also repairs the bounded developer ACL automatically when verification
shows that container-side file creation has made shared stack host-data
non-writable. Local and container development then reopen the same durable
Control Plane state. When a source runtime was reachable before the switch, the
harness compares `ControlPlaneInstanceId` after the target starts and stops the
target if identity changed unexpectedly.

`mem-seq-local` and `mem-seq-dev` remain runtime-specific service instances and
may remain running on their separate host ports. Their routing and restart
semantics remain derived from the active runtime mode even though the Control
Plane authority and ordinary Diagnostics history are shared.

Switching does not stop a production or legacy Control Plane automatically and
never treats the legacy/unscoped `mem-seq` identity as a development cleanup
target.

## Shared interactive authority, adoption and reset

Normal `down` and `restart` operations preserve state.

The ordinary developer modes now share one durable authority:

```text
dev/.state/interactive/
  authority.env
  mem-data-authority.env
  data/       # SQLite, identity, Data Protection, Diagnostics, secrets, setup token
  mem-data/   # runtime manifests and new HostAgent backup/restore/migration state

~/mem-data/
  # legacy compatibility path only; retained for persisted historical references

dev/.state/container/host-data/
  # shared Matrix/Element/TURN/Seq host resource root; historical path retained
```

Local and container development are still mutually exclusive on one Docker
host. The difference between modes is runtime behavior — process/container,
network authorities, embedded SPA versus Vite, and restart boundary — not a
second ownership database.

### One-time adoption from the previous split-state model

The harness never guesses which old state is authoritative and never merges two
SQLite databases. If `installer/data` and/or the historical
`mem-control-plane-dev-data` volume exists before shared authority has been
created, ordinary startup fails closed.

Inspect:

```bash
./dev/mem-env authority status
```

Stop both ordinary Control Plane modes, then deliberately choose one source:

```bash
./dev/mem-env authority adopt container
# or
./dev/mem-env authority adopt local
```

Container adoption copies only the old `mem-control-plane-dev-data` Control
Plane volume into the new shared `data/` authority. The harness then initializes
the shared MEM data authority. It imports only runtime manifests whose exact
`StackId` matches an active durable `RuntimeStack`. Slug-only matches are never
accepted as ownership. If an exact historical manifest no longer exists, the
developer harness reconstructs that exact manifest projection from the durable
RuntimeStack/service/route rows. Stale legacy manifests remain retired under
`~/mem-data` and do not re-enter Chat Servers inventory.
Its existing
`dev/.state/container/host-data` is **not moved or copied**: that exact path becomes
the shared ordinary-development resource root so existing Docker bind mounts stay
valid.

Local adoption copies `installer/data` and normalizes the historical development
DB/key-ring names. It is allowed only when the legacy local stack, TURN and Seq
host-data roots are empty. If live host-bound resource data exists, adoption fails
closed rather than copying files while Docker resources still refer to their old
paths. The selected legacy Control Plane source is retained as a backup and marked
retired so future starts cannot silently re-import it.

Repositories that already adopted shared Control Plane state before the MEM data
boundary was added must run, with both ordinary runtimes stopped:

```bash
./dev/mem-env authority repair-mem-data
```

The repair does not merge historical manifests blindly. It queries the shared
SQLite authority and requires exact `StackId` identity. Exact historical
manifests are copied into the canonical shared MEM data root; active stacks whose
manifest file no longer exists are reconstructed from the durable RuntimeStack,
service-instance and route rows. Slug-only matches are rejected. The full staged
manifest set must exactly equal the active durable stack IDs before it replaces
the current canonical set, and the database `DataRoot`/`ManifestPath` fields are
then retargeted to the shared MEM-data root. Historical `~/mem-data` remains
available as a compatibility mount for persisted backup/migration paths.

For a brand-new repository/host with no legacy state, the first ordinary start
creates fresh shared authority automatically.

### Destructive reset

Because local and container modes now share state, there is no longer a truthful
mode-specific reset. Use:

```bash
./dev/mem-env reset interactive \
  --confirm DELETE_MEM_DEV_STATE
```

This stops the ordinary Control Plane modes, removes both context-scoped Seq
runtimes when their labels prove ownership, and deletes the shared Control Plane
authority (including canonical `mem-data/`) plus the shared
`dev/.state/container/host-data` resource root. Historical `~/mem-data`
compatibility data is deliberately preserved by this ordinary reset.
Old `reset local` / `reset container` forms fail closed with guidance.

The legacy source selected during adoption is retained and remains retired; a
normal reset does not silently resurrect it. Use the clean-room host reset when
the entire development host must return to pre-MEM state.

## Lower-level Compose use

The wrapper is the supported interface because it prepares authority, validates
ports and checks controller conflicts. For investigation only, its lower-level
Compose file is:

```bash
docker compose \
  --env-file dev/.state/container/runtime.env \
  -f dev/compose.control-plane.yml \
  config
```

Do not run the local and containerized mutable environments concurrently on one
Docker daemon.

## Validation

```bash
bash dev/tests/mem-env.test.sh
bash bootstrap/tests/control-plane-runtime-name.test.sh

for file in dev/mem-env dev/tests/*.sh bootstrap/*.sh bootstrap/lib/*.sh bootstrap/tests/*.sh; do
  bash -n "$file"
done
```


## Dual-environment browser proof

```bash
./dev/mem-env e2e local
./dev/mem-env e2e container
./dev/mem-env e2e all
```

The E2E environments use disposable state under `dev/.state/e2e/` and never reuse the ordinary shared interactive authority. Container E2E uses `mem-control-plane-dev-e2e` with its own disposable bind-mounted data and host-data directories. Both modes run the same first-owner, named-login, Home, Diagnostics, neutral Seq, Backup Catalog and route-refresh journey, then restart the API and prove stable Control Plane identity with a rotated process identity. Ephemeral auth material is mode `0600` and is deleted with the E2E state. `e2e all` runs local then container sequentially.

## Control Plane runtime release proof

The final legacy-runtime migration and rollback rehearsal are documented in:

```text
dev/CONTROL-PLANE-RUNTIME-RELEASE.md
```

Use the bounded helper to audit source and capture only safe migration evidence:

```bash
./dev/control-plane-release-proof source-audit
./dev/control-plane-release-proof status
```

The guide deliberately proves rollback before accepting the migration from
`mem-installer` to `mem-control-plane`. The legacy `mem-installer-data` volume is
retained in place for upgraded installations.

## Clean-room development-host reset

Normal development reset is intentionally scoped to the shared interactive authority:

```bash
./dev/mem-env reset interactive --confirm DELETE_MEM_DEV_STATE
```

It does **not** mean "pretend MEM has never been installed on this Docker host".
That broader operation is intentionally separate because it removes stacks and
shared platform services as well as Control Plane state.

Use the clean-room reset when developing or validating first-install behaviour:

```bash
cd "$(git rev-parse --show-toplevel)"

./dev/mem-env reset host --dry-run
```

The dry-run inventories:

- containers carrying `io.message-easy-mode.managed=true`;
- legacy/current containers in MEM's `mem-*` development namespace;
- MEM-managed or `mem-*` Docker volumes;
- named Docker volumes mounted by the exact containers selected for clean-room reset;
- MEM-managed or `mem-*` Docker networks;
- local, container-development, and E2E Control Plane state roots.

It deliberately preserves:

- Docker images and build cache;
- unrelated Portainer and other unrelated containers;
- named volumes mounted only by unrelated containers;
- unrelated Docker volumes and networks.

A canonical permanent Control Plane (`mem-control-plane`, or the historical
permanent `mem-installer` identity) is protected by default. If a stopped
canonical Control Plane is intentionally part of a disposable development host,
review the expanded plan:

```bash
./dev/mem-env reset host \
  --dry-run \
  --include-canonical-control-plane
```

A **running** canonical Control Plane always blocks the clean-room reset. Stop it
explicitly first; the developer harness will not silently stop a permanent
runtime.

After reviewing the plan, execute with the exact acknowledgement:

```bash
./dev/mem-env reset host \
  --include-canonical-control-plane \
  --confirm DELETE_MEM_DEV_HOST_STATE
```

If no canonical Control Plane exists, omit
`--include-canonical-control-plane`.

This operation is intended for a disposable/heavy-development workstation or
test host. It is not a production uninstall command.

### Legacy Docker Compose volume identities

Historical MEM development Compose projects have used both hyphenated and
underscore-based volume names. Examples include:

```text
mem-dev_mem_postgres_data
mem_mem_npm_data
mem_postgres_data
```

`reset host` therefore recognises both bounded legacy forms:

```text
mem-*
mem_*
```

for Docker **volumes**.

When Docker Compose provenance labels are present, the clean-room reset prefers
those labels. A volume is considered MEM-owned when its
`com.docker.compose.project` value is an approved MEM development project such
as `mem`, `mem-*`, or `message-easy-mode*`.

Anonymous hexadecimal volumes are not deleted merely because they are dangling.
They are included only when explicit MEM managed or Docker Compose provenance
proves they belong to MEM.

Some MEM-managed services historically allowed Docker to create a named data
volume implicitly. Such a volume may have no MEM or Compose labels even though
the container using it is proven MEM-managed. The clean-room reset therefore also
includes **named volumes mounted by the exact container set already selected for
reset**. This is attachment-derived ownership, not a global name rule. For
example, `portainer_data` is included when it is mounted by the MEM-managed
Portainer container, while an unrelated Portainer container or similarly named
volume remains outside the reset target set. Docker still refuses volume removal
if a non-target container retains an attachment, so the reset fails closed rather
than deleting shared state.

This compatibility applies to the exceptional developer clean-room reset only;
it does not make legacy names authoritative for normal Control Plane adoption.

### Container-owned bind-mount files

Some managed services can write repository-local development data using a
container UID that is not writable by the normal host user. Seq is a common
example.

`reset host` now checks the exact path-validated development-state roots before
removing Docker resources. If privileged cleanup is required, it obtains
`sudo` authorization **before** destructive Docker work begins.

The dry-run marks affected state roots:

```text
[privileged cleanup required]
```

The reset first attempts normal user deletion. It uses `sudo rm -rf` only for
the already validated MEM development-state path if normal deletion is
insufficient.

This privilege escalation applies only to the exceptional clean-room host
reset. The ordinary `reset interactive` path remains narrowly scoped to the shared
development authority and never broadens into a host-wide cleanup.

### Why legacy names are included

Newer MEM resources increasingly carry explicit ownership labels. Older
development resources such as `mem-npm`, `mem-postgres`, existing
`mem-matrix-*` / `mem-element-*` stacks, and older test resources may predate
those labels.

For clean-room development only, `reset host` therefore supports the legacy
`mem-*` namespace as a compatibility boundary. This does **not** change normal
product ownership rules: seeing a matching container name is still not enough
for the MEM Control Plane to adopt or mutate a resource.

Future platform-service ownership work should continue moving resources toward
the canonical `io.message-easy-mode.*` labels; the clean-room command will
prefer those labels automatically.

