import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  checkCoturn,
  ensureCoturn,
  getCoturnInstallOperation,
  getActiveCoturnMaintenance,
  getCoturnStartupSupervision,
  getCoturnMaintenanceOperation,
  getCoturnLogs,
  getLatestCoturnCheck,
  inspectCoturn,
  installCoturn,
  maintainCoturn,
} from "./coturn.api"

const runtimeResponse = {
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
  containerId: "container-1",
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
  realm: "example.test",
  publicHost: "turn.example.test",
  turnPort: 3478,
  relayMinPort: 49160,
  relayMaxPort: 49200,
  turnUris: [
    "turn:turn.example.test:3478?transport=udp",
    "turn:turn.example.test:3478?transport=tcp",
  ],
  secretPresent: true,
  secretSource: "protected-file",
  secretStorage: "protected-host-file",
  secretFilePermissionsApplied: true,
  expectedBaseDomain: "example.test",
  configuredBaseDomain: "example.test",
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

describe("Coturn control-plane transport", () => {
  it("uses the authenticated browser session without sending infrastructure authority", async () => {
    const requests: string[] = []

    server.use(
      http.get("/internal/host-agent/platform/coturn", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json(runtimeResponse)
      }),
      http.post("/internal/host-agent/platform/coturn/ensure", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(await request.json()).toEqual({
          externalIp: "203.0.113.10",
          forceRecreate: true,
          publishRelayPorts: true,
        })
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({ ...runtimeResponse, externalIp: "203.0.113.10" })
      }),
      http.post("/internal/host-agent/platform/coturn/install", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(await request.json()).toEqual({ externalIp: null })
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          operationId: "11111111-2222-3333-4444-555555555555",
          status: "accepted",
          pollUrl: "/internal/host-agent/operations/11111111-2222-3333-4444-555555555555",
          reusedExistingOperation: false,
        }, { status: 202 })
      }),
      http.post("/internal/host-agent/platform/coturn/maintenance", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(await request.json()).toEqual({
          action: "repair",
          externalIp: "203.0.113.10",
          idempotencyKey: "repair-request-1",
        })
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          action: "repair",
          status: "accepted",
          pollUrl: "/internal/host-agent/operations/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          reusedExistingOperation: false,
        }, { status: 202 })
      }),
      http.get("/internal/host-agent/platform/coturn/startup-supervision", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          source: "control-plane",
          status: "recovered",
          runtimeMode: "containerized-development",
          enabled: true,
          decision: "start-stopped",
          mutationPerformed: true,
          automaticRestartAttempted: false,
          cooldownActive: false,
          cooldownUntilUtc: null,
          operationId: "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
          observedAtUtc: "2026-08-23T01:02:27Z",
          detail: "recovered",
        })
      }),
      http.get("/internal/host-agent/platform/coturn/maintenance/active", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          source: "control-plane",
          active: true,
          operation: {
            operationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            action: "repair",
            status: "running",
            currentStep: "verify-platform-turn-functional",
            requestedAtUtc: "2026-08-18T00:00:00Z",
            startedAtUtc: "2026-08-18T00:00:00Z",
            completedAtUtc: null,
            lastError: null,
            terminal: false,
            succeeded: false,
          },
        })
      }),
      http.get("/internal/host-agent/operations/:operationId", ({ request, params }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          operationId: params.operationId,
          runtimeStackId: null,
          status: "succeeded",
          currentStep: "completed",
          requestedAtUtc: "2026-08-18T00:00:00Z",
          startedAtUtc: "2026-08-18T00:00:00Z",
          completedAtUtc: "2026-08-18T00:00:01Z",
          lastError: null,
          terminal: true,
          succeeded: true,
        })
      }),
      http.post("/internal/host-agent/platform/coturn/check", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          source: "control-plane",
          status: "passed",
          checkedAtUtc: "2026-07-26T10:00:00Z",
          freshUntilUtc: "2026-07-26T10:30:00Z",
          containerState: "running",
          readiness: "ready",
          publicHost: "turn.example.test",
          runtimeContainerId: "container-1",
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
        })
      }),
      http.get("/internal/host-agent/platform/coturn/check/latest", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          source: "control-plane",
          freshness: "fresh",
          fresh: true,
          freshForSeconds: 1800,
          observedAtUtc: "2026-08-22T10:01:00Z",
          checkedAtUtc: "2026-08-22T10:00:00Z",
          freshUntilUtc: "2026-08-22T10:30:00Z",
          incidentId: null,
          result: {
            source: "control-plane",
            status: "passed",
            checkedAtUtc: "2026-08-22T10:00:00Z",
            freshUntilUtc: "2026-08-22T10:30:00Z",
            containerState: "running",
            readiness: "ready",
            publicHost: "turn.example.test",
            runtimeContainerId: "container-1",
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
          },
          warnings: [],
          detail: "The latest Coturn functional check is fresh.",
        })
      }),
      http.get("/internal/host-agent/platform/coturn/logs", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(new URL(request.url).searchParams.get("tail")).toBe("200")
        requests.push(`${request.method} ${new URL(request.url).pathname}`)
        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          retrievedAtUtc: "2026-07-26T10:01:00Z",
          containerName: "mem-coturn",
          requestedTail: 200,
          returnedLines: 1,
          truncated: false,
          content: "ready",
          warnings: [],
        })
      }),
    )

    await expect(inspectCoturn()).resolves.toMatchObject({
      readiness: "ready",
      protectedEvidenceAccess: "available",
    })
    await expect(
      ensureCoturn({
        externalIp: "203.0.113.10",
        forceRecreate: true,
        publishRelayPorts: true,
      }),
    ).resolves.toMatchObject({ externalIp: "203.0.113.10" })
    const accepted = await installCoturn({ externalIp: null })
    expect(accepted.operationId).toBe("11111111-2222-3333-4444-555555555555")
    await expect(getCoturnInstallOperation(accepted.operationId)).resolves.toMatchObject({
      terminal: true,
      succeeded: true,
    })
    const maintenance = await maintainCoturn({
      action: "repair",
      externalIp: "203.0.113.10",
      idempotencyKey: "repair-request-1",
    })
    expect(maintenance.action).toBe("repair")
    await expect(getActiveCoturnMaintenance()).resolves.toMatchObject({
      active: true,
      operation: {
        operationId: maintenance.operationId,
        action: "repair",
      },
    })
    await expect(getCoturnMaintenanceOperation(maintenance.operationId)).resolves.toMatchObject({
      terminal: true,
      succeeded: true,
    })
    await expect(getCoturnStartupSupervision()).resolves.toMatchObject({
      status: "recovered",
      enabled: true,
      mutationPerformed: true,
    })
    await expect(checkCoturn()).resolves.toMatchObject({ status: "passed" })
    await expect(getLatestCoturnCheck()).resolves.toMatchObject({
      freshness: "fresh",
      fresh: true,
    })
    await expect(getCoturnLogs(999)).resolves.toMatchObject({ requestedTail: 200 })

    expect(requests).toEqual([
      "GET /internal/host-agent/platform/coturn",
      "POST /internal/host-agent/platform/coturn/ensure",
      "POST /internal/host-agent/platform/coturn/install",
      "GET /internal/host-agent/operations/11111111-2222-3333-4444-555555555555",
      "POST /internal/host-agent/platform/coturn/maintenance",
      "GET /internal/host-agent/platform/coturn/maintenance/active",
      "GET /internal/host-agent/operations/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      "GET /internal/host-agent/platform/coturn/startup-supervision",
      "POST /internal/host-agent/platform/coturn/check",
      "GET /internal/host-agent/platform/coturn/check/latest",
      "GET /internal/host-agent/platform/coturn/logs",
    ])
  })

  it("surfaces the server-safe JSON detail for failed operations", async () => {
    server.use(
      http.post("/internal/host-agent/platform/coturn/check", () =>
        HttpResponse.json(
          {
            error: "coturn_check_unavailable",
            detail: "Coturn is not deployed.",
          },
          { status: 400 },
        ),
      ),
    )

    await expect(checkCoturn()).rejects.toMatchObject({
      code: "coturn_check_unavailable",
      message: "Coturn is not deployed.",
    })
  })
})
