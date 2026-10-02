import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"

import type { CoturnRuntimeResponse } from "../api/coturn.api"
import { CoturnPage } from "./coturn-page"

const coturnEndpoint = "/internal/host-agent/platform/coturn"
const latestCheckEndpoint = `${coturnEndpoint}/check/latest`
const startupSupervisionEndpoint = `${coturnEndpoint}/startup-supervision`

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
  checks: [
    {
      key: "container",
      status: "passed",
      summary: "The MEM-owned Coturn container is running.",
      detail: null,
    },
    {
      key: "host-dns",
      status: "passed",
      summary: "The host resolver returned a non-loopback address.",
      detail: "Host resolver addresses: 10.10.0.193. This may reflect MEM local split DNS.",
    },
    {
      key: "external-ip",
      status: "passed",
      summary: "Automatic detection advertised a public relay address in the allocation response.",
      code: "relay-auto-public",
      detail: "Observed relays: [8.8.8.8]:49168. External-client reachability still needs an independent external test.",
    },
  ],
  allocation: {
    status: "passed",
    transport: "udp",
    summary: "Coturn accepted temporary credentials and completed a local UDP allocation probe.",
    logTail: "IPv4. Received relay addr: 8.8.8.8:49168",
    relayAddressEvidence: { complete: true, relays: [{ address: "8.8.8.8", port: 49168 }] },
  },
  warnings: [],
  detail: "Coturn passed the available functional checks.",
  evidencePersisted: true,
  incidentId: null,
}

const runtimeResponse: CoturnRuntimeResponse = {
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
  configurationPresent: true,
  configurationExact: true,
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
  turnUris: [
    "turn:turn.deltabox.dev:3478?transport=udp",
    "turn:turn.deltabox.dev:3478?transport=tcp",
  ],
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
  detail: "ready",
}

function renderPage(runtimeMode = "containerized-development") {
  server.use(
    http.get("/api/operator/runtime-context", () =>
      HttpResponse.json(createRuntimeContext({
        runtimeMode,
        runningInContainer: runtimeMode !== "local-development",
      })),
    ),
  )

  return renderWithProviders(
    <MemoryRouter>
      <CoturnPage />
    </MemoryRouter>,
  )
}

function registerInspectHandler(response = runtimeResponse) {
  server.use(
    http.get(coturnEndpoint, () => HttpResponse.json(response)),
    http.get(`${coturnEndpoint}/maintenance/active`, () =>
      HttpResponse.json({
        source: "control-plane",
        active: false,
        operation: null,
      }),
    ),
    http.get(latestCheckEndpoint, () =>
      HttpResponse.json(noLatestCheckResponse),
    ),
    http.get(startupSupervisionEndpoint, () =>
      HttpResponse.json({
        source: "control-plane",
        status: "disabled",
        runtimeMode: "local-development",
        enabled: false,
        decision: "not-evaluated",
        mutationPerformed: false,
        automaticRestartAttempted: false,
        cooldownActive: false,
        cooldownUntilUtc: null,
        operationId: null,
        observedAtUtc: "2026-08-23T01:00:00Z",
        detail: "disabled",
      }),
    ),
  )
}

async function waitForRuntimeOverview(containerStateLabel = "Container state") {
  expect(await screen.findByText(containerStateLabel)).toBeInTheDocument()
}

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

const networkOperationId = "aaaaaaaa-1111-2222-3333-eeeeeeeeeeee"
const networkAccepted = {
  operationId: networkOperationId,
  action: "repair",
  status: "accepted",
  pollUrl: `/internal/host-agent/operations/${networkOperationId}`,
  reusedExistingOperation: false,
}

function registerNetworkOperationCompletion() {
  server.use(http.get(`/internal/host-agent/operations/${networkOperationId}`, () =>
    HttpResponse.json({
      operationId: networkOperationId,
      action: "repair",
      status: "succeeded",
      currentStep: "completed",
      requestedAtUtc: "2026-09-23T00:00:00Z",
      startedAtUtc: "2026-09-23T00:00:00Z",
      completedAtUtc: "2026-09-23T00:00:01Z",
      terminal: true,
      succeeded: true,
      lastError: null,
    }),
  ))
}

describe("CoturnPage", () => {
  it.each([
    ["8.8.8.8", "8.8.8.8"],
    ["2606:4700:4700:0:0:0:0:1111", "2606:4700:4700::1111"],
  ])("reviews and applies %s on a healthy runtime using protected repair", async (enteredIp, canonicalIp) => {
    registerInspectHandler()
    registerNetworkOperationCompletion()
    const bodies: Record<string, unknown>[] = []
    let appliedIp: string | null = null
    server.use(
      http.get(coturnEndpoint, () => HttpResponse.json({ ...runtimeResponse, externalIp: appliedIp })),
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        bodies.push(body)
        appliedIp = canonicalIp
        return HttpResponse.json(networkAccepted, { status: 202 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    const input = screen.getByRole("textbox", { name: /External\/public IP/i })
    await user.type(input, enteredIp)
    expect(bodies).toHaveLength(0)
    expect(screen.queryByRole("button", { name: "Restart & verify" })).not.toBeInTheDocument()
    expect(screen.getByText(/Network setting changed — not applied yet/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Refresh" }))
    expect(input).toHaveValue(enteredIp) // polling must not erase the edit
    await waitFor(() => expect(screen.getByRole("button", { name: "Apply & verify" })).toBeEnabled())
    await user.click(screen.getByRole("button", { name: "Apply & verify" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Apply TURN network settings?")).toBeInTheDocument()
    expect(within(dialog).getByText("Currently configured: Automatic detection")).toBeInTheDocument()
    expect(within(dialog).getByText(`Apply: ${enteredIp}`)).toBeInTheDocument()
    expect(input).toBeDisabled() // preserve reviewed settings through confirmation
    await user.click(within(dialog).getByRole("button", { name: "Apply & verify" }))
    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
    expect(bodies).toHaveLength(1)
    expect(bodies[0]).toMatchObject({ action: "repair", externalIp: enteredIp, idempotencyKey: expect.any(String) })
    await waitFor(() => expect(screen.queryByText(/Network setting changed — not applied yet/)).not.toBeInTheDocument())
    await waitFor(() => expect(input).toHaveValue(canonicalIp))
    expect(screen.getByRole("button", { name: "Restart & verify" })).toBeInTheDocument()
  })

  it("requires explicit review when clearing a persisted external IP back to automatic mode", async () => {
    registerInspectHandler({ ...runtimeResponse, externalIp: "8.8.8.8" })
    registerNetworkOperationCompletion()
    const bodies: Record<string, unknown>[] = []
    server.use(http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
      bodies.push(await request.json() as Record<string, unknown>)
      return HttpResponse.json(networkAccepted, { status: 202 })
    }))
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    const input = screen.getByRole("textbox", { name: /External\/public IP/i })
    await waitFor(() => expect(input).toHaveValue("8.8.8.8"))
    await user.clear(input)
    expect(bodies).toHaveLength(0)
    await user.click(screen.getByRole("button", { name: "Apply & verify" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Currently configured: 8.8.8.8")).toBeInTheDocument()
    expect(within(dialog).getByText("Apply: Automatic detection")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Apply & verify" }))
    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toMatchObject({ action: "repair", externalIp: null })
    // A queued/completed response alone must not claim the persisted setting
    // changed: this fixture intentionally still reports the old runtime label.
    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
    expect(screen.getByText(/Network setting changed — not applied yet/)).toBeInTheDocument()
  })

  it("cancelling an IP change does not mutate and undoing the edit restores the plain restart action", async () => {
    registerInspectHandler()
    let requests = 0
    server.use(http.post(`${coturnEndpoint}/maintenance`, () => {
      requests++
      return HttpResponse.json(networkAccepted, { status: 202 })
    }))
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    const input = screen.getByRole("textbox", { name: /External\/public IP/i })
    await user.type(input, "8.8.8.8")
    await user.click(screen.getByRole("button", { name: "Apply & verify" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }))
    expect(requests).toBe(0)
    expect(input).toHaveValue("8.8.8.8")
    await user.clear(input)
    expect(screen.queryByRole("button", { name: "Apply & verify" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Restart & verify" })).toBeEnabled()
  })

  it("replays the exact reviewed IP and idempotency key after fresh identity verification", async () => {
    registerInspectHandler()
    registerNetworkOperationCompletion()
    const bodies: Record<string, unknown>[] = []
    let verified = false
    server.use(
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        bodies.push(await request.json() as Record<string, unknown>)
        return verified
          ? HttpResponse.json(networkAccepted, { status: 202 })
          : HttpResponse.json({ error: "step_up_required" }, { status: 403 })
      }),
      http.post("/api/auth/step-up", () => {
        verified = true
        return HttpResponse.json({ status: "step_up_authenticated", expiresAtUtc: "2026-09-23T00:05:00Z" })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    const input = screen.getByRole("textbox", { name: /External\/public IP/i })
    await user.type(input, "8.8.8.8")
    await user.click(screen.getByRole("button", { name: "Apply & verify" }))
    const review = await screen.findByRole("alertdialog")
    await user.click(within(review).getByRole("button", { name: "Apply & verify" }))
    const stepUp = await screen.findByRole("dialog", { name: "Verify your identity" })
    expect(input).toBeDisabled()
    await user.type(within(stepUp).getByLabelText("Current password"), "test-only-password")
    await user.type(within(stepUp).getByLabelText("Current authenticator code"), "123456")
    await user.click(within(stepUp).getByRole("button", { name: "Verify identity" }))
    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
    expect(bodies).toHaveLength(2)
    expect(bodies[0]).toMatchObject({ action: "repair", externalIp: "8.8.8.8" })
    expect(bodies[1]).toEqual(bodies[0])
  })

  it("keeps the idempotency key when retrying an ambiguous apply response in the confirmation", async () => {
    registerInspectHandler()
    registerNetworkOperationCompletion()
    const bodies: Record<string, unknown>[] = []
    server.use(http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
      bodies.push(await request.json() as Record<string, unknown>)
      return bodies.length === 1
        ? HttpResponse.json({ error: "response_unavailable" }, { status: 503 })
        : HttpResponse.json({ ...networkAccepted, reusedExistingOperation: true }, { status: 202 })
    }))
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    await user.type(screen.getByRole("textbox", { name: /External\/public IP/i }), "8.8.8.8")
    await user.click(screen.getByRole("button", { name: "Apply & verify" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.click(within(dialog).getByRole("button", { name: "Apply & verify" }))
    expect(await within(dialog).findByText("Could not start this operation")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Apply & verify" }))
    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
    expect(bodies).toHaveLength(2)
    expect(bodies[1]).toEqual(bodies[0])
  })

  it("shows private relay failure prominently even when local allocation passed", async () => {
    registerInspectHandler()
    server.use(http.get(latestCheckEndpoint, () => HttpResponse.json({
      ...noLatestCheckResponse, freshness: "fresh", fresh: true,
      result: {
        ...freshPassedCheck, status: "failed",
        allocation: {
          ...freshPassedCheck.allocation,
          logTail: "IPv4. Received relay addr: 172.18.0.5:49168",
          relayAddressEvidence: { complete: true, relays: [{ address: "172.18.0.5", port: 49168 }] },
        },
        checks: [{ key: "external-ip", status: "failed", code: "relay-non-public", summary: "Private relay detected", detail: "Observed relay: 172.18.0.5:49168" }],
      },
    })))
    renderPage()
    await waitForRuntimeOverview()
    expect(await screen.findByText(/Coturn advertised a private or reserved relay address/)).toBeInTheDocument()
    expect(screen.queryByText("Healthy")).not.toBeInTheDocument()
    expect(screen.getByText(/A public relay address is not proof of external connectivity/)).toBeInTheDocument()
  })

  it("keeps edited network configuration behind the protected-authority gate", async () => {
    registerInspectHandler({ ...runtimeResponse, ownershipVerified: false })
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview()
    await user.type(screen.getByRole("textbox", { name: /External\/public IP/i }), "8.8.8.8")
    expect(screen.getByRole("button", { name: "Apply & verify" })).toBeDisabled()
  })

  it("localizes apply and automatic-mode review in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerInspectHandler({ ...runtimeResponse, externalIp: "8.8.8.8" })
    const user = userEvent.setup()
    renderPage()
    await waitForRuntimeOverview("Containerstatus")
    const input = screen.getByRole("textbox", { name: /Externe\/öffentliche IP/i })
    await waitFor(() => expect(input).toHaveValue("8.8.8.8"))
    await user.clear(input)
    await user.click(screen.getByRole("button", { name: "Anwenden & prüfen" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("TURN-Netzwerkeinstellungen anwenden?")).toBeInTheDocument()
    expect(within(dialog).getByText("Anwenden: Automatische Erkennung")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Abbrechen" }))
  })

  it("repairs recreation-required drift through a durable no-pull server-owned operation", async () => {
    registerInspectHandler({
      ...runtimeResponse,
      externalIp: "203.0.113.10",
      status: "runtime_drift",
      readiness: "repair-required",
      operatorStatus: "repair-required",
      runtimeExact: false,
      runtimeDrift: ["command"],
      dockerRuntime: runtimeResponse.dockerRuntime
        ? { ...runtimeResponse.dockerRuntime, commandMatches: false }
        : null,
    })
    const user = userEvent.setup()
    const operationId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    let maintenanceBody: Record<string, unknown> | null = null

    server.use(
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        maintenanceBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({
          operationId,
          action: "repair",
          status: "accepted",
          pollUrl: `/internal/host-agent/operations/${operationId}`,
          reusedExistingOperation: false,
        }, { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${operationId}`, () =>
        HttpResponse.json({
          operationId,
          runtimeStackId: null,
          status: "succeeded",
          currentStep: "completed",
          requestedAtUtc: "2026-08-23T09:00:00Z",
          startedAtUtc: "2026-08-23T09:00:00Z",
          completedAtUtc: "2026-08-23T09:00:01Z",
          lastError: null,
          terminal: true,
          succeeded: true,
        }),
      ),
    )

    renderPage()

    await waitForRuntimeOverview()
    expect(screen.getByRole("link", { name: "Back to Services" })).toHaveAttribute(
      "href",
      "/services",
    )
    expect(screen.getByText("Platform TURN service")).toBeInTheDocument()
    expect(screen.getByText("Service status")).toBeInTheDocument()
    expect(screen.getByText("Runtime readiness")).toBeInTheDocument()
    expect(screen.getAllByText("Repair required").length).toBeGreaterThan(0)
    expect(screen.queryByText(/coturn-secret\.json/i)).not.toBeInTheDocument()

    await user.click(screen.getByText("Technical details"))
    expect(screen.getByText("Exact Docker runtime")).toBeInTheDocument()
    expect(screen.getByText("Restart policy")).toBeInTheDocument()
    expect(screen.getByText("unless-stopped (expected unless-stopped)")).toBeInTheDocument()
    expect(screen.getAllByText("mem-gateway").length).toBeGreaterThan(0)
    expect(screen.getByText("Container command drift")).toBeInTheDocument()

    expect(
      screen.getByRole("textbox", { name: /External\/public IP/i }),
    ).toHaveValue("203.0.113.10")
    await user.click(
      screen.getByRole("button", { name: "Repair service" }),
    )

    const repairDialog = await screen.findByRole("alertdialog")
    expect(within(repairDialog).getByText("Repair shared TURN service?")).toBeInTheDocument()
    expect(within(repairDialog).getByText(/does not pull a new image/i)).toBeInTheDocument()
    expect(within(repairDialog).getByText(/49160-49200/)).toBeInTheDocument()
    await user.click(within(repairDialog).getByRole("button", { name: "Repair service" }))

    await waitFor(() => {
      expect(maintenanceBody).toMatchObject({
        action: "repair",
        externalIp: "203.0.113.10",
      })
      expect(maintenanceBody?.idempotencyKey).toEqual(expect.any(String))
    })
    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
  })

  it("keeps Repair behind fresh identity verification when the backend requires step-up", async () => {
    registerInspectHandler({
      ...runtimeResponse,
      status: "runtime_drift",
      readiness: "repair-required",
      operatorStatus: "repair-required",
      runtimeExact: false,
      runtimeDrift: ["command"],
      dockerRuntime: runtimeResponse.dockerRuntime
        ? { ...runtimeResponse.dockerRuntime, commandMatches: false }
        : null,
    })
    const user = userEvent.setup()

    server.use(
      http.post(`${coturnEndpoint}/maintenance`, () =>
        HttpResponse.json(
          {
            error: "step_up_required",
            detail: "Fresh identity verification is required before this action.",
          },
          { status: 403 },
        ),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    await user.click(screen.getByRole("button", { name: "Repair service" }))
    const repairDialog = await screen.findByRole("alertdialog")
    await user.click(within(repairDialog).getByRole("button", { name: "Repair service" }))

    expect(await screen.findByText("Verify your identity")).toBeInTheDocument()
  })

  it("offers Restart & verify for restart-policy-only drift without offering destructive Repair", async () => {
    registerInspectHandler({
      ...runtimeResponse,
      status: "runtime_drift",
      readiness: "repair-required",
      operatorStatus: "repair-required",
      runtimeExact: false,
      runtimeDrift: ["restart-policy"],
      dockerRuntime: runtimeResponse.dockerRuntime
        ? {
            ...runtimeResponse.dockerRuntime,
            restartPolicy: "no",
            restartPolicyMatches: false,
          }
        : null,
    })

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.getByRole("button", { name: "Restart & verify" })).toBeEnabled()
    expect(screen.queryByRole("button", { name: "Repair service" })).not.toBeInTheDocument()
  })

  it("restarts an exact runtime through a durable operation and refreshes post-restart functional evidence", async () => {
    const operationId = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff"
    registerInspectHandler()
    const user = userEvent.setup()
    let maintenanceBody: Record<string, unknown> | null = null
    let maintenanceTerminal = false
    let latestReads = 0

    server.use(
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        maintenanceBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({
          operationId,
          action: "restart-verify",
          status: "accepted",
          pollUrl: `/internal/host-agent/operations/${operationId}`,
          reusedExistingOperation: false,
        }, { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${operationId}`, () => {
        maintenanceTerminal = true
        return HttpResponse.json({
          operationId,
          runtimeStackId: null,
          status: "succeeded",
          currentStep: "completed",
          requestedAtUtc: "2026-08-23T09:00:00Z",
          startedAtUtc: "2026-08-23T09:00:00Z",
          completedAtUtc: "2026-08-23T09:00:02Z",
          lastError: null,
          terminal: true,
          succeeded: true,
        })
      }),
      http.get(latestCheckEndpoint, () => {
        latestReads += 1
        if (!maintenanceTerminal) return HttpResponse.json(noLatestCheckResponse)

        return HttpResponse.json({
          source: "control-plane",
          freshness: "fresh",
          fresh: true,
          freshForSeconds: 1800,
          observedAtUtc: "2026-08-23T09:00:02Z",
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          incidentId: null,
          result: freshPassedCheck,
          warnings: [],
          detail: "The latest Coturn functional check is fresh.",
        })
      }),
    )

    renderPage()
    await waitForRuntimeOverview()
    await user.click(screen.getByRole("button", { name: "Restart & verify" }))

    const restartDialog = await screen.findByRole("alertdialog")
    expect(within(restartDialog).getByText("Restart shared TURN service?")).toBeInTheDocument()
    expect(within(restartDialog).getByText(/same MEM-owned Coturn runtime/i)).toBeInTheDocument()
    expect(within(restartDialog).getByText(/Active calls.*may be interrupted briefly/i)).toBeInTheDocument()
    await user.click(within(restartDialog).getByRole("button", { name: "Restart & verify" }))

    await waitFor(() => {
      expect(maintenanceBody).toMatchObject({
        action: "restart-verify",
        externalIp: null,
      })
      expect(maintenanceBody?.idempotencyKey).toEqual(expect.any(String))
    })

    expect(await screen.findByText("Platform TURN maintenance completed")).toBeInTheDocument()
    expect(await screen.findByText("Healthy")).toBeInTheDocument()
    expect(latestReads).toBeGreaterThanOrEqual(2)
    expect(screen.queryByText("Verify your identity")).not.toBeInTheDocument()
  })

  it("rediscovers an accepted maintenance operation after the page is reloaded", async () => {
    registerInspectHandler()
    let operationPolls = 0

    server.use(
      http.get(`${coturnEndpoint}/maintenance/active`, () =>
        HttpResponse.json({
          source: "control-plane",
          active: true,
          operation: {
            operationId: "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
            action: "restart-verify",
            status: "running",
            currentStep: "verify-platform-turn-runtime",
            requestedAtUtc: "2026-08-23T09:20:00Z",
            startedAtUtc: "2026-08-23T09:20:00Z",
            completedAtUtc: null,
            lastError: null,
            terminal: false,
            succeeded: false,
          },
        }),
      ),
      http.get("/internal/host-agent/operations/bbbbbbbb-cccc-dddd-eeee-ffffffffffff", () => {
        operationPolls += 1
        const terminal = operationPolls > 1
        return HttpResponse.json({
          operationId: "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
          runtimeStackId: null,
          status: terminal ? "succeeded" : "running",
          currentStep: terminal ? "completed" : "verify-platform-turn-functional",
          requestedAtUtc: "2026-08-23T09:20:00Z",
          startedAtUtc: "2026-08-23T09:20:00Z",
          completedAtUtc: terminal ? "2026-08-23T09:20:03Z" : null,
          lastError: null,
          terminal,
          succeeded: terminal,
        })
      }),
    )

    renderPage()

    expect(await screen.findByText("Platform TURN maintenance is running")).toBeInTheDocument()
    expect(
      await screen.findByText("Platform TURN maintenance completed", {}, { timeout: 3_500 }),
    ).toBeInTheDocument()
  })

  it("treats host-native protected evidence restriction as verification-limited rather than repair-required", async () => {
    registerInspectHandler({
      ...runtimeResponse,
      status: "protected_evidence_restricted",
      readiness: "verification-limited",
      operatorStatus: "verification-limited",
      runtimeExact: true,
      protectedEvidenceAccess: "restricted",
      secretPresent: false,
      secretSource: "restricted",
      secretFilePermissionsApplied: false,
      securityPolicyApplied: false,
      warnings: [],
      detail:
        "Coturn Docker runtime configuration is exact, but protected host-file verification is intentionally restricted in this execution context.",
    })
    server.use(
      http.get("/api/operator/diagnostics/portainer", () =>
        HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-08-23T01:00:00Z",
          available: true,
          managed: true,
          runtimeState: "ready",
          ownershipState: "managed",
          version: "2.39.5",
          approvedVersion: "2.39.5",
          environmentConfigured: true,
          exactResourceLinksSupported: true,
          links: {
            home: "https://portainer.example.test",
            environment: "https://portainer.example.test/#!/endpoints/1/docker/dashboard",
            containers: "https://portainer.example.test/#!/endpoints/1/docker/containers",
          },
          capabilities: {
            canOpenHome: true,
            canOpenEnvironment: true,
            canOpenContainers: true,
            canOpenExactResource: true,
          },
          warnings: [],
        }),
      ),
    )

    renderPage("local-development")
    await waitForRuntimeOverview()

    expect(screen.getByText("Verification limited")).toBeInTheDocument()
    expect(screen.getByText("Protected evidence restricted")).toBeInTheDocument()
    expect(screen.queryByText("Repair required")).not.toBeInTheDocument()
    expect(screen.getByText("Protected verification is limited")).toBeInTheDocument()
    expect(
      screen.getByText(/intentionally cannot read the owner-only TURN secret/i),
    ).toBeInTheDocument()

    const externalIpInput = screen.getByRole("textbox", { name: /External\/public IP/i })
    const restartButton = screen.getByRole("button", { name: "Restart & verify" })
    const checkButton = screen.getByRole("button", { name: "Check now" })

    expect(externalIpInput).toBeDisabled()
    expect(screen.queryByRole("button", { name: "Repair service" })).not.toBeInTheDocument()
    expect(restartButton).toBeDisabled()
    expect(checkButton).toBeDisabled()
    expect(restartButton).toHaveAttribute(
      "aria-describedby",
      "coturn-action-authority-help",
    )
    expect(checkButton).toHaveAttribute(
      "aria-describedby",
      "coturn-action-authority-help",
    )

    const portainerLink = await screen.findByRole("link", { name: "Open Portainer" })
    expect(portainerLink).toHaveAttribute(
      "href",
      "/api/operator/diagnostics/portainer/resources/platform-service/coturn?service=coturn",
    )
    expect(portainerLink).toHaveAttribute("target", "_blank")

    const user = userEvent.setup()
    expect(
      screen.queryByText("Host-native Coturn recovery"),
    ).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Why are these controls limited?" }))
    expect(screen.getByText("Host-native Coturn recovery")).toBeInTheDocument()
    expect(
      screen.getByText(/fresh Portainer 2\.39\.5 installation requires a one-time setup token/i),
    ).toBeInTheDocument()
    expect(
      screen.getByText("docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1"),
    ).toBeInTheDocument()
    expect(screen.getByText("docker restart portainer")).toBeInTheDocument()
    expect(screen.getByText("docker logs --tail 200 mem-coturn")).toBeInTheDocument()
    expect(screen.getByText("docker restart mem-coturn")).toBeInTheDocument()
    expect(
      screen.getByText(/bypass MEM's durable maintenance journal/i),
    ).toBeInTheDocument()

    await user.click(screen.getByText("Technical details"))

    expect(screen.getByText("Protected evidence access")).toBeInTheDocument()
    expect(screen.getAllByText("Restricted by execution mode").length).toBeGreaterThan(0)
  })

  it("keeps Open Portainer usable when host-native fallback has only the Portainer home", async () => {
    registerInspectHandler({
      ...runtimeResponse,
      status: "protected_evidence_restricted",
      readiness: "verification-limited",
      operatorStatus: "verification-limited",
      protectedEvidenceAccess: "restricted",
    })
    server.use(
      http.get("/api/operator/diagnostics/portainer", () =>
        HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-08-23T01:00:00Z",
          available: true,
          managed: true,
          runtimeState: "ready",
          ownershipState: "managed",
          version: "2.39.5",
          approvedVersion: "2.39.5",
          environmentConfigured: false,
          exactResourceLinksSupported: false,
          links: {
            home: "https://localhost:9443",
            environment: null,
            containers: null,
          },
          capabilities: {
            canOpenHome: true,
            canOpenEnvironment: false,
            canOpenContainers: false,
            canOpenExactResource: false,
          },
          warnings: [
            "portainer_ui_not_configured",
            "portainer_environment_not_configured",
          ],
        }),
      ),
    )

    renderPage("local-development")
    await waitForRuntimeOverview()

    const link = await screen.findByRole("link", { name: "Open Portainer" })
    expect(link).toHaveAttribute(
      "href",
      "/api/operator/diagnostics/portainer/resources/platform-service/coturn?service=coturn",
    )
    expect(link).toHaveAttribute("target", "_blank")
  })

  it("keeps the host-native Portainer and manual-recovery guidance out of containerized mode", async () => {
    registerInspectHandler()

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.queryByRole("link", { name: "Open Portainer" })).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Why are these controls limited?" }),
    ).not.toBeInTheDocument()
  })

  it("runs the focused TURN check and displays a bounded sanitized log tail", async () => {
    registerInspectHandler()
    const user = userEvent.setup()

    server.use(
      http.post(`${coturnEndpoint}/check`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "passed",
          checkedAtUtc: "2026-07-26T10:00:00Z",
          freshUntilUtc: "2026-07-26T10:30:00Z",
          containerState: "running",
          readiness: "ready",
          publicHost: "turn.deltabox.dev",
          runtimeContainerId: "coturn-1",
          runtimeStartedAtUtc: "2026-08-22T05:00:00Z",
          runtimeRestartCount: 0,
          checks: [
            {
              key: "container",
              status: "passed",
              summary: "The MEM-owned Coturn container is running.",
              detail: null,
            },
            {
              key: "relay-ports",
              status: "passed",
              summary: "UDP relay range is published.",
              detail: null,
            },
            {
              key: "host-dns",
              status: "passed",
              summary: "The host resolver returned a non-loopback address.",
              detail: "Host resolver addresses: 10.10.0.193. This may reflect MEM local split DNS.",
            },
            {
              key: "probe-dns",
              status: "passed",
              summary: "The Coturn probe runtime resolved a non-loopback address.",
              detail: "Probe-runtime resolver addresses: 10.10.0.193.",
            },
          ],
          allocation: {
            status: "passed",
            transport: "udp",
            summary: "local allocation completed",
            logTail: "allocation completed",
          },
          warnings: [],
          detail: "local allocation passed",
          evidencePersisted: true,
          incidentId: null,
        }),
      ),
      http.get(`${coturnEndpoint}/logs`, ({ request }) => {
        expect(new URL(request.url).searchParams.get("tail")).toBe("100")
        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          retrievedAtUtc: "2026-07-26T10:01:00Z",
          containerName: "mem-coturn",
          requestedTail: 100,
          returnedLines: 2,
          truncated: false,
          content: "listener ready\ncredential=[redacted]",
          warnings: [],
        })
      }),
    )

    renderPage()
    await waitForRuntimeOverview()

    await user.click(screen.getByRole("button", { name: "Check now" }))

    expect(await screen.findByText("TURN check")).toBeInTheDocument()
    expect(screen.getByText("Coturn container")).toBeInTheDocument()
    expect(screen.getByText("UDP relay range")).toBeInTheDocument()
    expect(screen.getByText("Host DNS resolution")).toBeInTheDocument()
    expect(screen.getByText("Probe-runtime DNS resolution")).toBeInTheDocument()
    expect(screen.getByText(/completed the local allocation probe/i)).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "View recent logs" }))

    expect(await screen.findByText("listener ready", { exact: false })).toBeInTheDocument()
    expect(screen.getByText("credential=[redacted]", { exact: false })).toBeInTheDocument()
  })


  it("uses refreshed server freshness after a current check when the Coturn runtime changes", async () => {
    registerInspectHandler()
    const user = userEvent.setup()
    const currentCheck = {
      ...freshPassedCheck,
      checkedAtUtc: "2026-08-22T10:00:00Z",
      freshUntilUtc: "2026-08-22T10:30:00Z",
    }
    let latestReads = 0

    server.use(
      http.post(`${coturnEndpoint}/check`, () =>
        HttpResponse.json(currentCheck),
      ),
      http.get(latestCheckEndpoint, () => {
        latestReads += 1
        if (latestReads === 1) {
          return HttpResponse.json(noLatestCheckResponse)
        }

        return HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "runtime-changed",
          fresh: false,
          checkedAtUtc: currentCheck.checkedAtUtc,
          freshUntilUtc: currentCheck.freshUntilUtc,
          result: currentCheck,
          detail:
            "The Coturn runtime changed after the latest functional check. Run Check now before relying on that evidence.",
        })
      }),
    )

    renderPage()
    await waitForRuntimeOverview()
    await user.click(screen.getByRole("button", { name: "Check now" }))

    expect(await screen.findByText("Check required")).toBeInTheDocument()
    expect(screen.getByText("Runtime changed")).toBeInTheDocument()
    expect(screen.queryByText("Healthy")).not.toBeInTheDocument()
  })

  it("renders a persisted fresh functional check after reload without requiring another POST", async () => {
    registerInspectHandler()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "fresh",
          fresh: true,
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          result: freshPassedCheck,
          detail: "The latest Coturn functional check is fresh.",
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.getByText("Functional health")).toBeInTheDocument()
    expect(screen.getByText("Healthy")).toBeInTheDocument()
    expect(await screen.findByText("TURN check")).toBeInTheDocument()
    expect(screen.getByText("Fresh")).toBeInTheDocument()
    expect(screen.getByText(/Last checked/)).toBeInTheDocument()
    expect(
      screen.getByText(/External client reachability remains a separate proof/i),
    ).toBeInTheDocument()
    expect(screen.getByText("Advertised relay address")).toBeInTheDocument()
    expect(
      screen.getByText("Automatic detection advertised a public relay address."),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/A public relay address is not proof of external connectivity/i),
    ).toBeInTheDocument()
  })

  it("requires a new functional check when the Coturn runtime changed after the persisted result", async () => {
    registerInspectHandler()
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
    await waitForRuntimeOverview()

    expect(screen.getByText("Check required")).toBeInTheDocument()
    expect(screen.queryByText("Healthy")).not.toBeInTheDocument()
    expect(
      screen.getByText("Coturn changed after this check"),
    ).toBeInTheDocument()
    expect(screen.getByText("Runtime changed")).toBeInTheDocument()
  })

  it("surfaces a failed persisted functional check with its related incident", async () => {
    registerInspectHandler()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          ...noLatestCheckResponse,
          freshness: "fresh",
          fresh: true,
          checkedAtUtc: "2026-08-22T09:55:00Z",
          freshUntilUtc: "2026-08-22T10:25:00Z",
          incidentId: "inc_coturn_failure",
          result: {
            ...freshPassedCheck,
            status: "failed",
            incidentId: "inc_coturn_failure",
            allocation: {
              status: "failed",
              transport: "udp",
              summary: "The local UDP allocation probe failed.",
              logTail: "allocation failed",
            },
            detail: "Coturn needs attention.",
          },
          detail: "The latest Coturn functional check is fresh.",
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.getByText("Needs attention")).toBeInTheDocument()
    const incidentLink = await screen.findByRole("link", {
      name: "Review related incident",
    })
    expect(incidentLink).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_coturn_failure",
    )
  })

  it("localizes an invalid external-IP failure without exposing a raw error as normal copy", async () => {
    registerInspectHandler()
    const user = userEvent.setup()
    const bodies: Record<string, unknown>[] = []

    server.use(
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        bodies.push(await request.json() as Record<string, unknown>)
        return HttpResponse.json(
          {
            error: "coturn_external_ip_invalid",
            detail: "ExternalIp must be a valid IPv4 or IPv6 address.",
          },
          { status: 400 },
        )
      }),
    )

    renderPage()
    await waitForRuntimeOverview()

    const externalIpInput = screen.getByRole("textbox", {
      name: /External\/public IP/i,
    })
    await user.clear(externalIpInput)
    await user.type(externalIpInput, "not-an-ip")
    await user.click(
      screen.getByRole("button", { name: "Apply & verify" }),
    )
    const invalidIpDialog = await screen.findByRole("alertdialog")
    await user.click(within(invalidIpDialog).getByRole("button", { name: "Apply & verify" }))

    expect(
      await screen.findByText(
        "Enter a valid IPv4 or IPv6 address, or leave the field empty for automatic detection.",
      ),
    ).toBeInTheDocument()
    expect(bodies).toHaveLength(1)
    expect(bodies[0]).toMatchObject({ action: "repair", externalIp: "not-an-ip" })
  })

  it.each([
    ["passed", "Coturn accepted short-lived credentials and completed the local allocation probe. This does not prove the external client path."],
    ["failed", "The local allocation probe reached the allocation stage but failed. Review TURN authentication, listener and relay networking, firewall/NAT, and Coturn logs. This does not prove the external client path."],
    ["not-run", "The local allocation probe was not run because a prerequisite was unavailable."],
  ] as const)("renders the %s allocation outcome distinctly", async (status, expectedSummary) => {
    registerInspectHandler()
    const user = userEvent.setup()

    server.use(
      http.post(`${coturnEndpoint}/check`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: status === "not-run" ? "warning" : status,
          checkedAtUtc: "2026-07-26T10:00:00Z",
          freshUntilUtc: "2026-07-26T10:30:00Z",
          containerState: "running",
          readiness: "ready",
          publicHost: "turn.deltabox.dev",
          runtimeContainerId: "coturn-1",
          runtimeStartedAtUtc: "2026-07-26T05:00:00Z",
          runtimeRestartCount: 0,
          checks: [],
          allocation: {
            status,
            transport: "udp",
            summary: status,
            logTail: null,
          },
          warnings: [],
          detail: status,
          evidencePersisted: true,
          incidentId: status === "failed" ? "inc_coturn_failed" : null,
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()
    await user.click(screen.getByRole("button", { name: "Check now" }))

    expect(await screen.findByText(expectedSummary)).toBeInTheDocument()
  })

  it("uses durable Repair for a missing container when protected setup and the approved image remain local", async () => {
    const repairableMissing: CoturnRuntimeResponse = {
      ...runtimeResponse,
      status: "not-deployed",
      containerState: "not-deployed",
      readiness: "not-deployed",
      imageApproved: false,
      containerExists: false,
      running: false,
      ownershipVerified: false,
      containerId: null,
      dockerState: null,
      operatorStatus: "not-deployed",
      runtimeExact: false,
      configurationPresent: true,
      configurationExact: false,
      dockerRuntime: null,
      runtimeDrift: [],
      protectedEvidenceAccess: "available",
      secretPresent: true,
      secretFilePermissionsApplied: true,
      relayPortsPublished: false,
      securityPolicyApplied: true,
      publishedPorts: [],
      detail: "The shared platform TURN container is missing, but protected setup evidence remains available.",
    }
    registerInspectHandler(repairableMissing)
    const user = userEvent.setup()
    let maintenanceBody: Record<string, unknown> | null = null
    const operationId = "cccccccc-dddd-eeee-ffff-000000000000"

    server.use(
      http.post(`${coturnEndpoint}/maintenance`, async ({ request }) => {
        maintenanceBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({
          operationId,
          action: "repair",
          status: "accepted",
          pollUrl: `/internal/host-agent/operations/${operationId}`,
          reusedExistingOperation: false,
        }, { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${operationId}`, () =>
        HttpResponse.json({
          operationId,
          runtimeStackId: null,
          status: "succeeded",
          currentStep: "completed",
          requestedAtUtc: "2026-08-23T09:30:00Z",
          startedAtUtc: "2026-08-23T09:30:00Z",
          completedAtUtc: "2026-08-23T09:30:01Z",
          lastError: null,
          terminal: true,
          succeeded: true,
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.queryByRole("button", { name: "Install or repair shared platform TURN" })).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Repair service" }))
    const missingRepairDialog = await screen.findByRole("alertdialog")
    await user.click(within(missingRepairDialog).getByRole("button", { name: "Repair service" }))

    await waitFor(() => {
      expect(maintenanceBody).toMatchObject({ action: "repair", externalIp: null })
    })
  })

  it("installs an absent shared platform TURN runtime through a durable operation", async () => {
    const operationId = "11111111-2222-3333-4444-555555555555"
    const missing: CoturnRuntimeResponse = {
      ...runtimeResponse,
      status: "not-deployed",
      containerState: "not-deployed",
      readiness: "not-deployed",
      resolvedImageId: null,
      imageApproved: false,
      containerExists: false,
      running: false,
      ownershipVerified: false,
      containerId: null,
      dockerState: null,
      operatorStatus: "not-deployed",
      runtimeExact: false,
      configurationPresent: false,
      configurationExact: false,
      dockerRuntime: null,
      runtimeDrift: ["docker-inspection"],
      protectedEvidenceAccess: "available",
      secretPresent: false,
      secretFilePermissionsApplied: false,
      relayPortsPublished: false,
      securityPolicyApplied: false,
      publishedPorts: [],
      detail: "The shared platform TURN runtime is not deployed.",
    }
    registerInspectHandler(missing)
    const user = userEvent.setup()
    let installBody: unknown = null

    server.use(
      http.post(`${coturnEndpoint}/install`, async ({ request }) => {
        installBody = await request.json()
        return HttpResponse.json(
          {
            operationId,
            status: "accepted",
            pollUrl: `/internal/host-agent/operations/${operationId}`,
            reusedExistingOperation: false,
          },
          { status: 202 },
        )
      }),
      http.get(`/internal/host-agent/operations/${operationId}`, () =>
        HttpResponse.json({
          operationId,
          runtimeStackId: null,
          status: "succeeded",
          currentStep: "completed",
          requestedAtUtc: "2026-08-18T00:00:00Z",
          startedAtUtc: "2026-08-18T00:00:00Z",
          completedAtUtc: "2026-08-18T00:00:01Z",
          lastError: null,
          terminal: true,
          succeeded: true,
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    await user.click(screen.getByRole("button", { name: "Install or repair shared platform TURN" }))
    const installDialog = await screen.findByRole("alertdialog")
    expect(within(installDialog).getByText("Install shared platform TURN?")).toBeInTheDocument()
    expect(within(installDialog).getByText(/TCP\/UDP 3478/)).toBeInTheDocument()
    await user.click(within(installDialog).getByRole("button", { name: "Install or repair shared platform TURN" }))

    await waitFor(() => {
      expect(installBody).toEqual({ externalIp: null })
    })
    expect(await screen.findByText("Shared platform TURN installed")).toBeInTheDocument()
  })

  it("hides an earlier startup repair finding after a newer fresh passing verification proves convergence", async () => {
    registerInspectHandler()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          freshness: "fresh",
          fresh: true,
          freshForSeconds: 1800,
          observedAtUtc: "2026-08-23T01:10:00Z",
          checkedAtUtc: freshPassedCheck.checkedAtUtc,
          freshUntilUtc: freshPassedCheck.freshUntilUtc,
          incidentId: null,
          result: {
            ...freshPassedCheck,
            checkedAtUtc: "2026-08-23T01:10:00Z",
            freshUntilUtc: "2026-08-23T01:40:00Z",
          },
          warnings: [],
          detail: "Fresh passing Coturn verification is available.",
        }),
      ),
      http.get(startupSupervisionEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "repair-required",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "repair-required",
          mutationPerformed: false,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          observedAtUtc: "2026-08-23T01:00:00Z",
          detail: "Earlier startup command drift required reviewed repair.",
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(screen.getByText("Healthy")).toBeInTheDocument()
    expect(screen.queryByText("Coturn requires reviewed repair")).not.toBeInTheDocument()
  })

  it("keeps a current startup repair finding visible when no newer fresh passing verification exists", async () => {
    registerInspectHandler()
    server.use(
      http.get(latestCheckEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          freshness: "fresh",
          fresh: true,
          freshForSeconds: 1800,
          observedAtUtc: "2026-08-23T00:55:00Z",
          checkedAtUtc: "2026-08-23T00:55:00Z",
          freshUntilUtc: "2026-08-23T01:25:00Z",
          incidentId: null,
          result: {
            ...freshPassedCheck,
            checkedAtUtc: "2026-08-23T00:55:00Z",
            freshUntilUtc: "2026-08-23T01:25:00Z",
          },
          warnings: [],
          detail: "Passing evidence predates the startup repair finding.",
        }),
      ),
      http.get(startupSupervisionEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "repair-required",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "repair-required",
          mutationPerformed: false,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          observedAtUtc: "2026-08-23T01:00:00Z",
          detail: "Current startup repair finding.",
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(await screen.findByText("Coturn requires reviewed repair")).toBeInTheDocument()
  })

  it("shows successful automatic startup recovery as first-class service evidence", async () => {
    registerInspectHandler()
    server.use(
      http.get(startupSupervisionEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "recovered",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "start-stopped",
          mutationPerformed: true,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          observedAtUtc: "2026-08-23T01:02:27Z",
          detail: "recovered",
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByText("Coturn recovered during startup"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/safely restored the exact owned Coturn runtime/i),
    ).toBeInTheDocument()
  })

  it("shows when startup supervision defers to an operator-requested Coturn operation", async () => {
    registerInspectHandler()
    server.use(
      http.get(startupSupervisionEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "deferred",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "not-evaluated",
          mutationPerformed: false,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: null,
          observedAtUtc: "2026-08-23T01:02:20Z",
          detail: "deferred",
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByText("Startup supervision deferred"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/will not compete with it/i),
    ).toBeInTheDocument()
  })

  it("blocks competing manual controls while bounded startup recovery is active", async () => {
    registerInspectHandler()
    server.use(
      http.get(startupSupervisionEndpoint, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "recovering",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "start-stopped",
          mutationPerformed: false,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          observedAtUtc: "2026-08-23T01:02:20Z",
          detail: "recovering",
        }),
      ),
    )

    renderPage()
    await waitForRuntimeOverview()

    expect(
      await screen.findByText("Coturn startup recovery is running"),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Restart & verify" }),
    ).toBeDisabled()
    expect(
      screen.getByRole("button", { name: "Check now" }),
    ).toBeDisabled()
  })

  it("keeps the normal global Coturn controls and restart confirmation localized in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerInspectHandler()
    const user = userEvent.setup()

    renderPage()

    expect(
      await screen.findByRole("heading", { name: "Coturn-TURN-Server" }),
    ).toBeInTheDocument()
    await waitForRuntimeOverview("Containerstatus")
    expect(
      screen.queryByRole("button", { name: "Dienst reparieren" }),
    ).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Neu starten & prüfen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Jetzt prüfen" })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Neu starten & prüfen" }))
    const restartDialog = await screen.findByRole("alertdialog")
    expect(within(restartDialog).getByText("Gemeinsamen TURN-Dienst neu starten?")).toBeInTheDocument()
    expect(within(restartDialog).getByText("Kurze Dienstunterbrechung")).toBeInTheDocument()
    await user.click(within(restartDialog).getByRole("button", { name: "Abbrechen" }))
  })
})
