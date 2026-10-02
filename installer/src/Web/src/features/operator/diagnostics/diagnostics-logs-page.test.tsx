import { fireEvent, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"
import { DiagnosticsLogsPage } from "./diagnostics-logs-page"

const capabilities = {
  canReadTechnicalEvents: true,
  canGenerateSupportReport: true,
  canViewOwnerHealthFacts: true,
  canReadDockerEvidence: true,
  canVerifyPipeline: true,
  canOpenPortainer: true,
  canManageIncidentLifecycle: true,
}

const health = {
  observedAtUtc: "2026-08-02T01:05:00Z",
  status: "ready",
  localRecorder: {
    enabled: true,
    status: "ready",
    persistentRecorderConfigured: true,
    persistentRecorderActive: true,
    persistentFilePath: "/data/logs/control-plane/mem-.clef",
    lastFileWriteAtUtc: "2026-08-02T01:04:00Z",
    retainedFileCount: 2,
    retainedBytes: 1024,
    serilogSelfLogMessageCount: 0,
    warningCode: null,
    storage: {
      status: "ready",
      availableBytes: 10737418240,
      totalBytes: 21474836480,
      warningCode: null,
    },
  },
  safeEventStore: {
    enabled: true,
    status: "ready",
    lastWriteAtUtc: "2026-08-02T01:04:00Z",
    lastReadAtUtc: "2026-08-02T01:05:00Z",
    storedEventCount: 7,
    droppedEventCount: 0,
    malformedLineCount: 0,
    lastWriteErrorCode: null,
    lastReadWarningCode: null,
    lastRetentionRunAtUtc: null,
    lastRetentionDeletedFileCount: 0,
    lastRetentionDeletedBytes: 0,
    lastRetentionErrorCode: null,
    hasEverRecordedEvent: true,
    storage: {
      status: "low",
      availableBytes: 536870912,
      totalBytes: 21474836480,
      warningCode: "diagnostics.storage_low",
    },
  },
  seq: {
    status: "optional-disabled",
    sinkEnabled: false,
    managementEnabled: false,
    configured: false,
    serverUrl: null,
    reachable: false,
    lastCheckedAtUtc: null,
    lastSuccessAtUtc: null,
    warningCode: null,
  },
  capabilities,
  partial: false,
  warnings: [],
}

const overview = {
  schemaVersion: 1,
  generatedAtUtc: "2026-08-02T01:05:00Z",
  status: "attention",
  counts: { information: 4, warning: 2, error: 1, critical: 0, incidentCount: 1, eventCount: 7 },
  loggingHealth: health,
  capabilities,
  partial: false,
  truncated: false,
  warnings: [],
}

const event = {
  schemaVersion: 1,
  eventId: "evt_1",
  timestampUtc: "2026-08-02T01:01:00Z",
  severity: "error",
  eventCode: "docker_operation_failed",
  source: "MemGlobalExceptionHandler",
  feature: "migration",
  stage: "private-staging",
  message: "The private Synapse staging container exited before readiness.",
  incidentId: "inc_123",
  traceId: "trace_123",
  spanId: "span_123",
  requestId: "request_123",
  correlationId: "correlation_123",
  operationId: null,
  resource: null,
  expected: { readiness: "ready" },
  observed: { containerState: "exited" },
  details: { exitCode: "1" },
  exception: {
    type: "DockerApiException",
    message: "Container exited before readiness.",
    stackTrace: "at Safe.Frame()",
    innerExceptions: [],
  },
  suggestedAction: "Review the staging container evidence.",
  retryable: false,
  redactionsApplied: true,
  truncated: false,
}

const incidentDetail = {
  incident: {
    incidentId: "inc_123",
    severity: "error",
    eventCode: "docker_operation_failed",
    feature: "migration",
    stage: "private-staging",
    message: "The private Synapse staging container exited before readiness.",
    firstSeenAtUtc: "2026-08-02T01:00:00Z",
    lastSeenAtUtc: "2026-08-02T01:01:00Z",
    occurrenceCount: 2,
    retryable: false,
    truncated: false,
    resource: {
      kind: "migration",
      id: "mig_123",
      displayName: "Migration mig_123",
      stackId: null,
      stackSlug: null,
      service: "synapse",
      workspacePath: "/migrations/mig_123",
    },
    workspaceLink: "/migrations/mig_123",
    lifecycle: {
      state: "open",
      reopened: false,
      storedDisposition: null,
      updatedAtUtc: null,
      updatedByOperatorId: null,
      observedThroughAtUtc: null,
      observedThroughEventId: null,
      snoozedUntilUtc: null,
      resolutionCode: null,
      revision: null,
    },
  },
  capabilities,
  relatedEventCount: 1,
  technicalEvents: [event],
  operations: [],
  truncated: false,
  warnings: [],
}

beforeEach(() => {
  window.localStorage.clear()
  server.use(
    http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview)),
    http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json(health)),
    http.get("/api/operator/diagnostics/incidents", () => HttpResponse.json({
      fromUtc: "2026-08-01T01:05:00Z",
      untilUtc: "2026-08-02T01:05:00Z",
      pageSize: 50,
      windowClamped: false,
      incidents: [incidentDetail.incident],
      nextCursor: null,
      partial: false,
      warnings: [],
    })),
    http.get("/api/operator/diagnostics/events", () => HttpResponse.json({
      fromUtc: "2026-08-01T01:05:00Z",
      untilUtc: "2026-08-02T01:05:00Z",
      pageSize: 50,
      windowClamped: false,
      events: [event],
      nextCursor: null,
      warnings: [],
    })),
    http.get("/api/operator/diagnostics/incidents/inc_123", () => HttpResponse.json(incidentDetail)),
    http.get("/api/operator/diagnostics/incidents/inc_123/docker-evidence", () => HttpResponse.json({
      available: false,
      resource: incidentDetail.incident.resource,
      observedAtUtc: null,
      container: null,
      logTail: null,
      warningCode: "diagnostics.docker_evidence_resource_not_resolved",
      warnings: ["diagnostics.docker_evidence_resource_not_resolved"],
    })),
    http.post("/api/operator/diagnostics/incidents/inc_123/docker-evidence/refresh", () => HttpResponse.json({
      available: false,
      resource: incidentDetail.incident.resource,
      observedAtUtc: null,
      container: null,
      logTail: null,
      warningCode: "diagnostics.docker_evidence_resource_not_resolved",
      warnings: ["diagnostics.docker_evidence_resource_not_resolved"],
    })),
    http.post("/api/operator/diagnostics/self-test", () => HttpResponse.json({
      schemaVersion: 1,
      verificationId: "diag_verify_123456",
      status: "passed",
      startedAtUtc: "2026-08-02T01:06:00Z",
      completedAtUtc: "2026-08-02T01:06:01Z",
      eventId: "evt_self_test_123456",
      checks: [
        { code: "local_recorder_writable", status: "passed", warningCode: null },
        { code: "safe_event_write", status: "passed", warningCode: null },
        { code: "safe_event_read_back", status: "passed", warningCode: null },
        { code: "correlation_round_trip", status: "passed", warningCode: null },
        { code: "seq_delivery", status: "not-configured", warningCode: null },
      ],
      warnings: [],
    })),
    http.post("/api/operator/diagnostics/support-report", () => HttpResponse.json({
      schemaVersion: 1,
      generatedAtUtc: "2026-08-02T01:06:00Z",
      memVersion: "0.2.0",
      runtimeContext: createRuntimeContext({ version: "0.2.0" }),
      incident: incidentDetail.incident,
      events: [event],
      operations: [],
      loggingHealth: health,
      dockerEvidence: null,
      redaction: { policyVersion: "1", redactionsApplied: true, omittedContent: ["raw Docker output"] },
      truncated: false,
      warnings: [],
    })),
  )
})

describe("DiagnosticsLogsPage", () => {
  it("opens a deep-linked incident and copies a safe support report", async () => {
    const user = userEvent.setup()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } })

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findAllByText("The private Synapse staging container exited before readiness.")).not.toHaveLength(0)
    expect(screen.getByText("docker_operation_failed")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open owning workspace" })).toHaveAttribute("href", "/migrations/mig_123")

    await user.click(screen.getByRole("button", { name: "Copy support JSON" }))
    expect(writeText).toHaveBeenCalledTimes(1)
    expect(writeText.mock.calls[0]?.[0]).toContain('"incidentId": "inc_123"')
  })

  it("opens an incident from its message or incident id", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=incidents"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    const incidentMessage = await screen.findByRole("button", {
      name: "The private Synapse staging container exited before readiness.",
    })
    expect(screen.getByRole("button", { name: "inc_123" })).toBeInTheDocument()

    await user.click(incidentMessage)

    expect(await screen.findByRole("button", { name: "Back to incident list" })).toBeInTheDocument()
    expect(screen.getAllByText("inc_123").length).toBeGreaterThan(0)
  })

  it("bulk resolves selected visible incidents with one reason and leaves failures selected", async () => {
    const user = userEvent.setup()
    const secondIncident = {
      ...incidentDetail.incident,
      incidentId: "inc_456",
      eventCode: "api.request.unexpected_failure",
      message: "A second incident also needs operator attention.",
    }
    const resolvedIncident = {
      ...incidentDetail.incident,
      incidentId: "inc_resolved",
      message: "An already resolved incident.",
      lifecycle: {
        ...incidentDetail.incident.lifecycle,
        state: "resolved",
        resolutionCode: "fixed",
      },
    }
    const resolutionRequests: Array<{ incidentId: string; resolutionCode: string }> = []

    server.use(
      http.get("/api/operator/diagnostics/incidents", () => HttpResponse.json({
        fromUtc: "2026-08-01T01:05:00Z",
        untilUtc: "2026-08-02T01:05:00Z",
        pageSize: 50,
        windowClamped: false,
        incidents: [incidentDetail.incident, secondIncident, resolvedIncident],
        nextCursor: null,
        partial: false,
        warnings: [],
      })),
      http.post("/api/operator/diagnostics/incidents/inc_123/resolve", async ({ request }) => {
        const body = await request.json() as { resolutionCode: string }
        resolutionRequests.push({ incidentId: "inc_123", resolutionCode: body.resolutionCode })
        return HttpResponse.json({
          ...incidentDetail,
          incident: {
            ...incidentDetail.incident,
            lifecycle: { ...incidentDetail.incident.lifecycle, state: "resolved", resolutionCode: body.resolutionCode },
          },
        })
      }),
      http.post("/api/operator/diagnostics/incidents/inc_456/resolve", async ({ request }) => {
        const body = await request.json() as { resolutionCode: string }
        resolutionRequests.push({ incidentId: "inc_456", resolutionCode: body.resolutionCode })
        return HttpResponse.json({ code: "diagnostics.test_failure" }, { status: 500 })
      }),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=incidents"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("checkbox", { name: "Select all visible" }))
    expect(screen.getByText("2 selected")).toBeInTheDocument()
    expect(screen.queryByRole("checkbox", { name: "Select incident inc_resolved" })).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Resolve selected" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.selectOptions(within(dialog).getByLabelText("Resolution reason"), "fixed")
    await user.click(within(dialog).getByRole("button", { name: "Resolve selected" }))

    await waitFor(() => expect(resolutionRequests).toEqual([
      { incidentId: "inc_123", resolutionCode: "fixed" },
      { incidentId: "inc_456", resolutionCode: "fixed" },
    ]))
    expect(await screen.findByText("Resolved: 1. Failed: 1. Failed incidents remain selected.")).toBeInTheDocument()
    expect(screen.getByText("1 selected")).toBeInTheDocument()
  })

  it("shows technical events only when the server capability allows them", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Recorded incidents")).toBeInTheDocument()
    await user.click(await screen.findByRole("button", { name: "Technical events" }))
    expect(await screen.findByText("Container exited before readiness.")).toBeInTheDocument()
    expect(screen.getByText("Server-side redaction was applied.")).toBeInTheDocument()
  })

  it("keeps incident bulk lifecycle actions owner-only", async () => {
    const auditorCapabilities = {
      ...capabilities,
      canManageIncidentLifecycle: false,
    }
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json({
        ...overview,
        capabilities: auditorCapabilities,
        loggingHealth: { ...health, capabilities: auditorCapabilities },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=incidents"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Recorded incidents")).toBeInTheDocument()
    expect(screen.queryByRole("checkbox", { name: "Select all visible" })).not.toBeInTheDocument()
    expect(screen.queryByRole("checkbox", { name: "Select incident inc_123" })).not.toBeInTheDocument()
    expect(await screen.findByRole("button", { name: "inc_123" })).toBeInTheDocument()
  })

  it("hides technical events and support actions for an Auditor projection", async () => {
    const auditorCapabilities = {
      canReadTechnicalEvents: false,
      canGenerateSupportReport: false,
      canViewOwnerHealthFacts: false,
      canReadDockerEvidence: false,
      canVerifyPipeline: false,
      canOpenPortainer: false,
      canManageIncidentLifecycle: false,
    }
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json({
        ...overview,
        capabilities: auditorCapabilities,
        loggingHealth: { ...health, capabilities: auditorCapabilities },
      })),
      http.get("/api/operator/diagnostics/incidents/inc_123", () => HttpResponse.json({
        ...incidentDetail,
        capabilities: auditorCapabilities,
        technicalEvents: null,
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("inc_123")).toBeInTheDocument()
    expect(screen.getByText("Read-only")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Acknowledge" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Resolve" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Snooze" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Technical events" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Copy support JSON" })).not.toBeInTheDocument()
  })

  it("shows disabled Seq as Not configured without a warning", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Local black-box recorder")).toBeInTheDocument()
    expect(screen.getByText("Not configured")).toBeInTheDocument()
    expect(screen.queryByText("Logging-health warnings")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Incidents" }))
    expect(await screen.findByText("Recorded incidents")).toBeInTheDocument()
  })

  it("renders an intentionally stopped Seq runtime as a friendly neutral state", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        seq: {
          ...health.seq,
          status: "stopped-intentionally",
          managementEnabled: true,
          configured: true,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    const status = await screen.findByText("Stopped intentionally")
    expect(status).toHaveAttribute("data-slot", "badge")
    expect(status).toHaveAttribute("data-variant", "outline")
    expect(screen.getByText("The MEM-managed Seq container is present but stopped.")).toBeInTheDocument()
    expect(screen.queryByText("stopped-intentionally")).not.toBeInTheDocument()
  })

  it("localises an absent managed Seq runtime in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        seq: {
          ...health.seq,
          status: "runtime-absent",
          managementEnabled: true,
          configured: true,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    const status = await screen.findByText("Laufzeit nicht vorhanden")
    expect(status).toHaveAttribute("data-slot", "badge")
    expect(status).toHaveAttribute("data-variant", "outline")
    expect(screen.getByText("Die Verwaltung ist konfiguriert, aber es ist kein MEM-verwalteter Seq-Container vorhanden.")).toBeInTheDocument()
    expect(screen.queryByText("runtime-absent")).not.toBeInTheDocument()
  })

  it("offers advanced Seq search only when the owner receives an authoritative UI URL", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        seq: {
          status: "ready",
          sinkEnabled: true,
          managementEnabled: true,
          configured: true,
          serverUrl: "https://seq.example.test",
          reachable: true,
          lastCheckedAtUtc: "2026-08-02T01:05:00Z",
          lastSuccessAtUtc: "2026-08-02T01:05:00Z",
          warningCode: null,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    const link = await screen.findByRole("link", { name: "Open advanced Seq search" })
    expect(link).toHaveAttribute("href", "https://seq.example.test")
    expect(link).toHaveAttribute("target", "_blank")
    expect(screen.getByText("Last successful check")).toBeInTheDocument()
  })

  it("runs the owner-only diagnostics pipeline verification without creating an incident", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Run verification" }))

    expect(await screen.findByText("Diagnostics pipeline verified")).toBeInTheDocument()
    expect(screen.getByText("diag_verify_123456")).toBeInTheDocument()
    expect(screen.getByText("evt_self_test_123456")).toBeInTheDocument()
    expect(screen.getByText("Local CLEF recorder")).toBeInTheDocument()
    expect(screen.getByText("Optional Seq delivery")).toBeInTheDocument()
    expect(screen.getAllByText("Not configured")).not.toHaveLength(0)
    expect(screen.getByText(/does not create a warning, error/i)).toBeInTheDocument()
  })

  it("shows the exact failed self-test stage without inventing a successful result", async () => {
    server.use(
      http.post("/api/operator/diagnostics/self-test", () => HttpResponse.json({
        schemaVersion: 1,
        verificationId: "diag_verify_failed",
        status: "failed",
        startedAtUtc: "2026-08-02T01:06:00Z",
        completedAtUtc: "2026-08-02T01:06:01Z",
        eventId: "evt_self_test_failed",
        checks: [
          { code: "local_recorder_writable", status: "passed", warningCode: null },
          { code: "safe_event_write", status: "passed", warningCode: null },
          { code: "safe_event_read_back", status: "failed", warningCode: "diagnostics.self_test.safe_event_read_back_failed" },
          { code: "correlation_round_trip", status: "not-run", warningCode: "diagnostics.self_test.safe_event_read_back_failed" },
          { code: "seq_delivery", status: "not-configured", warningCode: null },
        ],
        warnings: ["diagnostics.self_test.safe_event_read_back_failed"],
      })),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Run verification" }))

    expect(await screen.findByText("Diagnostics pipeline verification failed")).toBeInTheDocument()
    expect(screen.getByText("Safe event read-back")).toBeInTheDocument()
    expect(screen.getAllByText("Failed")).not.toHaveLength(0)
    expect(screen.getAllByText("diagnostics.self_test.safe_event_read_back_failed")).not.toHaveLength(0)
  })

  it("hides the pipeline verification action when the server capability is absent", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        capabilities: { ...capabilities, canVerifyPipeline: false },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Local black-box recorder")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Run verification" })).not.toBeInTheDocument()
  })

  it("distinguishes a ready store that has never recorded an event", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        safeEventStore: {
          ...health.safeEventStore,
          lastWriteAtUtc: null,
          storedEventCount: 0,
          hasEverRecordedEvent: false,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("The safe store is ready, but no browser-safe diagnostic event has ever been recorded.")).toBeInTheDocument()
    expect(screen.getByText("Events recorded since process start")).toBeInTheDocument()
  })

  it("presents a degraded safe store as unavailable rather than empty", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        status: "degraded",
        safeEventStore: {
          ...health.safeEventStore,
          status: "degraded",
          lastWriteAtUtc: null,
          storedEventCount: 0,
          hasEverRecordedEvent: false,
          lastReadWarningCode: "diagnostics.store_read_failed",
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Browser-safe diagnostic event storage is unavailable or degraded; this is not an empty healthy state.")).toBeInTheDocument()
  })

  it("distinguishes an active recorder with no observed file write", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        localRecorder: {
          ...health.localRecorder,
          lastFileWriteAtUtc: null,
          retainedFileCount: 0,
          retainedBytes: 0,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("The recorder is active, but MEM has not yet observed a persistent file write.")).toBeInTheDocument()
  })

  it("does not present a disabled or unavailable safe store as an empty healthy store", async () => {
    server.use(
      http.get("/api/operator/diagnostics/logging-health", () => HttpResponse.json({
        ...health,
        status: "degraded",
        safeEventStore: {
          ...health.safeEventStore,
          enabled: false,
          status: "disabled",
          lastWriteAtUtc: null,
          storedEventCount: 0,
          hasEverRecordedEvent: false,
        },
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Browser-safe diagnostic event storage is deliberately disabled.")).toBeInTheDocument()
    expect(screen.queryByText(/no browser-safe diagnostic event has ever been recorded/i)).not.toBeInTheDocument()
  })

  it("downloads a support report with an incident-and-UTC filename", async () => {
    const user = userEvent.setup()
    const createObjectUrl = vi.fn(() => "blob:diagnostics")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Download support report" }))
    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(click).toHaveBeenCalledTimes(1)
    const anchor = click.mock.instances[0] as HTMLAnchorElement
    expect(anchor.download).toContain("inc_123")
    expect(anchor.download).toContain("20260802T010600Z")
  })


  it("offers an owner-only server redirect to the current incident container", async () => {
    const user = userEvent.setup()
    server.use(
      http.get(
        "/api/operator/diagnostics/incidents/inc_123/docker-evidence",
        () => HttpResponse.json({
          available: true,
          resource: incidentDetail.incident.resource,
          observedAtUtc: "2026-08-04T08:00:00Z",
          container: {
            logicalName: "Migration private Synapse staging runtime",
            observedState: "running",
            exitCode: null,
            health: "healthy",
            startedAtUtc: "2026-08-04T07:59:00Z",
            finishedAtUtc: null,
            restartCount: 0,
            image: "matrixdotorg/synapse:latest",
          },
          logTail: null,
          warningCode: null,
          warnings: [],
        }),
      ),
    )
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Load Docker evidence" }))

    expect(await screen.findByRole("link", { name: "Open this container in Portainer" })).toHaveAttribute(
      "href",
      "/api/operator/diagnostics/portainer/incidents/inc_123/container",
    )
  })

  it("keeps the incident usable when Docker evidence is unavailable", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Load Docker evidence" }))
    expect(await screen.findByText("Docker evidence is unavailable, but the incident remains usable.")).toBeInTheDocument()
    expect(screen.getByText("diagnostics.docker_evidence_resource_not_resolved")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Copy support JSON" })).toBeEnabled()
  })

  it("shows bounded storage capacity in Logging health", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findAllByText("Storage capacity")).toHaveLength(2)
    expect(screen.getByText("10 GiB")).toBeInTheDocument()
    expect(screen.getByText("512 MiB")).toBeInTheDocument()
  })

  it("renders the recovered Logging Health contract in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=health"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findAllByText("Speicherkapazität")).toHaveLength(2)
    expect(screen.getByText("Nicht konfiguriert")).toBeInTheDocument()
    expect(screen.getAllByText("Verfügbar")).not.toHaveLength(0)
    expect(screen.getByText("Diagnosepipeline überprüfen")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Überprüfung starten" })).toBeInTheDocument()
  })

  it("keeps incident filters bounded and sends the cursor returned by the API", async () => {
    const seenUrls: string[] = []
    server.use(
      http.get("/api/operator/diagnostics/incidents", ({ request }) => {
        seenUrls.push(request.url)
        const url = new URL(request.url)
        return HttpResponse.json({
          fromUtc: "2026-08-01T01:05:00Z",
          untilUtc: "2026-08-02T01:05:00Z",
          pageSize: 50,
          windowClamped: false,
          incidents: [incidentDetail.incident],
          nextCursor: url.searchParams.has("cursor") ? null : "cursor_2",
          partial: false,
          warnings: [],
        })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Recorded incidents")).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText("Search diagnostics"), { target: { value: "synapse" } })
    await user.click(screen.getByRole("button", { name: "Apply filters" }))
    await user.click(await screen.findByRole("button", { name: "Next" }))

    expect(seenUrls.some((value) => value.includes("search=synapse"))).toBe(true)
    expect(seenUrls.every((value) => new URL(value).searchParams.get("lifecycle") === "open")).toBe(true)
    expect(seenUrls.some((value) => value.includes("cursor=cursor_2"))).toBe(true)
  })

  it("uses shared breadcrumbs and exposes summary counters as bookmarkable filters", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    const breadcrumbs = await screen.findByRole("navigation", { name: "Breadcrumb" })
    expect(breadcrumbs).toHaveTextContent("Diagnostics")
    expect(breadcrumbs).toHaveTextContent("Incidents and technical events")
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute("href", "/diagnostics")
    expect(await screen.findByRole("link", { name: /Warnings\s*2/i })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=events&level=warning",
    )
    expect(await screen.findByRole("link", { name: /Information\s*4/i })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=events&level=information",
    )
    expect(await screen.findByRole("link", { name: /Events\s*7/i })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=events",
    )
  })

  it("keeps warning technical activity separate from incident attention", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json({
        ...overview,
        status: "ready",
        counts: {
          information: 21,
          warning: 1,
          error: 0,
          critical: 0,
          incidentCount: 0,
          eventCount: 22,
        },
      })),
      http.get("/api/operator/diagnostics/incidents", () => HttpResponse.json({
        fromUtc: "2026-08-01T01:05:00Z",
        untilUtc: "2026-08-02T01:05:00Z",
        pageSize: 50,
        windowClamped: false,
        incidents: [],
        nextCursor: null,
        partial: false,
        warnings: [],
      })),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("No recent recorded incidents require attention.")).toBeInTheDocument()
    expect(screen.getByText("No incidents require attention")).toBeInTheDocument()
    expect(screen.getByText("No incidents require attention. 22 technical events are available for review.")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "View technical events" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=events",
    )
  })

  it("shows newest technical events without a severity filter and loads older cursor pages deliberately", async () => {
    const seenUrls: string[] = []
    const olderEvent = {
      ...event,
      eventId: "evt_older",
      timestampUtc: "2026-08-02T00:59:00Z",
      message: "An older bounded technical event.",
      incidentId: null,
    }
    server.use(
      http.get("/api/operator/diagnostics/events", ({ request }) => {
        seenUrls.push(request.url)
        const url = new URL(request.url)
        const hasCursor = url.searchParams.has("cursor")
        return HttpResponse.json({
          fromUtc: "2026-08-01T01:05:00Z",
          untilUtc: "2026-08-02T01:05:00Z",
          pageSize: 50,
          windowClamped: false,
          events: hasCursor ? [olderEvent] : [event],
          nextCursor: hasCursor ? null : "cursor_older",
          warnings: [],
        })
      }),
    )
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=events"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Container exited before readiness.")).toBeInTheDocument()
    expect(seenUrls[0]).not.toContain("severity=")
    expect(screen.getByText("Newest events first")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Load 50 more events" }))

    expect(await screen.findByText("An older bounded technical event.")).toBeInTheDocument()
    expect(seenUrls.some((url) => url.includes("cursor=cursor_older"))).toBe(true)
    expect(screen.getByText("All matching events in this bounded window are loaded.")).toBeInTheDocument()
  })

  it("reads severity filters from the URL and can clear them", async () => {
    const seenSeverities: Array<string | null> = []
    server.use(
      http.get("/api/operator/diagnostics/events", ({ request }) => {
        seenSeverities.push(new URL(request.url).searchParams.get("severity"))
        return HttpResponse.json({
          fromUtc: "2026-08-01T01:05:00Z",
          untilUtc: "2026-08-02T01:05:00Z",
          pageSize: 50,
          windowClamped: false,
          events: [event],
          nextCursor: null,
          warnings: [],
        })
      }),
    )
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?tab=events&level=information"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByDisplayValue("Information")).toBeInTheDocument()
    expect(seenSeverities).toContain("information")
    await user.click(screen.getByRole("button", { name: "Clear filters" }))
    expect(await screen.findByDisplayValue("All severities")).toBeInTheDocument()
    expect(seenSeverities).toContain(null)
  })

  it("defaults the incident workspace to Needs attention and exposes lifecycle history filters", async () => {
    const seenLifecycle: Array<string | null> = []
    server.use(
      http.get("/api/operator/diagnostics/incidents", ({ request }) => {
        seenLifecycle.push(new URL(request.url).searchParams.get("lifecycle"))
        return HttpResponse.json({
          fromUtc: "2026-08-01T01:05:00Z",
          untilUtc: "2026-08-02T01:05:00Z",
          pageSize: 50,
          windowClamped: false,
          incidents: [incidentDetail.incident],
          nextCursor: null,
          partial: false,
          warnings: [],
        })
      }),
    )
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("button", { name: "Incidents" })).toBeInTheDocument()
    expect(await screen.findByRole("button", { name: "Needs attention", pressed: true })).toBeInTheDocument()
    expect(seenLifecycle).toContain("open")

    await user.click(screen.getByRole("button", { name: "Resolved" }))
    expect(await screen.findByRole("button", { name: "Resolved", pressed: true })).toBeInTheDocument()
    expect(seenLifecycle).toContain("resolved")

    await user.click(screen.getByRole("button", { name: "All" }))
    expect(await screen.findByRole("button", { name: "All", pressed: true })).toBeInTheDocument()
    expect(seenLifecycle).toContain("all")
  })

  it("resolves an incident with a structured reason and keeps exact UTC evidence available", async () => {
    let resolveBody: unknown = null
    server.use(
      http.post("/api/operator/diagnostics/incidents/inc_123/resolve", async ({ request }) => {
        resolveBody = await request.json()
        return HttpResponse.json({
          ...incidentDetail,
          incident: {
            ...incidentDetail.incident,
            lifecycle: {
              state: "resolved",
              reopened: false,
              storedDisposition: "resolved",
              updatedAtUtc: "2026-08-02T01:07:00Z",
              updatedByOperatorId: "11111111-1111-1111-1111-111111111111",
              observedThroughAtUtc: "2026-08-02T01:01:00Z",
              observedThroughEventId: "evt_1",
              snoozedUntilUtc: null,
              resolutionCode: "fixed",
              revision: 1,
            },
          },
        })
      }),
    )
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter initialEntries={["/diagnostics/logs?incident=inc_123"]}>
        <DiagnosticsLogsPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("button", { name: "Acknowledge" })).toBeInTheDocument()
    expect(document.querySelector('[title="Exact UTC evidence: 2026-08-02T01:00:00.000Z"]')).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Resolve" }))
    await user.selectOptions(screen.getByLabelText("Resolution reason"), "fixed")
    const resolveButtons = screen.getAllByRole("button", { name: "Resolve" })
    await user.click(resolveButtons[resolveButtons.length - 1])

    expect(resolveBody).toEqual({ resolutionCode: "fixed" })
    expect(await screen.findByText("Fixed")).toBeInTheDocument()
    expect(document.querySelector('[data-incident-lifecycle="resolved"]')).toHaveTextContent("Resolved")
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument()
    expect(document.querySelector('[title="Exact UTC evidence: 2026-08-02T01:07:00.000Z"]')).toBeInTheDocument()
  })

})
