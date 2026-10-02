import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { fireEvent, screen, waitFor, within } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { ServicesPage } from "./services-page"

const postgresEndpoint = "/api/operator/services/postgres/"
const npmEndpoint = "/api/operator/services/npm/"
const seqEndpoint = "/api/operator/services/seq/"
const stagingEndpoint = "/api/operator/services/temporary-staging"
const containersEndpoint = "/api/operator/services/docker/containers"
const coturnEndpoint = "/internal/host-agent/platform/coturn"
const latestCheckEndpoint = `${coturnEndpoint}/check/latest`
const diagnosticsSeqEndpoint = "/api/operator/diagnostics/seq"
const diagnosticsPortainerEndpoint = "/api/operator/diagnostics/portainer"

const noLatestCheckResponse = {
  source: "control-plane",
  freshness: "not-checked",
  fresh: false,
  freshForSeconds: 1800,
  observedAtUtc: "2026-08-22T10:00:00Z",
  checkedAtUtc: null,
  freshUntilUtc: null,
  incidentId: null,
  result: null,
  warnings: [],
  detail: "No persisted Coturn functional check is available yet.",
}

const freshPassedCheck = {
  source: "control-plane",
  status: "passed",
  checkedAtUtc: "2026-08-22T09:55:00Z",
  freshUntilUtc: "2026-08-22T10:25:00Z",
  containerState: "running",
  readiness: "ready",
  publicHost: "turn.deltabox.dev",
  runtimeContainerId: "coturn-1",
  runtimeStartedAtUtc: "2026-08-22T05:00:00Z",
  runtimeRestartCount: 0,
  checks: [],
  allocation: {
    status: "passed",
    transport: "udp",
    summary: "passed",
    logTail: null,
  },
  warnings: [],
  detail: "passed",
  evidencePersisted: true,
  incidentId: null,
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter>
      <ServicesPage />
    </MemoryRouter>,
  )
}

async function getServiceRow(displayName: string) {
  const name = await screen.findByText(displayName)
  const row = name.closest("[data-service-name]")

  expect(row).not.toBeNull()
  return row as HTMLElement
}

function registerServicesHandlers({ postgresRunning = true }: { postgresRunning?: boolean } = {}) {
  server.use(
    http.get(stagingEndpoint, () => HttpResponse.json({
      source: "control-plane", observedAtUtc: "2026-09-24T03:00:00Z",
      status: "complete", warningCodes: [], items: [],
    })),
    http.get(postgresEndpoint, () =>
      HttpResponse.json({
        serviceName: "postgres",
        containerName: "mem-postgres",
        preferredHostPort: null,
        selectedHostPort: null,
        hostDataPath: "/srv/mem/postgres",
        exists: true,
        running: postgresRunning,
        state: postgresRunning ? "Running" : "Stopped",
        container: {
          id: "postgres-1",
          name: "mem-postgres",
          image: "postgres:16",
          state: postgresRunning ? "running" : "exited",
          running: postgresRunning,
          ports: [],
        },
        warnings: [],
      }),
    ),
    http.get(npmEndpoint, () =>
      HttpResponse.json({
        serviceName: "npm",
        containerName: "mem-npm",
        hostDataPath: "/srv/mem/npm/data",
        hostLetsEncryptPath: "/srv/mem/npm/letsencrypt",
        httpHostPort: 80,
        adminHostPort: 81,
        httpsHostPort: 443,
        exists: true,
        running: true,
        state: "Running",
        container: {
          id: "npm-1",
          name: "mem-npm",
          image: "jc21/nginx-proxy-manager:2.14.0",
          state: "running",
          running: true,
          ports: [],
        },
        warnings: [],
      }),
    ),
    http.get(seqEndpoint, () =>
      HttpResponse.json({
        serviceName: "seq",
        containerName: "mem-seq",
        hostDataPath: null,
        uiHostPort: null,
        exists: false,
        running: false,
        state: "Not deployed",
        container: null,
        warnings: [],
      }),
    ),
    http.get(diagnosticsSeqEndpoint, () =>
      HttpResponse.json({
        ui: { available: false, url: null },
        capabilities: { canOpenUi: false },
      }),
    ),
    http.get(diagnosticsPortainerEndpoint, () =>
      HttpResponse.json({
        available: false,
        links: { home: null },
        capabilities: { canOpenHome: false },
      }),
    ),
    http.get(containersEndpoint, () =>
      HttpResponse.json({
        containers: [
          {
            id: "api-1",
            name: "mem-api",
            image: "mem-api:dev",
            state: "running",
            status: "Up 1 minute",
            ports: [{ privatePort: 7000, publicPort: 7000, type: "tcp", ip: "0.0.0.0" }],
          },
          {
            id: "web-1",
            name: "mem-web",
            image: "mem-web:dev",
            state: "running",
            status: "Up 1 minute",
            ports: [{ privatePort: 3000, publicPort: 3000, type: "tcp", ip: "0.0.0.0" }],
          },
        ],
      }),
    ),
    http.get(coturnEndpoint, () =>
      HttpResponse.json({
        source: "control-plane",
        status: "ok",
        containerState: "running",
        readiness: "ready",
        serviceKey: "coturn",
        containerName: "mem-coturn",
        image: "sha256:approved-image",
        approvedImageReference: "coturn/coturn@sha256:approved-image",
        resolvedImageId: "sha256:approved-image",
        imageApproved: true,
        containerExists: true,
        running: true,
        ownershipVerified: true,
        containerId: "coturn-1",
        dockerState: "running",
        operatorStatus: "runtime-ready",
        runtimeExact: true,
        dockerRuntime: {
          restartPolicy: "unless-stopped",
          expectedRestartPolicy: "unless-stopped",
          restartPolicyMatches: true,
          restartCount: 0,
          restarting: false,
          paused: false,
          exitCode: 0,
          oomKilled: false,
          dead: false,
          stateErrorPresent: false,
          healthStatus: null,
          startedAtUtc: "2026-08-22T05:00:00Z",
          finishedAtUtc: null,
          networkMode: "default",
          expectedNetwork: "mem-gateway",
          networkModeMatches: false,
          attachedNetworks: ["mem-gateway"],
          expectedNetworkAliases: ["coturn", "mem-coturn"],
          observedExpectedNetworkAliases: ["coturn", "mem-coturn"],
          expectedNetworkAttached: true,
          networkAliasesMatch: true,
          configMountPresent: true,
          configMountReadOnly: true,
          configMountSourceMatches: true,
          configMountDestinationMatches: true,
          commandMatches: true,
          startupUserMatches: true,
        },
        runtimeDrift: [],
        protectedEvidenceAccess: "available",
        realm: "deltabox.dev",
        publicHost: "turn.deltabox.dev",
        turnPort: 3478,
        relayMinPort: 49160,
        relayMaxPort: 49200,
        turnUris: ["turn:turn.deltabox.dev:3478?transport=udp"],
        secretPresent: true,
        secretSource: "protected-file",
        secretStorage: "protected-host-file",
        secretFilePermissionsApplied: true,
        expectedBaseDomain: "deltabox.dev",
        configuredBaseDomain: "deltabox.dev",
        domainDriftDetected: false,
        recreated: false,
        externalIp: null,
        relayPortsPublished: true,
        securityPolicyApplied: true,
        securityPolicyVersion: "mem-coturn-v1",
        publishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
        requiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
        warnings: [],
        detail: null,
      }),
    ),
    http.get(latestCheckEndpoint, () =>
      HttpResponse.json(noLatestCheckResponse),
    ),
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("ServicesPage", () => {
  it("renders the cleaned German service inventory without generic bulk management", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerServicesHandlers()

    renderPage()

    expect(await screen.findByRole("heading", { name: "Dienste" })).toBeInTheDocument()
    expect(
      screen.getByText(
        "Gemeinsam genutzte Plattformdienste, optionale Werkzeuge und Dienste Ihrer Chat-Server.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: /Alle/ })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: /Plattform/ })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: /Hilfswerkzeuge/ })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Kernplattform" })).toBeInTheDocument()
    expect(screen.queryByText("MEM API")).not.toBeInTheDocument()
    expect(screen.queryByText("MEM Web")).not.toBeInTheDocument()
    expect(screen.queryByText("PgAdmin")).not.toBeInTheDocument()
    expect(screen.queryByText("Änderungen anwenden")).not.toBeInTheDocument()
    expect(screen.queryByText("Änderungen prüfen")).not.toBeInTheDocument()
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
    expect((await screen.findAllByText("Wird ausgeführt")).length).toBeGreaterThan(0)
    expect(screen.getByText("Prüfung empfohlen")).toBeInTheDocument()
    expect(
      screen.queryByText("Laufzeit bereit · Funktionale Prüfung: Prüfung empfohlen"),
    ).not.toBeInTheDocument()

    const postgresRow = await getServiceRow("Postgres")
    expect(within(postgresRow).getByRole("link", { name: "Postgres" })).toHaveAttribute(
      "href",
      "/services/postgres",
    )
    expect(within(postgresRow).queryByRole("link", { name: "Öffnen" })).not.toBeInTheDocument()

    const npmRow = await getServiceRow("Nginx Proxy Manager")
    expect(
      within(npmRow).getByRole("link", { name: "Nginx Proxy Manager" }),
    ).toHaveAttribute("href", "/services/nginx-proxy-manager")

    const coturnRow = await getServiceRow("Coturn-TURN-Server")
    expect(
      within(coturnRow).getByRole("link", { name: "Coturn-TURN-Server" }),
    ).toHaveAttribute("href", "/services/coturn")

    const seqRow = await getServiceRow("Seq")
    expect(within(seqRow).getByRole("link", { name: "Seq" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )

    expect(screen.queryByRole("button", { name: "Erweitern" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Details" })).not.toBeInTheDocument()
  })


  it("refreshes the aggregate inventory on focus and on explicit operator refresh", async () => {
    registerServicesHandlers()

    let includeDemoStack = true
    server.use(
      http.get(containersEndpoint, () =>
        HttpResponse.json({
          containers: includeDemoStack
            ? [
                {
                  id: "matrix-demo",
                  name: "mem-matrix-demo",
                  image: "matrixdotorg/synapse:latest",
                  state: "running",
                  status: "Up 1 minute",
                  ports: [],
                },
                {
                  id: "element-demo",
                  name: "mem-element-demo",
                  image: "vectorim/element-web:latest",
                  state: "running",
                  status: "Up 1 minute",
                  ports: [],
                },
              ]
            : [],
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("demo")).toBeInTheDocument()

    includeDemoStack = false
    fireEvent.focus(window)

    await waitFor(() => {
      expect(screen.queryByText("demo")).not.toBeInTheDocument()
    })

    includeDemoStack = true
    const refreshButton = screen.getByRole("button", { name: "Refresh" })
    await waitFor(() => expect(refreshButton).toBeEnabled())
    fireEvent.click(refreshButton)

    expect(await screen.findByText("demo")).toBeInTheDocument()
  })

  it("only exposes an external Nginx Proxy Manager Open action while the service is running", async () => {
    registerServicesHandlers()
    server.use(
      http.get(npmEndpoint, () =>
        HttpResponse.json({
          serviceName: "npm",
          containerName: "mem-npm",
          hostDataPath: "/srv/mem/npm/data",
          hostLetsEncryptPath: "/srv/mem/npm/letsencrypt",
          httpHostPort: 80,
          adminHostPort: 81,
          httpsHostPort: 443,
          exists: true,
          running: false,
          state: "Stopped",
          container: {
            id: "npm-1",
            name: "mem-npm",
            image: "jc21/nginx-proxy-manager:2.14.0",
            state: "exited",
            running: false,
            ports: [],
          },
          warnings: [],
        }),
      ),
    )

    renderPage()

    const npmRow = await getServiceRow("Nginx Proxy Manager")
    expect(await within(npmRow).findByText("Stopped")).toBeInTheDocument()
    expect(within(npmRow).queryByRole("link", { name: "Open" })).not.toBeInTheDocument()
  })

  it("presents restricted Coturn protected evidence as verification limited rather than repair required", async () => {
    registerServicesHandlers()
    server.use(
      http.get(coturnEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "protected_evidence_restricted",
          containerState: "running",
          readiness: "verification-limited",
          operatorStatus: "verification-limited",
          runtimeExact: true,
          runtimeDrift: [],
          protectedEvidenceAccess: "restricted",
          serviceKey: "coturn",
          containerName: "mem-coturn",
          image: "sha256:approved-image",
          approvedImageReference: "coturn/coturn@sha256:approved-image",
          resolvedImageId: "sha256:approved-image",
          imageApproved: true,
          containerExists: true,
          running: true,
          ownershipVerified: true,
          containerId: "coturn-1",
          dockerState: "running",
          dockerRuntime: {
            restartPolicy: "unless-stopped",
            expectedRestartPolicy: "unless-stopped",
            restartPolicyMatches: true,
            restartCount: 0,
            restarting: false,
            paused: false,
            exitCode: 0,
            oomKilled: false,
            dead: false,
            stateErrorPresent: false,
            healthStatus: null,
            startedAtUtc: "2026-08-22T05:00:00Z",
            finishedAtUtc: null,
            networkMode: "default",
            expectedNetwork: "mem-gateway",
            networkModeMatches: false,
            attachedNetworks: ["mem-gateway"],
            expectedNetworkAliases: ["coturn", "mem-coturn"],
            observedExpectedNetworkAliases: ["coturn", "mem-coturn"],
            expectedNetworkAttached: true,
            networkAliasesMatch: true,
            configMountPresent: true,
            configMountReadOnly: true,
            configMountSourceMatches: true,
            configMountDestinationMatches: true,
            commandMatches: true,
            startupUserMatches: true,
          },
          realm: "deltabox.dev",
          publicHost: "turn.deltabox.dev",
          turnPort: 3478,
          relayMinPort: 49160,
          relayMaxPort: 49200,
          turnUris: ["turn:turn.deltabox.dev:3478?transport=udp"],
          secretPresent: false,
          secretSource: "restricted",
          secretStorage: "protected-host-file",
          secretFilePermissionsApplied: false,
          expectedBaseDomain: "deltabox.dev",
          configuredBaseDomain: "deltabox.dev",
          domainDriftDetected: false,
          recreated: false,
          externalIp: null,
          relayPortsPublished: true,
          securityPolicyApplied: false,
          securityPolicyVersion: "mem-coturn-v1",
          publishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
          requiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
          warnings: [],
          detail: "protected evidence restricted",
        }),
      ),
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "fresh",
          fresh: true,
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          result: freshPassedCheck,
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Verification limited")).toBeInTheDocument()
    expect(screen.queryByText("Repair required")).not.toBeInTheDocument()
  })

  it("keeps a healthy exact Coturn runtime green and labels the service as running", async () => {
    registerServicesHandlers()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "fresh",
          fresh: true,
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          result: freshPassedCheck,
        }),
      ),
    )

    renderPage()

    const coturnRow = await getServiceRow("Coturn TURN server")
    expect(await within(coturnRow).findByText("Running")).toBeInTheDocument()
    expect(
      within(coturnRow).queryByText("Runtime ready · Functional check: Healthy"),
    ).not.toBeInTheDocument()

  })

  it("invalidates a previously passing check when the Coturn runtime changed", async () => {
    registerServicesHandlers()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "runtime-changed",
          fresh: false,
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          result: freshPassedCheck,
          detail:
            "The Coturn runtime changed after the latest functional check. Run Check now before relying on that evidence.",
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Check required")).toBeInTheDocument()
    expect(screen.queryByText("Healthy")).not.toBeInTheDocument()

  })

  it("marks an old passing check overdue instead of presenting stale evidence as healthy", async () => {
    registerServicesHandlers()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "stale",
          fresh: false,
          checkedAtUtc: "2026-08-22T08:00:00Z",
          freshUntilUtc: "2026-08-22T08:30:00Z",
          result: {
            ...freshPassedCheck,
            checkedAtUtc: "2026-08-22T08:00:00Z",
            freshUntilUtc: "2026-08-22T08:30:00Z",
          },
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Check overdue")).toBeInTheDocument()
    expect(screen.queryByText("Healthy")).not.toBeInTheDocument()

  })

  it("surfaces a fresh failed functional check as needs attention", async () => {
    registerServicesHandlers()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "fresh",
          fresh: true,
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          incidentId: "inc_coturn_failure",
          result: {
            ...freshPassedCheck,
            status: "failed",
            incidentId: "inc_coturn_failure",
            allocation: {
              status: "failed",
              transport: "udp",
              summary: "failed",
              logTail: null,
            },
          },
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Needs attention")).toBeInTheDocument()

  })

  it("does not present a running Coturn container as ready when exact runtime drift exists", async () => {
    registerServicesHandlers()
    server.use(
      http.get(coturnEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "runtime_drift",
          containerState: "running",
          readiness: "repair-required",
          operatorStatus: "repair-required",
          runtimeExact: false,
          runtimeDrift: ["restart-policy"],
          protectedEvidenceAccess: "available",
          serviceKey: "coturn",
          containerName: "mem-coturn",
          image: "sha256:approved-image",
          approvedImageReference: "coturn/coturn@sha256:approved-image",
          resolvedImageId: "sha256:approved-image",
          imageApproved: true,
          containerExists: true,
          running: true,
          ownershipVerified: true,
          containerId: "coturn-1",
          dockerState: "running",
          dockerRuntime: {
            restartPolicy: "no",
            expectedRestartPolicy: "unless-stopped",
            restartPolicyMatches: false,
            restartCount: 0,
            restarting: false,
            paused: false,
            exitCode: 0,
            oomKilled: false,
            dead: false,
            stateErrorPresent: false,
            healthStatus: null,
            startedAtUtc: "2026-08-22T05:00:00Z",
            finishedAtUtc: null,
            networkMode: "default",
            expectedNetwork: "mem-gateway",
            networkModeMatches: false,
            attachedNetworks: ["mem-gateway"],
            expectedNetworkAliases: ["coturn", "mem-coturn"],
            observedExpectedNetworkAliases: ["coturn", "mem-coturn"],
            expectedNetworkAttached: true,
            networkAliasesMatch: true,
            configMountPresent: true,
            configMountReadOnly: true,
            configMountSourceMatches: true,
            configMountDestinationMatches: true,
            commandMatches: true,
            startupUserMatches: true,
          },
          realm: "deltabox.dev",
          publicHost: "turn.deltabox.dev",
          turnPort: 3478,
          relayMinPort: 49160,
          relayMaxPort: 49200,
          turnUris: ["turn:turn.deltabox.dev:3478?transport=udp"],
          secretPresent: true,
          secretSource: "protected-file",
          secretStorage: "protected-host-file",
          secretFilePermissionsApplied: true,
          expectedBaseDomain: "deltabox.dev",
          configuredBaseDomain: "deltabox.dev",
          domainDriftDetected: false,
          recreated: false,
          externalIp: null,
          relayPortsPublished: true,
          securityPolicyApplied: true,
          securityPolicyVersion: "mem-coturn-v1",
          publishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
          requiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
          warnings: ["Coturn restart policy is 'no', but MEM requires 'unless-stopped'."],
          detail: "runtime drift",
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Repair required")).toBeInTheDocument()
    expect(screen.queryByText("Runtime ready")).not.toBeInTheDocument()
  })

  it("uses service-specific navigation with a compact port-free aligned status inventory", async () => {
    registerServicesHandlers()
    server.use(
      http.get(containersEndpoint, () =>
        HttpResponse.json({
          containers: [
            {
              id: "portainer-1",
              name: "portainer",
              image: "portainer/portainer-ce:2.39.5",
              state: "running",
              status: "Up 1 minute",
              ports: [
                {
                  privatePort: 9443,
                  publicPort: 9443,
                  type: "tcp",
                  ip: "127.0.0.1",
                },
              ],
            },
            {
              id: "matrix-1",
              name: "mem-demo-synapse",
              image: "matrixdotorg/synapse:latest",
              state: "running",
              status: "Up 1 minute",
              ports: [
                {
                  privatePort: 8008,
                  publicPort: 18008,
                  type: "tcp",
                  ip: "127.0.0.1",
                },
              ],
            },
            {
              id: "element-1",
              name: "mem-demo-element",
              image: "vectorim/element-web:latest",
              state: "running",
              status: "Up 1 minute",
              ports: [
                {
                  privatePort: 80,
                  publicPort: 18080,
                  type: "tcp",
                  ip: "127.0.0.1",
                },
              ],
            },
          ],
        }),
      ),
      http.get(seqEndpoint, () =>
        HttpResponse.json({
          serviceName: "seq",
          containerName: "mem-seq",
          hostDataPath: "/srv/mem/seq",
          uiHostPort: 17341,
          exists: true,
          running: true,
          state: "Running",
          container: {
            id: "seq-1",
            name: "mem-seq",
            image: "datalust/seq:latest",
            state: "running",
            running: true,
            ports: [],
          },
          warnings: [],
        }),
      ),
      http.get(diagnosticsSeqEndpoint, () =>
        HttpResponse.json({
          ui: {
            available: true,
            url: "http://127.0.0.1:17341",
          },
          capabilities: { canOpenUi: true },
        }),
      ),
      http.get(diagnosticsPortainerEndpoint, () =>
        HttpResponse.json({
          available: true,
          links: { home: "https://127.0.0.1:9443" },
          capabilities: { canOpenHome: true },
        }),
      ),
    )

    renderPage()

    const postgresRow = await getServiceRow("Postgres")
    expect(within(postgresRow).getByRole("link", { name: "Postgres" })).toHaveAttribute(
      "href",
      "/services/postgres",
    )
    expect(within(postgresRow).queryByRole("link", { name: "Open" })).not.toBeInTheDocument()

    const npmRow = await getServiceRow("Nginx Proxy Manager")
    expect(
      within(npmRow).getByRole("link", { name: "Nginx Proxy Manager" }),
    ).toHaveAttribute("href", "/services/nginx-proxy-manager")
    expect(await within(npmRow).findByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "http://localhost:81",
    )

    const coturnRow = await getServiceRow("Coturn TURN server")
    expect(
      within(coturnRow).getByRole("link", { name: "Coturn TURN server" }),
    ).toHaveAttribute("href", "/services/coturn")
    expect(within(coturnRow).queryByRole("link", { name: "Open" })).not.toBeInTheDocument()

    const seqRow = await getServiceRow("Seq")
    expect(within(seqRow).getByRole("link", { name: "Seq" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(await within(seqRow).findByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "http://127.0.0.1:17341/",
    )

    const portainerRow = await getServiceRow("Portainer")
    expect(within(portainerRow).getByRole("link", { name: "Portainer" })).toHaveAttribute(
      "href",
      "/diagnostics/portainer",
    )
    expect(await within(portainerRow).findByRole("link", { name: "Open" })).toHaveAttribute(
      "href",
      "https://127.0.0.1:9443/",
    )

    const stackRow = await getServiceRow("demo")
    expect(within(stackRow).getByRole("link", { name: "demo" })).toHaveAttribute(
      "href",
      "/stacks/demo",
    )
    expect(within(stackRow).queryByRole("link", { name: "Open" })).not.toBeInTheDocument()

    expect(screen.queryByText(/Ports:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/3478\/tcp/)).not.toBeInTheDocument()
    expect(screen.queryByText(/18008/)).not.toBeInTheDocument()

    for (const row of [postgresRow, npmRow, coturnRow, seqRow, portainerRow, stackRow]) {
      const stateActions = row.querySelector("[data-service-state-actions]")
      const statusColumn = row.querySelector("[data-service-status-column]")
      const actionColumn = row.querySelector("[data-service-action-column]")
      expect(stateActions).not.toBeNull()
      expect(stateActions).toHaveClass("justify-end", "md:justify-self-end")
      expect(statusColumn).not.toBeNull()
      expect(statusColumn).toHaveClass("shrink-0")
      expect(actionColumn).not.toBeNull()
      expect(actionColumn).toHaveClass("shrink-0", "justify-end")
      expect(actionColumn).not.toHaveClass("w-24")
      expect(stateActions?.firstElementChild).toBe(actionColumn)
      expect(stateActions?.lastElementChild).toBe(statusColumn)
    }

    expect(screen.queryByRole("button", { name: "Expand" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Details" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "View chat server" })).not.toBeInTheDocument()
    expect(screen.queryByText("PgAdmin")).not.toBeInTheDocument()
  })
  it("keeps temporary staging workflow-neutral and out of managed stack navigation", async () => {
    registerServicesHandlers()
    let includeStaging = true

    server.use(
      http.get(containersEndpoint, () =>
        HttpResponse.json({
          containers: [
            {
              id: "matrix-live",
              name: "mem-demo-synapse",
              image: "matrixdotorg/synapse:1.156.0",
              state: "running",
              status: "Up 1 minute",
              ports: [],
            },
            {
              id: "element-live",
              name: "mem-demo-element",
              image: "vectorim/element-web:v1.12.23",
              state: "running",
              status: "Up 1 minute",
              ports: [],
            },
            ...(includeStaging
              ? [
                  {
                    id: "matrix-staging",
                    name: "mem-restore-staging-synapse-20260917-abcd1234",
                    image: "matrixdotorg/synapse:1.156.0",
                    state: "running",
                    status: "Up 1 minute",
                    ports: [],
                  },
                  {
                    id: "element-staging",
                    name: "mem-restore-staging-element-20260917-abcd1234",
                    image: "vectorim/element-web:v1.12.23",
                    state: "running",
                    status: "Up 1 minute",
                    ports: [],
                  },
                ]
              : []),
          ],
        }),
      ),
    )

    renderPage()

    const stagingRow = await getServiceRow("Temporary staging")
    expect(
      within(stagingRow).queryByText(
        "Temporary private restore runtime. Open the owning workspace from Restores.",
      ),
    ).not.toBeInTheDocument()
    expect(
      within(stagingRow).queryByRole("link", { name: "Temporary staging" }),
    ).not.toBeInTheDocument()
    expect(stagingRow.querySelector('a[href^="/stacks/restore-staging-"]')).toBeNull()

    const managedStackRow = await getServiceRow("demo")
    expect(within(managedStackRow).getByRole("link", { name: "demo" })).toHaveAttribute(
      "href",
      "/stacks/demo",
    )

    includeStaging = false
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }))

    await waitFor(() => {
      expect(screen.queryByText("Temporary staging")).not.toBeInTheDocument()
    })
    expect(within(await getServiceRow("demo")).getByRole("link", { name: "demo" })).toHaveAttribute(
      "href",
      "/stacks/demo",
    )
  })

  it("shows a matched migration owner without duplicate name-inferred staging rows", async () => {
    registerServicesHandlers()
    server.use(
      http.get(stagingEndpoint, () => HttpResponse.json(stagingResponse())),
      http.get(containersEndpoint, () => HttpResponse.json({ containers: [
        { id: "pg-stage", name: "mem-restore-staging-postgres-stage-one", image: "postgres:16", state: "running", status: "Up", ports: [] },
        { id: "syn-stage", name: "mem-restore-staging-synapse-stage-one", image: "synapse:approved", state: "running", status: "Up", ports: [] },
      ] })),
    )
    renderPage()
    const link = await screen.findByRole("link", { name: "Open migration" })
    expect(link).toHaveAttribute("href", "/migrations/mig_one")
    expect(screen.getAllByText("Temporary staging")).toHaveLength(1)
    const row = await getServiceRow("Temporary staging")
    expect(within(row).getByText(/Migration · Example/)).toBeInTheDocument()
    expect(within(row).queryByRole("button", { name: /retire|delete|destroy/i })).not.toBeInTheDocument()
    expect(row.querySelector('a[href^="/stacks/"]')).toBeNull()
  })

  it("opens the shared retirement review from Services without issuing a mutation", async () => {
    registerServicesHandlers()
    const response = stagingResponse()
    const item = { ...response.items[0], retirementReview: { migrationId: "mig_one", stagingRunId: "mst_one", status: null } }
    const endpoint = "/api/operator/migrations/sessions/mig_one/staging-runs/mst_one/retirement"
    let posts = 0
    server.use(
      http.get(stagingEndpoint, () => HttpResponse.json({ ...response, items: [item] })),
      http.get(endpoint, () => HttpResponse.json({ migrationId: "mig_one", stagingRunId: "mst_one",
        displayName: "Example", canRetire: true, blockerCode: null, reviewFingerprint: "review-a",
        containerCount: 2, networkCount: 1, workspacePresent: true, operation: null })),
      http.post(endpoint, () => { posts++; return HttpResponse.json({}, { status: 500 }) }),
    )
    renderPage()
    fireEvent.click(await screen.findByRole("button", { name: "Review retirement" }))
    const dialog = await screen.findByRole("alertdialog", { name: "Retire migration staging" })
    await within(dialog).findByRole("checkbox")
    expect(within(dialog).getByRole("button", { name: "Retire staging" })).toBeDisabled()
    expect(within(dialog).getByRole("link", { name: "Open migration" })).toHaveAttribute("href", "/migrations/mig_one")
    expect(posts).toBe(0)
  })

  it("withholds the retirement review entry point after an inventory refresh failure", async () => {
    registerServicesHandlers()
    const response = stagingResponse()
    let unavailable = false
    server.use(http.get(stagingEndpoint, () => unavailable ? HttpResponse.json({}, { status: 503 })
      : HttpResponse.json({ ...response, items: [{ ...response.items[0],
        retirementReview: { migrationId: "mig_one", stagingRunId: "mst_one", status: null } }] })))
    renderPage()
    await screen.findByRole("button", { name: "Review retirement" })
    unavailable = true
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }))
    await screen.findByText(/Temporary staging inventory could not be refreshed/)
    expect(screen.queryByRole("button", { name: "Review retirement" })).not.toBeInTheDocument()
  })

  it("shows a restore owner and keeps network-only leftovers visible", async () => {
    registerServicesHandlers()
    const response = stagingResponse()
    response.items[0] = { ...response.items[0],
      owner: { kind: "restore", id: "rs_one", displayName: "Example restore", workspaceHref: "/restores/rs_one" },
      runtimeStatus: "network-only", containerCount: 0, runningContainerCount: 0, containerIds: [],
    }
    server.use(http.get(stagingEndpoint, () => HttpResponse.json(response)))
    renderPage()
    expect(await screen.findByRole("link", { name: "Open restore" })).toHaveAttribute("href", "/restores/rs_one")
    expect(within(await getServiceRow("Temporary staging")).getByText("Network remains")).toBeInTheDocument()
  })

  it("never enables a workspace or deletion action for unresolved ownership", async () => {
    registerServicesHandlers()
    const response = stagingResponse()
    response.items[0] = { ...response.items[0], ownershipStatus: "unresolved", owner: null }
    server.use(http.get(stagingEndpoint, () => HttpResponse.json(response)))
    renderPage()
    expect(await screen.findByText(/Ownership unresolved/)).toBeInTheDocument()
    const row = await getServiceRow("Temporary staging")
    expect(within(row).queryByRole("link")).not.toBeInTheDocument()
    expect(within(row).queryByRole("button")).not.toBeInTheDocument()
  })

  it("retains stale inventory after failed refresh but removes its workspace link", async () => {
    registerServicesHandlers()
    let unavailable = false
    server.use(http.get(stagingEndpoint, () => unavailable
      ? HttpResponse.json({ detail: "/private/never-render-host-error" }, { status: 503 })
      : HttpResponse.json(stagingResponse())))
    renderPage()
    await screen.findByRole("link", { name: "Open migration" })
    unavailable = true
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }))
    await screen.findByText(/Temporary staging inventory could not be refreshed/)
    expect(screen.getByText("Temporary staging")).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open migration" })).not.toBeInTheDocument()
    expect(within(await getServiceRow("Temporary staging")).getByText("Runtime unavailable")).toBeInTheDocument()
    expect(screen.queryByText(/never-render-host-error/)).not.toBeInTheDocument()
  })

  it("shows a bounded incomplete-inventory notice without raw error details", async () => {
    registerServicesHandlers()
    server.use(http.get(stagingEndpoint, () => HttpResponse.json({ ...stagingResponse(), status: "partial", warningCodes: ["history-unavailable"] })))
    renderPage()
    expect(await screen.findByText(/Temporary staging inventory is incomplete/)).toBeInTheDocument()
  })

  it("keeps a name-only PostgreSQL leftover visible when ownership inventory is unavailable", async () => {
    registerServicesHandlers()
    server.use(
      http.get(stagingEndpoint, () => new HttpResponse(null, { status: 503 })),
      http.get(containersEndpoint, () => HttpResponse.json({ containers: [
        { id: "pg-only", name: "mem-restore-staging-postgres-stage-orphan", image: "postgres:16", state: "running", status: "Up", ports: [] },
      ] })),
    )
    renderPage()
    const row = await getServiceRow("Temporary staging")
    expect(within(row).getByText("Ownership unresolved")).toBeInTheDocument()
    expect(within(row).queryByRole("link")).not.toBeInTheDocument()
    expect(row.querySelector('a[href^="/stacks/"]')).toBeNull()
  })

  it("localizes the staging owner and workspace action in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerServicesHandlers()
    server.use(http.get(stagingEndpoint, () => HttpResponse.json(stagingResponse())))
    renderPage()
    expect(await screen.findByRole("link", { name: "Migration öffnen" })).toHaveAttribute("href", "/migrations/mig_one")
    expect(screen.getByText(/Container: 2 · Netzwerke: 1/)).toBeInTheDocument()
  })
})

function stagingResponse() {
  return {
    source: "control-plane", observedAtUtc: "2026-09-24T03:00:00Z", status: "complete", warningCodes: [],
    items: [{
      resourceGroupId: "stage-one", stagingId: "stage-one", ownershipStatus: "matched",
      owner: { kind: "migration", id: "mig_one", displayName: "Example", workspaceHref: "/migrations/mig_one" } as {
        kind: string; id: string; displayName: string; workspaceHref: string
      } | null,
      runtimeStatus: "running", recordedStatus: "retained", containerCount: 2, runningContainerCount: 2,
      networkCount: 1, containerIds: ["pg-stage", "syn-stage"], reasonCodes: [], canRetire: false,
    }],
  }
}
