import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import {
  acknowledgeDiagnosticIncident,
  getDiagnosticDockerEvidence,
  getDiagnosticsAttention,
  connectDiagnosticsSeq,
  getDiagnosticsPortainerOverview,
  changeDiagnosticsSeqDelivery,
  checkDiagnosticsSeqHealth,
  executeDiagnosticsSeqBootstrap,
  getDiagnosticsSeqBootstrapOperation,
  getDiagnosticsSeqBootstrapOverview,
  getDiagnosticsSeqOverview,
  listDiagnosticEvents,
  listDiagnosticIncidents,
  refreshDiagnosticDockerEvidence,
  reopenDiagnosticIncident,
  resolveDiagnosticIncident,
  reviewDiagnosticsSeqBootstrap,
  reviewDiagnosticsSeqSetup,
  runDiagnosticsPipelineSelfTest,
  runDiagnosticsSeqRuntimeAction,
  snoozeDiagnosticIncident,
  updateDiagnosticsSeqUiAuthority,
  verifyDiagnosticsSeqDelivery,
} from "./diagnostics.api"

describe("diagnostics API", () => {
  it("requests the bounded server-authored attention summary", async () => {
    let requestedUrl = ""
    server.use(
      http.get("/api/operator/diagnostics/attention", ({ request }) => {
        requestedUrl = request.url
        return HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-08-04T05:30:00Z",
          state: "ready",
          total: 0,
          highestSeverity: null,
          items: [],
          partial: false,
          warnings: [],
        })
      }),
    )

    await getDiagnosticsAttention()

    const url = new URL(requestedUrl)
    expect(url.pathname).toBe("/api/operator/diagnostics/attention")
    expect(url.searchParams.get("limit")).toBe("5")
  })

  it("encodes bounded incident filters and opaque cursors", async () => {
    let requestedUrl = ""
    server.use(
      http.get("/api/operator/diagnostics/incidents", ({ request }) => {
        requestedUrl = request.url
        return HttpResponse.json({
          fromUtc: "2026-08-01T00:00:00Z",
          untilUtc: "2026-08-02T00:00:00Z",
          pageSize: 50,
          windowClamped: false,
          incidents: [],
          nextCursor: null,
          partial: false,
          warnings: [],
        })
      }),
    )

    await listDiagnosticIncidents({
      feature: "migration",
      search: "synapse readiness",
      lifecycle: "resolved",
      cursor: "opaque+/=cursor",
      pageSize: 50,
    })

    const url = new URL(requestedUrl)
    expect(url.searchParams.get("feature")).toBe("migration")
    expect(url.searchParams.get("search")).toBe("synapse readiness")
    expect(url.searchParams.get("lifecycle")).toBe("resolved")
    expect(url.searchParams.get("cursor")).toBe("opaque+/=cursor")
    expect(url.searchParams.get("pageSize")).toBe("50")
  })

  it("uses the explicit incident lifecycle action contracts", async () => {
    const requests: Array<{ path: string; body: unknown }> = []
    const response = {
      incident: {
        incidentId: "inc_123",
        severity: "error",
        eventCode: "docker_operation_failed",
        feature: "migration",
        stage: null,
        message: "Incident",
        firstSeenAtUtc: "2026-08-02T01:00:00Z",
        lastSeenAtUtc: "2026-08-02T01:01:00Z",
        occurrenceCount: 1,
        retryable: false,
        truncated: false,
        resource: null,
        workspaceLink: null,
        lifecycle: { state: "acknowledged", reopened: false },
      },
      capabilities: {},
      relatedEventCount: 1,
      technicalEvents: [],
      operations: [],
      truncated: false,
      warnings: [],
    }

    for (const action of ["acknowledge", "snooze", "resolve", "reopen"] as const) {
      server.use(
        http.post(`/api/operator/diagnostics/incidents/inc_123/${action}`, async ({ request }) => {
          requests.push({
            path: new URL(request.url).pathname,
            body: await request.json(),
          })
          return HttpResponse.json(response)
        }),
      )
    }

    await acknowledgeDiagnosticIncident("inc_123")
    await snoozeDiagnosticIncident("inc_123", "2026-08-03T01:00:00.000Z")
    await resolveDiagnosticIncident("inc_123", "fixed")
    await reopenDiagnosticIncident("inc_123")

    expect(requests).toEqual([
      { path: "/api/operator/diagnostics/incidents/inc_123/acknowledge", body: {} },
      {
        path: "/api/operator/diagnostics/incidents/inc_123/snooze",
        body: { snoozedUntilUtc: "2026-08-03T01:00:00.000Z" },
      },
      {
        path: "/api/operator/diagnostics/incidents/inc_123/resolve",
        body: { resolutionCode: "fixed" },
      },
      { path: "/api/operator/diagnostics/incidents/inc_123/reopen", body: {} },
    ])
  })

  it("requests only the selected technical-event severity", async () => {
    let requestedUrl = ""
    server.use(
      http.get("/api/operator/diagnostics/events", ({ request }) => {
        requestedUrl = request.url
        return HttpResponse.json({
          fromUtc: "2026-08-01T00:00:00Z",
          untilUtc: "2026-08-02T00:00:00Z",
          pageSize: 25,
          windowClamped: false,
          events: [],
          nextCursor: null,
          warnings: [],
        })
      }),
    )

    await listDiagnosticEvents({ severity: "warning", pageSize: 25 })
    const url = new URL(requestedUrl)
    expect(url.searchParams.get("severity")).toBe("warning")
    expect(url.searchParams.get("pageSize")).toBe("25")
  })

  it("requests the owner-only server-authored Portainer overview", async () => {
    let requestedPath = ""
    server.use(
      http.get("/api/operator/diagnostics/portainer", ({ request }) => {
        requestedPath = new URL(request.url).pathname
        return HttpResponse.json({
          schemaVersion: 1,
          available: false,
          links: { home: null, environment: null, containers: null },
          capabilities: {
            canOpenHome: false,
            canOpenEnvironment: false,
            canOpenContainers: false,
            canOpenExactResource: false,
          },
          warnings: [],
        })
      }),
    )

    await getDiagnosticsPortainerOverview()

    expect(requestedPath).toBe("/api/operator/diagnostics/portainer")
  })

  it("uses the dedicated safe Seq overview and secret-free setup review routes", async () => {
    const requests: Array<{ method: string; path: string; body: unknown }> = []
    server.use(
      http.get("/api/operator/diagnostics/seq", ({ request }) => {
        requests.push({ method: request.method, path: new URL(request.url).pathname, body: null })
        return HttpResponse.json({ schemaVersion: 1, configured: false })
      }),
      http.post("/api/operator/diagnostics/seq/setup/review", async ({ request }) => {
        requests.push({
          method: request.method,
          path: new URL(request.url).pathname,
          body: await request.json(),
        })
        return HttpResponse.json({ schemaVersion: 1, reviewId: "seq_review_api" })
      }),
    )

    await getDiagnosticsSeqOverview()
    const review = await reviewDiagnosticsSeqSetup()

    expect(requests).toEqual([
      { method: "GET", path: "/api/operator/diagnostics/seq", body: null },
      { method: "POST", path: "/api/operator/diagnostics/seq/setup/review", body: {} },
    ])
    expect(review.reviewId).toBe("seq_review_api")
  })

  it("sends only the one-time Seq administrator password to the connection boundary", async () => {
    let requestBody: unknown = null
    server.use(
      http.post("/api/operator/diagnostics/seq/connect", async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "11111111-1111-1111-1111-111111111111",
          status: "succeeded",
          credentialState: "available",
          verificationState: "verified",
          apiKeyId: "api-key-1",
          verificationId: "seq-connect-1",
          eventId: "event-1",
          verifiedAtUtc: "2026-08-05T06:00:00Z",
          reusedCredential: false,
          overview: { schemaVersion: 3 },
        })
      }),
    )

    await connectDiagnosticsSeq("current-seq-password")

    expect(requestBody).toEqual({ administratorPassword: "current-seq-password" })
    expect(Object.keys(requestBody as Record<string, unknown>)).toEqual([
      "administratorPassword",
    ])
  })

  it("uses the guided Seq bootstrap routes without browser-authored Docker targets", async () => {
    const requests: Array<{ method: string; path: string; body: unknown }> = []
    server.use(
      http.get("/api/operator/diagnostics/seq/bootstrap", ({ request }) => {
        requests.push({ method: request.method, path: new URL(request.url).pathname, body: null })
        return HttpResponse.json({ schemaVersion: 1, state: "not-installed", canStartSetup: true })
      }),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", async ({ request }) => {
        requests.push({ method: request.method, path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json({ schemaVersion: 1, reviewId: "seq_bootstrap_review_api", ready: true })
      }),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", async ({ request }) => {
        requests.push({ method: request.method, path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "11111111-1111-1111-1111-111111111111",
          status: "queued",
          acceptedAtUtc: "2026-08-05T00:00:00Z",
        }, { status: 202 })
      }),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        ({ request }) => {
          requests.push({ method: request.method, path: new URL(request.url).pathname, body: null })
          return HttpResponse.json({
            schemaVersion: 1,
            operationId: "11111111-1111-1111-1111-111111111111",
            status: "running",
            currentStep: "preparing-image",
            checks: [],
            warnings: [],
          })
        },
      ),
    )

    await getDiagnosticsSeqBootstrapOverview()
    await reviewDiagnosticsSeqBootstrap({ acceptEula: true, privateUiUrl: null, enableEventDelivery: true })
    await executeDiagnosticsSeqBootstrap({
      reviewId: "seq_bootstrap_review_api",
      administratorPassword: "Correct-Horse-Battery-42",
      administratorPasswordConfirmation: "Correct-Horse-Battery-42",
      connectionAdministratorPassword: "",
    })
    await getDiagnosticsSeqBootstrapOperation("11111111-1111-1111-1111-111111111111")

    expect(requests).toEqual([
      { method: "GET", path: "/api/operator/diagnostics/seq/bootstrap", body: null },
      {
        method: "POST",
        path: "/api/operator/diagnostics/seq/bootstrap/review",
        body: { acceptEula: true, privateUiUrl: null, enableEventDelivery: true },
      },
      {
        method: "POST",
        path: "/api/operator/diagnostics/seq/bootstrap/execute",
        body: {
          reviewId: "seq_bootstrap_review_api",
          administratorPassword: "Correct-Horse-Battery-42",
          administratorPasswordConfirmation: "Correct-Horse-Battery-42",
          connectionAdministratorPassword: "",
        },
      },
      {
        method: "GET",
        path: "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        body: null,
      },
    ])
    const executeBody = requests[2].body as Record<string, unknown>
    expect(Object.keys(executeBody).sort()).toEqual([
      "administratorPassword",
      "administratorPasswordConfirmation",
      "connectionAdministratorPassword",
      "reviewId",
    ])
    expect(JSON.stringify(executeBody)).not.toMatch(/image|container|hostPath|network|port|apiKey/i)
  })

  it("updates only the server-owned Seq UI authority", async () => {
    let requestBody: unknown = null
    server.use(
      http.put("/api/operator/diagnostics/seq/ui-authority", async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          configured: true,
          url: "http://127.0.0.1:15341/",
          updatedAtUtc: "2026-08-05T05:00:00Z",
        })
      }),
    )

    const response = await updateDiagnosticsSeqUiAuthority("http://127.0.0.1:15341")

    expect(requestBody).toEqual({ url: "http://127.0.0.1:15341" })
    expect(response.configured).toBe(true)
    expect(response.url).toBe("http://127.0.0.1:15341/")
    expect(JSON.stringify(requestBody)).not.toMatch(/container|image|hostPath|network|apiKey|password/i)
  })

  it("uses server-owned Seq lifecycle targets and a one-boolean delivery request", async () => {
    const requests: Array<{ path: string; body: unknown }> = []
    const response = {
      schemaVersion: 1,
      operationId: "11111111-1111-1111-1111-111111111111",
      operation: "seq.start",
      status: "succeeded",
      startedAtUtc: "2026-08-04T01:00:00Z",
      completedAtUtc: "2026-08-04T01:00:01Z",
      dataRetained: true,
      overview: { schemaVersion: 2 },
      warnings: [],
    }
    server.use(
      http.post("/api/operator/diagnostics/seq/runtime/start", async ({ request }) => {
        requests.push({ path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json(response)
      }),
      http.post("/api/operator/diagnostics/seq/delivery", async ({ request }) => {
        requests.push({ path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json({ ...response, operation: "seq.delivery.disable" })
      }),
      http.post("/api/operator/diagnostics/seq/health-check", async ({ request }) => {
        requests.push({ path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json({ ...response, operation: "seq.health-check" })
      }),
      http.post("/api/operator/diagnostics/seq/delivery/verify", async ({ request }) => {
        requests.push({ path: new URL(request.url).pathname, body: await request.json() })
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "22222222-2222-2222-2222-222222222222",
          status: "succeeded",
          verificationId: "seq-active-api",
          emittedAtUtc: "2026-08-05T09:00:00Z",
          overview: { schemaVersion: 5 },
        })
      }),
    )

    await runDiagnosticsSeqRuntimeAction("start")
    await changeDiagnosticsSeqDelivery(false)
    await checkDiagnosticsSeqHealth()
    await verifyDiagnosticsSeqDelivery()

    expect(requests).toEqual([
      { path: "/api/operator/diagnostics/seq/runtime/start", body: {} },
      { path: "/api/operator/diagnostics/seq/delivery", body: { enabled: false } },
      { path: "/api/operator/diagnostics/seq/health-check", body: {} },
      { path: "/api/operator/diagnostics/seq/delivery/verify", body: {} },
    ])
    expect(JSON.stringify(requests)).not.toMatch(/apiKey|password|hostPath|containerId|image|url/i)
  })

  it("starts the bounded pipeline self-test without a browser-authored target", async () => {
    let method = ""
    let path = ""
    let body: unknown = null
    server.use(
      http.post("/api/operator/diagnostics/self-test", async ({ request }) => {
        method = request.method
        path = new URL(request.url).pathname
        body = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          verificationId: "diag_verify_api",
          status: "passed",
          startedAtUtc: "2026-08-03T00:00:00Z",
          completedAtUtc: "2026-08-03T00:00:01Z",
          eventId: "evt_api",
          checks: [],
          warnings: [],
        })
      }),
    )

    const result = await runDiagnosticsPipelineSelfTest()

    expect(method).toBe("POST")
    expect(path).toBe("/api/operator/diagnostics/self-test")
    expect(body).toEqual({})
    expect(result.verificationId).toBe("diag_verify_api")
  })

  it("uses only the incident identity for bounded Docker evidence", async () => {
    const methods: string[] = []
    const paths: string[] = []
    const response = {
      available: false,
      resource: null,
      observedAtUtc: null,
      container: null,
      logTail: null,
      warningCode: "diagnostics.docker_evidence_resource_not_resolved",
      warnings: ["diagnostics.docker_evidence_resource_not_resolved"],
    }
    server.use(
      http.get("/api/operator/diagnostics/incidents/inc_123/docker-evidence", ({ request }) => {
        methods.push(request.method)
        paths.push(new URL(request.url).pathname)
        return HttpResponse.json(response)
      }),
      http.post("/api/operator/diagnostics/incidents/inc_123/docker-evidence/refresh", ({ request }) => {
        methods.push(request.method)
        paths.push(new URL(request.url).pathname)
        return HttpResponse.json(response)
      }),
    )

    await getDiagnosticDockerEvidence("inc_123")
    await refreshDiagnosticDockerEvidence("inc_123")

    expect(methods).toEqual(["GET", "POST"])
    expect(paths).toEqual([
      "/api/operator/diagnostics/incidents/inc_123/docker-evidence",
      "/api/operator/diagnostics/incidents/inc_123/docker-evidence/refresh",
    ])
  })

})
