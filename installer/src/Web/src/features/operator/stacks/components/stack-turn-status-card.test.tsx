import { delay, http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import type { RuntimeStackTurnInspectionResponse } from "../api/stacks.types"
import { StackTurnStatusCard } from "./stack-turn-status-card"

const base = "/internal/host-agent/runtime-stacks/tester"

const platform = {
  status: "ok",
  readiness: "ready",
  running: true,
  ownershipVerified: true,
  imageApproved: true,
  publicHost: "turn.deltabox.dev",
  turnUris: [
    "turn:turn.deltabox.dev:3478?transport=udp",
    "turn:turn.deltabox.dev:3478?transport=tcp",
  ],
  secretPresent: true,
  relayPortsPublished: true,
  securityPolicyApplied: true,
  detail: null,
}

function inspection(
  state: RuntimeStackTurnInspectionResponse["state"],
  recorded: boolean,
): RuntimeStackTurnInspectionResponse {
  const configured = state !== "not-connected"
  const external = state === "external"
  const turnUris = external
    ? [
        "turn:turn.external.test:3478?transport=udp",
        "turn:turn.external.test:3478?transport=tcp",
      ]
    : platform.turnUris
  const publicHost = external ? "turn.external.test" : platform.publicHost
  const realm = external ? "external.test" : "deltabox.dev"
  const userLifetime = external ? "2h" : "1h"
  const allowGuests = external ? false : true

  return {
    source: "control-plane",
    status: "ok",
    runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
    slug: "tester",
    inspectedAtUtc: "2026-07-26T04:56:00Z",
    state,
    management: external ? "external-observed" : configured ? "mem-managed" : "none",
    liveConfiguration: configured ? {
      supported: true,
      anyTurnSettings: true,
      memManagedMarkerPresent: !external,
      turnUris,
      credentialMechanism: "inline-shared-secret",
      sharedSecretPresent: true,
      sharedSecretMatchesPlatform: external ? false : true,
      userLifetime,
      allowGuests,
      publicHost,
      realm,
      fileSha256: external ? "sha256:external" : "sha256:live",
      problemCode: null,
      detail: null,
    } : {
      supported: true,
      anyTurnSettings: false,
      memManagedMarkerPresent: false,
      turnUris: [],
      credentialMechanism: "none",
      sharedSecretPresent: false,
      sharedSecretMatchesPlatform: null,
      userLifetime: null,
      allowGuests: null,
      publicHost: null,
      realm: null,
      fileSha256: "sha256:empty",
      problemCode: null,
      detail: null,
    },
    persistedMetadata: {
      recorded,
      configured: recorded ? true : null,
      turnUris: recorded ? turnUris : [],
      publicHost: recorded ? publicHost : null,
      realm: recorded ? realm : null,
      configurationSource: recorded
        ? external ? "migration-source-preserved" : "platform-coturn"
        : null,
      relayPortsPublished: recorded ? true : null,
      sharedSecretPresent: recorded ? true : null,
      userLifetime: recorded ? userLifetime : null,
      allowGuests: recorded ? allowGuests : null,
      matchesLiveConfiguration: recorded ? true : null,
    },
    platform,
    matrixRuntime: {
      exists: true,
      running: true,
      identityMatches: true,
      problemCode: null,
      detail: null,
    },
    diagnostics: [],
    warnings: configured && !recorded
      ? ["Live TURN settings and persisted runtime metadata disagree."]
      : [],
    detail: external
      ? "External TURN configuration."
      : configured ? "Configuration needs attention." : "Not connected.",
  }
}

function migratedPlatformMatchInspection(): RuntimeStackTurnInspectionResponse {
  const value = inspection("connected", true)
  return {
    ...value,
    liveConfiguration: value.liveConfiguration
      ? { ...value.liveConfiguration, memManagedMarkerPresent: false }
      : null,
    persistedMetadata: {
      ...value.persistedMetadata,
      configurationSource: "migration-source-preserved",
    },
  }
}

function operations(entries: Array<Record<string, unknown>> | unknown = []) {
  const durableEntries = Array.isArray(entries) ? entries : []
  return HttpResponse.json({
    source: "control-plane",
    status: "ok",
    stackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
    slug: "tester",
    operations: durableEntries,
    detail: null,
  })
}

function durableOperation(
  lastError: string,
  operation = "connect-stack-turn",
) {
  const disconnect = operation === "disconnect-stack-turn"
  return {
    id: "33333333-3333-3333-3333-333333333333",
    runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
    operation,
    status: "failed",
    idempotencyKey: disconnect
      ? "turn-disconnect-tester-durable"
      : "turn-connect-tester-durable",
    requestedBy: "owner",
    hostMutationLevel: "filesystem,docker",
    currentStep: lastError.endsWith("rollback_failed")
      ? "rollback-failed"
      : lastError.endsWith("candidate_validation_failed")
        ? "candidate-rejected"
        : "apply-failed",
    requestedAtUtc: "2026-07-26T05:00:00Z",
    startedAtUtc: "2026-07-26T05:00:01Z",
    completedAtUtc: "2026-07-26T05:00:02Z",
    lastError,
  }
}

function rolledBackDurableOperation(
  operation = "connect-stack-turn",
) {
  const disconnect = operation === "disconnect-stack-turn"
  return {
    id: disconnect
      ? "55555555-5555-5555-5555-555555555555"
      : "44444444-4444-4444-4444-444444444444",
    runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
    operation,
    status: "rolled_back",
    idempotencyKey: disconnect
      ? "turn-disconnect-tester-rolled-back"
      : "turn-connect-tester-rolled-back",
    requestedBy: "owner",
    hostMutationLevel: "filesystem,docker",
    currentStep: "rollback-verified",
    requestedAtUtc: "2026-08-19T07:00:00Z",
    startedAtUtc: "2026-08-19T07:00:01Z",
    completedAtUtc: "2026-08-19T07:00:05Z",
    lastError: null,
  }
}

function succeededDurableOperation(
  idempotencyKey: string,
  operation = "connect-stack-turn",
) {
  return {
    id: "66666666-6666-6666-6666-666666666666",
    runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
    operation,
    status: "succeeded",
    idempotencyKey,
    requestedBy: "owner",
    hostMutationLevel: "filesystem,docker",
    currentStep: "completed",
    requestedAtUtc: "2026-08-19T06:56:20Z",
    startedAtUtc: "2026-08-19T06:56:21Z",
    completedAtUtc: "2026-08-19T06:56:27Z",
    lastError: null,
  }
}

afterEach(() => {
  window.localStorage.clear()
})

describe("StackTurnStatusCard connection", () => {
  it("configures a disconnected stack through a reviewed restart path", async () => {
    let connected = false
    let applyBody: Record<string, unknown> | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection(connected ? "connected" : "not-connected", connected))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-configure",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, async ({ request }) => {
        applyBody = await request.json() as Record<string, unknown>
        connected = true
        return HttpResponse.json({
          source: "control-plane",
          status: "succeeded",
          operationId: "11111111-1111-1111-1111-111111111111",
          runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
          slug: "tester",
          mode: "configure",
          configurationChanged: true,
          matrixRestarted: true,
          rollbackAttempted: false,
          rollbackSucceeded: null,
          stateAfter: "connected",
          errorCode: null,
          detail: "Connected.",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    const connectMode = await screen.findByText("Configure Synapse and restart Matrix")
    expect(connectMode).toBeInTheDocument()
    expect(connectMode).toHaveClass("break-words")
    expect(connectMode).not.toHaveClass("break-all")
    expect(screen.getByText("Yes")).toBeInTheDocument()
    expect(screen.getByText(/Matrix container will restart/)).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))
    await waitFor(() => expect(applyBody).toMatchObject({
      reviewHash: "review-configure",
      confirmConnectToPlatformTurn: true,
      confirmReplaceExternalTurn: false,
    }))
    expect(applyBody).not.toHaveProperty("sharedSecret")
    expect(await screen.findByText(/stack is connected to platform TURN/)).toBeInTheDocument()
  })

  it("shows live progress while Connect is applying and while the durable operation remains running", async () => {
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey
          ? [{
              ...succeededDurableOperation(idempotencyKey),
              status: "running",
              completedAtUtc: null,
              currentStep: "restart-matrix",
            }]
          : [],
      )),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-progress",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        await delay(250)
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))

    const applyingTitle = await screen.findByText("Connecting to platform TURN…")
    expect(applyingTitle.closest('[role="alert"]')?.querySelector(".animate-spin")).not.toBeNull()
    const applyingButton = screen.getByRole("button", { name: "Connecting…" })
    expect(applyingButton).toBeDisabled()
    expect(applyingButton.querySelector(".animate-spin")).not.toBeNull()

    await screen.findByRole("button", { name: "Check status" })
    const runningTitle = screen.getByText("Connecting to platform TURN…")
    expect(runningTitle.closest('[role="alert"]')?.querySelector(".animate-spin")).not.toBeNull()
    expect(screen.getByRole("button", { name: "Connecting…" }).querySelector(".animate-spin")).not.toBeNull()
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled()
    expect(screen.queryByText(/browser did not receive/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/durable TURN connection/i)).not.toBeInTheDocument()
    expect(screen.queryByText("TURN connection failed")).not.toBeInTheDocument()
  })

  it("reconciles a successful durable Connect when the browser cannot consume the HTTP 200 response", async () => {
    let connected = false
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection(connected ? "connected" : "not-connected", connected))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey ? [succeededDurableOperation(idempotencyKey)] : [],
      )),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-transport-reconcile",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        connected = true
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))

    await waitFor(() => expect(idempotencyKey).toMatch(/^turn-connect-tester-/))
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(screen.queryByText("TURN connection failed")).not.toBeInTheDocument()
    expect((await screen.findAllByText("Connected")).length).toBeGreaterThan(0)
  })

  it("keeps an inconclusive Connect response explicitly unconfirmed instead of calling it a failure", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-transport-unconfirmed",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, () => HttpResponse.text("response body unavailable", { status: 200 })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))

    expect(await screen.findByText("TURN connection could not yet be confirmed")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Check status" })).toBeInTheDocument()
    expect(screen.queryByText(/browser could not confirm/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/durable operation/i)).not.toBeInTheDocument()
    expect(screen.queryByText("TURN connection failed")).not.toBeInTheDocument()
  })

  it("reports a Connect failure only after the matching durable operation proves failure", async () => {
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey
          ? [{ ...durableOperation("turn_connect_apply_failed"), idempotencyKey }]
          : [],
      )),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-durable-failure",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))

    expect(await screen.findByText("TURN connection failed")).toBeInTheDocument()
    expect(screen.getAllByText(/last TURN connection attempt failed/)).not.toHaveLength(0)
  })

  it("shows an authoritative Connect mutation rejection as a real failure", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "configure",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:empty",
        reviewHash: "review-explicit-connect-failure",
        confirmationText: "Connect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, () => HttpResponse.json({
        code: "turn_connect_apply_failed",
        detail: "Matrix rejected the reviewed TURN change and the previous configuration remains active.",
      }, { status: 409 })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))

    expect(await screen.findByText("TURN connection failed")).toBeInTheDocument()
    expect(screen.getByText(/Matrix rejected the reviewed TURN change/)).toBeInTheDocument()
    expect(screen.queryByText(/could not yet be confirmed/i)).not.toBeInTheDocument()
  })

  it("reconstructs a verified Connect rollback as a safe warning after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, () => operations([rolledBackDurableOperation()])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/restored and verified the previous stack state/)).toBeInTheDocument()
    expect(screen.queryByText(/Technical recovery is required/)).not.toBeInTheDocument()
  })

  it("replaces external TURN only through the explicit reviewed replacement path", async () => {
    let replaced = false
    let applyBody: Record<string, unknown> | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(
        replaced ? inspection("connected", true) : inspection("external", true),
      )),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "replace-external",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:external",
        reviewHash: "review-replace-external",
        confirmationText: "Server-authored text is not used as browser copy.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, async ({ request }) => {
        applyBody = await request.json() as Record<string, unknown>
        replaced = true
        return HttpResponse.json({
          source: "control-plane",
          status: "succeeded",
          operationId: "55555555-5555-5555-5555-555555555555",
          runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
          slug: "tester",
          mode: "replace-external",
          configurationChanged: true,
          matrixRestarted: true,
          rollbackAttempted: false,
          rollbackSucceeded: null,
          stateAfter: "connected",
          errorCode: null,
          detail: "Replaced.",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Replace external TURN" }))
    expect(await screen.findByText("Replace external TURN and restart Matrix")).toBeInTheDocument()
    expect(screen.getByText(/exact reviewed external homeserver.yaml/)).toBeInTheDocument()
    expect(screen.getByText(/restore the exact previous configuration and stack metadata/)).toBeInTheDocument()
    expect(screen.queryByText("Server-authored text is not used as browser copy.")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Replace external TURN" }))
    await waitFor(() => expect(applyBody).toMatchObject({
      reviewHash: "review-replace-external",
      confirmConnectToPlatformTurn: true,
      confirmReplaceExternalTurn: true,
    }))
    expect(applyBody).not.toHaveProperty("sharedSecret")
    expect(await screen.findByText(/external TURN configuration was replaced with platform TURN/)).toBeInTheDocument()
  })

  it("offers metadata-only adoption for a migrated exact platform TURN match", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(migratedPlatformMatchInspection())),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "adopt-existing",
        configurationChangeRequired: false,
        restartRequired: false,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "review-migration-adopt",
        confirmationText: "Record association.",
        consequences: [],
      })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    expect(await screen.findByText("Record the existing matching configuration")).toBeInTheDocument()
    expect(screen.getByText("No")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Disconnect from platform TURN" })).not.toBeInTheDocument()
  })

  it("adopts tester's already matching live configuration without a restart", async () => {
    let connected = false
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection(connected ? "connected" : "drift", connected))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "adopt-existing",
        configurationChangeRequired: false,
        restartRequired: false,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "review-adopt",
        confirmationText: "Record the existing association.",
        consequences: [],
      })),
      http.post(`${base}/turn/connect`, () => {
        connected = true
        return HttpResponse.json({
          source: "control-plane",
          status: "succeeded",
          operationId: "22222222-2222-2222-2222-222222222222",
          runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
          slug: "tester",
          mode: "adopt-existing",
          configurationChanged: false,
          matrixRestarted: false,
          rollbackAttempted: false,
          rollbackSucceeded: null,
          stateAfter: "connected",
          errorCode: null,
          detail: "Adopted.",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    expect(await screen.findByText("Record the existing matching configuration")).toBeInTheDocument()
    expect(screen.getByText("No")).toBeInTheDocument()
    expect(screen.getByText(/will not be rewritten/)).toBeInTheDocument()
    expect(screen.getByText(/will not restart/)).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Connect to platform TURN" }))
    expect(await screen.findByText(/now recorded as MEM-managed/)).toBeInTheDocument()
  })


  it("reconstructs an unresolved rollback outcome after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("drift", false))),
      http.get(`${base}/operations`, () => operations([
        durableOperation("turn_connect_rollback_failed"),
      ])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/could not verify or restore the final stack state automatically/)).toBeInTheDocument()
  })

  it("reconstructs a candidate rejection after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("not-connected", false))),
      http.get(`${base}/operations`, () => operations([
        durableOperation("turn_connect_candidate_validation_failed"),
      ])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/rejected the proposed TURN configuration before any mutation/)).toBeInTheDocument()
  })

  it("keeps the adoption review localized in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("drift", false))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        mode: "adopt-existing",
        configurationChangeRequired: false,
        restartRequired: false,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        userLifetime: "1h",
        allowGuests: true,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "review-adopt",
        confirmationText: "Server text is not used as normal copy.",
        consequences: [],
      })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Mit Plattform-TURN verbinden" }))
    expect(await screen.findByText("Vorhandene übereinstimmende Konfiguration erfassen")).toBeInTheDocument()
    expect(screen.getByText("Der Matrix-Container wird nicht neu gestartet.")).toBeInTheDocument()
    expect(screen.queryByText("Server text is not used as normal copy.")).not.toBeInTheDocument()
  })

  it("disconnects an exact MEM-managed stack through a reviewed restart path", async () => {
    let connected = true
    let applyBody: Record<string, unknown> | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection(connected ? "connected" : "not-connected", connected))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-review",
        confirmationText: "Server-authored text is not normal browser copy.",
        consequences: [],
      })),
      http.post(`${base}/turn/disconnect`, async ({ request }) => {
        applyBody = await request.json() as Record<string, unknown>
        connected = false
        return HttpResponse.json({
          source: "control-plane",
          status: "succeeded",
          operationId: "44444444-4444-4444-4444-444444444444",
          runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
          slug: "tester",
          configurationChanged: true,
          matrixRestarted: true,
          rollbackAttempted: false,
          rollbackSucceeded: null,
          stateAfter: "not-connected",
          errorCode: null,
          detail: "Disconnected.",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    expect(await screen.findByText("Remove MEM-managed TURN settings and restart Matrix")).toBeInTheDocument()
    expect(screen.getByText("Yes")).toBeInTheDocument()
    expect(screen.getByText(/less reliable for users behind restrictive NAT/)).toBeInTheDocument()
    expect(screen.queryByText("Server-authored text is not normal browser copy.")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Disconnect from platform TURN" }))
    await waitFor(() => expect(applyBody).toMatchObject({
      reviewHash: "disconnect-review",
      confirmDisconnectFromPlatformTurn: true,
    }))
    expect(applyBody).not.toHaveProperty("sharedSecret")
    expect(await screen.findByText(/stack is now verified as not connected/)).toBeInTheDocument()
  })

  it("shows live progress while Disconnect is applying and while the durable operation remains running", async () => {
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey
          ? [{
              ...succeededDurableOperation(idempotencyKey, "disconnect-stack-turn"),
              status: "running",
              completedAtUtc: null,
              currentStep: "restart-matrix",
            }]
          : [],
      )),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-progress",
        confirmationText: "Disconnect and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/disconnect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        await delay(250)
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Disconnect from platform TURN" }))

    const applyingTitle = await screen.findByText("Disconnecting from platform TURN…")
    expect(applyingTitle.closest('[role="alert"]')?.querySelector(".animate-spin")).not.toBeNull()
    const applyingButton = screen.getByRole("button", { name: "Disconnecting…" })
    expect(applyingButton).toBeDisabled()
    expect(applyingButton.querySelector(".animate-spin")).not.toBeNull()

    await screen.findByRole("button", { name: "Check status" })
    const runningTitle = screen.getByText("Disconnecting from platform TURN…")
    expect(runningTitle.closest('[role="alert"]')?.querySelector(".animate-spin")).not.toBeNull()
    expect(screen.getByRole("button", { name: "Disconnecting…" }).querySelector(".animate-spin")).not.toBeNull()
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled()
    expect(screen.queryByText(/browser did not receive/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/durable TURN disconnection/i)).not.toBeInTheDocument()
    expect(screen.queryByText("TURN disconnection failed")).not.toBeInTheDocument()
  })

  it("reconciles a successful durable Disconnect when the browser cannot consume the HTTP 200 response", async () => {
    let connected = true
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection(connected ? "connected" : "not-connected", connected))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey
          ? [succeededDurableOperation(idempotencyKey, "disconnect-stack-turn")]
          : [],
      )),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-transport-reconcile",
        confirmationText: "Disconnect and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/disconnect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        connected = false
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Disconnect from platform TURN" }))

    await waitFor(() => expect(idempotencyKey).toMatch(/^turn-disconnect-tester-/))
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(screen.queryByText("TURN disconnection failed")).not.toBeInTheDocument()
    expect((await screen.findAllByText("Not connected")).length).toBeGreaterThan(0)
  })

  it("reports a Disconnect failure only after the matching durable operation proves failure", async () => {
    let idempotencyKey: string | null = null
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, () => operations(
        idempotencyKey
          ? [{ ...durableOperation("turn_disconnect_apply_failed", "disconnect-stack-turn"), idempotencyKey }]
          : [],
      )),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-review-durable-failure",
        confirmationText: "Disconnect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/disconnect`, async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        idempotencyKey = String(body.idempotencyKey)
        return HttpResponse.text("response body unavailable", { status: 200 })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Disconnect from platform TURN" }))

    expect(await screen.findByText("TURN disconnection failed")).toBeInTheDocument()
    expect(screen.getAllByText(/last TURN disconnection attempt failed/)).not.toHaveLength(0)
  })

  it("shows an authoritative Disconnect mutation rejection as a real failure", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-review-explicit-failure",
        confirmationText: "Disconnect tester and restart Matrix.",
        consequences: [],
      })),
      http.post(`${base}/turn/disconnect`, () => HttpResponse.json({
        code: "turn_disconnect_apply_failed",
        detail: "Matrix rejected the reviewed TURN removal and the managed configuration remains active.",
      }, { status: 409 })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    await user.click(screen.getByRole("button", { name: "Disconnect from platform TURN" }))

    expect(await screen.findByText("TURN disconnection failed")).toBeInTheDocument()
    expect(screen.getByText(/Matrix rejected the reviewed TURN removal/)).toBeInTheDocument()
    expect(screen.queryByText(/could not yet be confirmed/i)).not.toBeInTheDocument()
  })

  it("clears a stale Disconnect review error after a newer authoritative TURN inspection", async () => {
    let inspectionCount = 0
    let disconnectMutationCount = 0
    server.use(
      http.get(`${base}/turn`, () => {
        inspectionCount += 1
        const value = inspection("connected", true)
        return HttpResponse.json({
          ...value,
          inspectedAtUtc: inspectionCount > 1
            ? "2026-08-19T11:53:30Z"
            : "2026-08-19T11:53:00Z",
        })
      }),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        code: "turn_operation_in_progress",
        detail: "A previous TURN change is still settling.",
      }, { status: 409 })),
      http.post(`${base}/turn/disconnect`, () => {
        disconnectMutationCount += 1
        return HttpResponse.json({ status: "succeeded" })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Disconnect from platform TURN" }))
    expect(await screen.findByText("Could not review TURN disconnection")).toBeInTheDocument()
    expect(screen.getByText("A previous TURN change is still settling.")).toBeInTheDocument()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(disconnectMutationCount).toBe(0)

    await user.click(screen.getByRole("button", { name: "Retry inspection" }))

    await waitFor(() => {
      expect(screen.queryByText("Could not review TURN disconnection")).not.toBeInTheDocument()
    })
    expect((await screen.findAllByText("Connected")).length).toBeGreaterThan(0)
  })

  it("clears a stale Connect review error after a newer authoritative TURN inspection", async () => {
    let inspectionCount = 0
    let connectMutationCount = 0
    server.use(
      http.get(`${base}/turn`, () => {
        inspectionCount += 1
        const value = inspection("not-connected", false)
        return HttpResponse.json({
          ...value,
          inspectedAtUtc: inspectionCount > 1
            ? "2026-08-19T11:54:30Z"
            : "2026-08-19T11:54:00Z",
        })
      }),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/connect/review`, () => HttpResponse.json({
        code: "turn_operation_in_progress",
        detail: "A previous TURN change is still settling.",
      }, { status: 409 })),
      http.post(`${base}/turn/connect`, () => {
        connectMutationCount += 1
        return HttpResponse.json({ status: "succeeded" })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Connect to platform TURN" }))
    expect(await screen.findByText("Could not review TURN connection")).toBeInTheDocument()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(connectMutationCount).toBe(0)

    await user.click(screen.getByRole("button", { name: "Retry inspection" }))

    await waitFor(() => {
      expect(screen.queryByText("Could not review TURN connection")).not.toBeInTheDocument()
    })
    expect((await screen.findAllByText("Not connected")).length).toBeGreaterThan(0)
  })

  it.each([
    ["external", "External TURN configuration"],
    ["drift", "Configuration needs attention"],
  ] as const)(
    "withholds Disconnect for %s TURN state",
    async (state, expectedLabel) => {
      server.use(
        http.get(`${base}/turn`, () => HttpResponse.json(inspection(state, state === "drift"))),
        http.get(`${base}/operations`, operations),
      )

      renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

      await screen.findAllByText(expectedLabel)
      expect(screen.queryByRole("button", { name: "Disconnect from platform TURN" })).not.toBeInTheDocument()
    },
  )

  it("reconstructs a verified Disconnect rollback as a safe warning after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, () => operations([
        rolledBackDurableOperation("disconnect-stack-turn"),
      ])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/restored and verified the previous connected stack state/)).toBeInTheDocument()
    expect(screen.queryByText(/Technical recovery is required/)).not.toBeInTheDocument()
  })

  it("reconstructs a Disconnect candidate rejection after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, () => operations([
        durableOperation("turn_disconnect_candidate_validation_failed", "disconnect-stack-turn"),
      ])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/rejected the proposed TURN removal before any mutation/)).toBeInTheDocument()
  })

  it("reconstructs an unresolved disconnection rollback outcome after browser refresh", async () => {
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("drift", true))),
      http.get(`${base}/operations`, () => operations([
        durableOperation("turn_disconnect_rollback_failed", "disconnect-stack-turn"),
      ])),
    )

    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    expect(await screen.findByText(/could not verify or restore the final stack state automatically/)).toBeInTheDocument()
  })

  it("keeps the disconnection review localized in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get(`${base}/turn`, () => HttpResponse.json(inspection("connected", true))),
      http.get(`${base}/operations`, operations),
      http.post(`${base}/turn/disconnect/review`, () => HttpResponse.json({
        source: "control-plane",
        status: "ready",
        runtimeStackId: "47dad438-56b0-4f47-a4b5-1fd4e92dc228",
        slug: "tester",
        matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
        configurationChangeRequired: true,
        restartRequired: true,
        platformPublicHost: platform.publicHost,
        turnUris: platform.turnUris,
        currentConfigurationSha256: "sha256:live",
        reviewHash: "disconnect-review-de",
        confirmationText: "Server text is not used as normal copy.",
        consequences: [],
      })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackTurnStatusCard slugOrId="tester" />)

    await user.click(await screen.findByRole("button", { name: "Von Plattform-TURN trennen" }))
    expect(await screen.findByText("MEM-verwaltete TURN-Einstellungen entfernen und Matrix neu starten")).toBeInTheDocument()
    expect(screen.getByText(/weniger zuverlässig/)).toBeInTheDocument()
    expect(screen.queryByText("Server text is not used as normal copy.")).not.toBeInTheDocument()
  })
})
