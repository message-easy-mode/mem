# MEM Diagnostics Operator Workspace

The Diagnostics workspace is MEM's operator-facing explanation and support surface. It is not a raw Docker administration console.

## Operator journey

1. A failed action returns a safe Problem Details response with incident and trace references.
2. **Open diagnostics** navigates to the exact recorded incident.
3. The operator reviews the safe summary, expected-versus-observed state, related operations, and authorized technical events.
4. Bounded Docker evidence can be collected only through the incident's persisted MEM resource identity.
5. A server-redacted support JSON report can be copied or downloaded for a maintainer or AI assistant.
6. The operator returns to the owning Stack, Restore, Migration, Federation, TURN, or Setup workspace.

## Role boundary

- Auditor: overview and incident summaries.
- Operator: technical events, support reports, and incident-owned Docker evidence.
- Platform Owner: Operator capabilities plus owner-only logging-health facts such as the configured recorder path and Seq authority.

The browser renders only capabilities returned by the server. Hidden controls are not authorization.

## Evidence safety

The browser never requests:

- an arbitrary container ID or name;
- an arbitrary host path;
- raw CLEF files;
- raw runtime-operation JSON;
- credentials, connection strings, private keys, or unrestricted environment data.

Docker evidence is bounded, sanitized, and optional. Container output may still contain user identifiers, room identifiers, IP addresses, or other sensitive operational metadata.

## API outage

When Diagnostics requests cannot reach the API, the workspace shows the emergency fallback:

```bash
sudo docker logs --tail 500 mem-control-plane
```

Operators can also inspect the canonical `mem-control-plane` container in Portainer and the persistent recorder at the configured CLEF location. An installed SPA cannot guarantee availability during a total Kestrel outage, so these external paths are intentional.

## Release proof

The development Playwright proof creates a synthetic incident through a Development-only, explicit, Platform-Owner fixture endpoint. The route is absent in Production. The deployed proof is read-only.

See `tests/e2e/README.md` for exact commands and cleanup requirements.

## Portainer handoff

`/diagnostics/portainer` is Platform Owner-only. It consumes a server-authored
Portainer overview and never derives URLs from the browser origin, published
ports, or `localhost`.

Contextual actions open MEM redirect endpoints rather than putting Docker
container IDs into React state or API request bodies. The server resolves the
current container from a logical incident, stack, migration staging resource,
or Seq runtime. Exact detail links may fall back to the configured containers
list. Portainer authentication and all low-level administration remain owned by
Portainer.

## Command-centre capability rules

The `/diagnostics` landing page consumes one server-owned overview. Cards render only server-projected capabilities and must not infer authority from the current route, React Query errors, browser storage, or raw Docker responses.

A capability card must provide a supported route or action. Do not add visible "Coming later" cards to the primary grid. Keep optional Seq and Portainer states neutral unless they are configured or expected and failing.

Each landing-page section is contained by `DiagnosticsSectionErrorBoundary`; one card or translation failure must not remove the route header, health strip, outage fallback, or other capabilities.

## Safe attention items

The header bell polls the bounded attention endpoint only while the document is visible. Attention items contain a safe summary, severity, feature, timestamp, incident identity, and canonical incident href. They do not contain exception text, raw paths, Docker identities, credentials, or browser-authored links.

The first alert surface is intentionally not an inbox. Do not add local read/unread state, dismissal, assignment, acknowledgement, or push notifications without a separate durable server contract.

## Seq control boundary

Seq runtime and event delivery are independent. Delivery preference changes may require an API restart; the browser must show current and desired state separately. Deploy, delivery changes, and removal use Platform Owner authority and step-up. Remove preserves data and is blocked while delivery remains enabled.

The browser never submits an image reference, host path, port, container identity, password hash, or API key. Setup review is presence-only and ordinary lifecycle operations never pull an image.

## Portainer runtime and handoff boundary

New MEM-managed Portainer installations use the exact approved 2.39.5 image, resolve it to a local immutable image identity, retain `portainer_data`, publish private HTTPS port 9443, omit Edge port 8000, and create no public NPM route.

Contextual browser actions call MEM redirect endpoints using logical incident or resource identity. The server resolves the current container at request time. Exact detail links may fall back to the configured containers list. Do not put raw Docker container IDs into React state, URLs, request bodies, or ordinary projections.

## Release guardrails

The release proof includes:

- focused backend authorization, no-store, redaction, fixture, Seq, Portainer, and Docker-evidence tests;
- focused Web command-centre, localization, attention, Seq, Portainer, incident, and source-guard tests;
- a Development-only deterministic browser proof;
- a deployed read-only browser proof that confirms the fixture route is absent;
- manual restart-persistence and outage proof;
- fresh-server Seq and Portainer runtime proof deferred until a disposable host is available.

Production must never map the deterministic fixture endpoint. Diagnostics browser code must never call `/internal/host-agent/*` directly.

## Rollback boundaries

- Landing page: restore the earlier Web page while retaining Diagnostics APIs.
- Attention bell: hide the bell; incident data remains.
- Seq workspace: hide management UI or disable delivery; MEM-native Diagnostics remains.
- Portainer handoff: hide links; Portainer remains independently accessible.
- Portainer runtime: use the documented previous approved immutable identity only after checking data compatibility; preserve `portainer_data`.
- Pipeline self-test: disable its capability while leaving recorder and safe event storage active.
