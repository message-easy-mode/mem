import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { RuntimeContextDetailsProvider } from "@/features/runtime-context/components/runtime-context-details"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { DiagnosticsOverviewResponse } from "./api/diagnostics.types"
import { DiagnosticsPage } from "./diagnostics-page"

const overviewResponse: DiagnosticsOverviewResponse = {
  schemaVersion: 1,
  generatedAtUtc: "2026-08-03T01:05:00Z",
  status: "ready",
  counts: {
    information: 4,
    warning: 0,
    error: 0,
    critical: 0,
    incidentCount: 0,
    eventCount: 4,
  },
  loggingHealth: {
    observedAtUtc: "2026-08-03T01:05:00Z",
    status: "ready",
    localRecorder: {
      enabled: true,
      status: "ready",
      persistentRecorderConfigured: true,
      persistentRecorderActive: true,
      persistentFilePath: null,
      lastFileWriteAtUtc: "2026-08-03T01:04:00Z",
      retainedFileCount: 2,
      retainedBytes: 1024,
      serilogSelfLogMessageCount: 0,
      warningCode: null,
    },
    safeEventStore: {
      enabled: true,
      status: "ready",
      lastWriteAtUtc: "2026-08-03T01:04:00Z",
      lastReadAtUtc: null,
      storedEventCount: 4,
      droppedEventCount: 0,
      malformedLineCount: 0,
      lastWriteErrorCode: null,
      lastReadWarningCode: null,
      lastRetentionRunAtUtc: null,
      lastRetentionDeletedFileCount: 0,
      lastRetentionDeletedBytes: 0,
      lastRetentionErrorCode: null,
      hasEverRecordedEvent: true,
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
    capabilities: {
      canReadTechnicalEvents: true,
      canGenerateSupportReport: true,
      canViewOwnerHealthFacts: true,
      canReadDockerEvidence: true,
      canVerifyPipeline: true,
      canOpenPortainer: true,
    },
    partial: false,
    warnings: [],
  },
  capabilities: {
    canReadTechnicalEvents: true,
    canGenerateSupportReport: true,
    canViewOwnerHealthFacts: true,
    canReadDockerEvidence: true,
    canVerifyPipeline: true,
      canOpenPortainer: true,
  },
  activeContext: null,
  partial: false,
  truncated: false,
  warnings: [],
}

function overview(overrides: Partial<DiagnosticsOverviewResponse> = {}): DiagnosticsOverviewResponse {
  return {
    ...overviewResponse,
    ...overrides,
  }
}

function renderPage(initialEntry = "/diagnostics") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <RuntimeContextDetailsProvider>
        <DiagnosticsPage />
      </RuntimeContextDetailsProvider>
    </MemoryRouter>,
  )
}

describe("DiagnosticsPage command centre", () => {
  beforeEach(() => {
    server.use(
      http.get(
        "/api/operator/diagnostics/overview",
        () => HttpResponse.json(overviewResponse),
      ),
      http.get(
        "/api/operator/runtime-context",
        () => HttpResponse.json(createRuntimeContext()),
      ),
    )
  })

  afterEach(() => {
    window.localStorage.clear()
  })

  it("renders one calm health strip and only implemented capability cards", async () => {
    renderPage()

    expect(screen.getByRole("heading", { name: "Diagnostics" })).toBeInTheDocument()
    expect(await screen.findByText("Diagnostics health")).toBeInTheDocument()
    expect(screen.getByText(/Last updated .* UTC/)).toBeInTheDocument()
    expect(await screen.findByText("Containerized production")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Control Plane runtime" })).toBeInTheDocument()
    expect(screen.getByText("MEM-native diagnostics are ready.")).toBeInTheDocument()
    expect(screen.getByText("No recent attention")).toBeInTheDocument()

    expect(screen.getByRole("heading", { name: "Incidents and technical events" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Logging health" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Seq advanced logging" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Advanced container diagnostics" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Verify diagnostics pipeline" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Runtime reconciliation" })).toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Support and reports" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Preflight checks" })).not.toBeInTheDocument()

    for (const tone of [
      "incidents",
      "logging",
      "seq",
      "containers",
      "verification",
      "reconciliation",
    ]) {
      expect(document.querySelector(`[data-capability-icon="${tone}"]`)).toBeInTheDocument()
    }
    expect(document.querySelectorAll('img[src="/brands/seq-mark.svg"]').length).toBeGreaterThanOrEqual(2)
    expect(document.querySelector("[data-diagnostics-capability-grid]")).toHaveClass("md:grid-cols-2")
    expect(document.querySelector("[data-diagnostics-capability-grid]")).toHaveClass("2xl:grid-cols-4")
    expect(document.querySelector("[data-diagnostics-capability-grid]")).not.toHaveClass("xl:grid-cols-4")
    expect(document.querySelectorAll('[data-state-tone="ready"]').length).toBeGreaterThan(0)
    expect(document.querySelectorAll('[data-health-tone="ready"]').length).toBeGreaterThan(0)
    expect(screen.getByRole("link", { name: "Open incidents" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
    expect(screen.getByRole("link", { name: "Open technical events" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=events",
    )
    expect(screen.getByRole("link", { name: "Open logging health" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=health",
    )
    expect(screen.getByRole("link", { name: "Manage Seq" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(screen.getByRole("link", { name: "Open container diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/portainer",
    )
    expect(screen.queryByRole("link", { name: "Open incident evidence" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Run verification" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open reconciliation" })).toHaveAttribute(
      "href",
      "/diagnostics/runtime-reconciliation",
    )

    expect(screen.getByRole("link", { name: "Incidents and technical events" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
    expect(screen.getByRole("link", { name: "Logging health" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=health",
    )
    expect(screen.getByRole("link", { name: "Seq advanced logging" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(screen.getByRole("link", { name: "Advanced container diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/portainer",
    )
    expect(screen.queryByRole("link", { name: "Verify diagnostics pipeline" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Runtime reconciliation" })).toHaveAttribute(
      "href",
      "/diagnostics/runtime-reconciliation",
    )

    expect(screen.queryByText("Coming later")).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Docker" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Host" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Raw checks" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Runtime health" })).not.toBeInTheDocument()
  })

  it("renders the attention count with real interpolation in English and German", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        status: "attention",
        counts: {
          ...overviewResponse.counts,
          warning: 2,
          incidentCount: 2,
          eventCount: 6,
        },
      }))),
    )

    const english = renderPage()

    expect(await screen.findByText("2 need attention")).toBeInTheDocument()
    expect(screen.getByText("2 recent warnings or failures need attention.")).toBeInTheDocument()
    expect(screen.queryByText("{count} need attention")).not.toBeInTheDocument()
    expect(screen.queryByText("{count} recent warnings or failures need attention.")).not.toBeInTheDocument()

    english.unmount()
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderPage()

    expect(await screen.findByText("2 benötigen Aufmerksamkeit")).toBeInTheDocument()
    expect(screen.getByText("2 aktuelle Warnungen oder Störungen benötigen Aufmerksamkeit.")).toBeInTheDocument()
    expect(screen.queryByText("{count} benötigen Aufmerksamkeit")).not.toBeInTheDocument()
    expect(screen.queryByText("{count} aktuelle Warnungen oder Störungen benötigen Aufmerksamkeit.")).not.toBeInTheDocument()
  })

  it("keeps incident-owned container evidence available when Portainer handoff is unavailable", async () => {
    const capabilities = {
      ...overviewResponse.capabilities,
      canOpenPortainer: false,
      canReadDockerEvidence: true,
    }
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        capabilities,
        loggingHealth: {
          ...overviewResponse.loggingHealth,
          capabilities,
        },
      }))),
    )

    renderPage()

    expect(await screen.findByRole("link", { name: "Open incident evidence" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
    expect(screen.queryByRole("link", { name: "Open container diagnostics" })).not.toBeInTheDocument()
  })

  it("uses the server-selected attention context and links to both workspace and incident", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        status: "attention",
        counts: {
          ...overviewResponse.counts,
          error: 1,
          incidentCount: 1,
          eventCount: 5,
        },
        activeContext: {
          state: "attention",
          kind: "migration",
          code: "workflow_requires_attention",
          feature: "migration",
          stage: "production-verification",
          resourceName: "Family migration",
          summary: "Production verification failed.",
          updatedAtUtc: "2026-08-03T01:04:00Z",
          workspaceHref: "/migrations/mig_123",
          incidentId: "inc_migration_1",
          incidentHref: "/diagnostics/logs?incident=inc_migration_1",
        },
      }))),
    )

    renderPage()

    expect(await screen.findByText("A workflow needs attention")).toBeInTheDocument()
    expect(screen.getByRole("alert")).toHaveAttribute("data-active-context-state", "attention")
    expect(screen.getByText("Production verification failed.")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: /Open owning workspace/i })).toHaveAttribute(
      "href",
      "/migrations/mig_123",
    )
    expect(screen.getByRole("link", { name: /^Open incident$/i })).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_migration_1",
    )
  })

  it("renders a running migration context without inventing an incident", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        activeContext: {
          state: "running",
          kind: "migration",
          code: "migration_active",
          feature: "migration",
          stage: null,
          resourceName: "Current migration",
          summary: null,
          updatedAtUtc: "2026-08-03T01:04:00Z",
          workspaceHref: "/migrations/mig_active",
          incidentId: null,
          incidentHref: null,
        },
      }))),
    )

    renderPage()

    expect(await screen.findByText("Migration in progress")).toBeInTheDocument()
    expect(screen.getByRole("status")).toHaveAttribute("data-active-context-state", "running")
    expect(screen.getByRole("link", { name: /Open owning workspace/i })).toHaveAttribute(
      "href",
      "/migrations/mig_active",
    )
    expect(screen.queryByRole("link", { name: /^Open incident$/i })).not.toBeInTheDocument()
  })

  it("links an active Seq operation to the dedicated Seq workspace", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        activeContext: {
          state: "running",
          kind: "seq",
          code: "seq_operation_active",
          feature: "logging",
          stage: "executing",
          resourceName: "Seq",
          summary: null,
          updatedAtUtc: "2026-08-03T01:04:00Z",
          workspaceHref: "/diagnostics/seq",
          incidentId: null,
          incidentHref: null,
        },
      }))),
    )

    renderPage()

    expect(await screen.findByText("Seq operation in progress")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: /Open owning workspace/i })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(screen.queryByRole("link", { name: /^Open incident$/i })).not.toBeInTheDocument()
  })

  it("does not render an active-context banner when the server returns none", async () => {
    renderPage()

    await screen.findByText("Diagnostics health")
    expect(screen.queryByText("Operational work in progress")).not.toBeInTheDocument()
    expect(screen.queryByText("A workflow needs attention")).not.toBeInTheDocument()
  })

  it("preserves the last successful overview when a manual refresh fails", async () => {
    let requestCount = 0
    server.use(
      http.get("/api/operator/diagnostics/overview", () => {
        requestCount += 1
        return requestCount === 1
          ? HttpResponse.json(overviewResponse)
          : HttpResponse.json({ title: "Unavailable" }, { status: 503 })
      }),
    )

    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText("Diagnostics health")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Refresh" }))

    expect(await screen.findByText("Showing the last successful data")).toBeInTheDocument()
    expect(screen.getByText("Diagnostics health")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Logging health" })).toBeInTheDocument()
  })

  it("distinguishes a partial overview and degraded evidence components", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        status: "degraded",
        partial: true,
        loggingHealth: {
          ...overviewResponse.loggingHealth,
          status: "degraded",
          partial: true,
          safeEventStore: {
            ...overviewResponse.loggingHealth.safeEventStore,
            status: "unavailable",
          },
        },
      }))),
    )

    renderPage()

    expect(await screen.findByText("Some diagnostic evidence is currently partial.")).toBeInTheDocument()
    expect(screen.getByText("Browser-safe event storage is unavailable or degraded.")).toBeInTheDocument()
    expect(screen.getAllByText("Degraded").length).toBeGreaterThan(0)
    expect(document.querySelectorAll('[data-health-tone="degraded"]').length).toBeGreaterThan(0)
    expect(document.querySelectorAll('[data-state-tone="degraded"]').length).toBeGreaterThan(0)
  })

  it("projects Auditor-safe cards without owner or operator actions", async () => {
    const restrictedCapabilities = {
      canReadTechnicalEvents: false,
      canGenerateSupportReport: false,
      canViewOwnerHealthFacts: false,
      canReadDockerEvidence: false,
      canVerifyPipeline: false,
      canOpenPortainer: false,
    }
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        capabilities: restrictedCapabilities,
        loggingHealth: {
          ...overviewResponse.loggingHealth,
          capabilities: restrictedCapabilities,
        },
      }))),
    )

    renderPage()

    await screen.findByText("Diagnostics health")
    expect(screen.queryByRole("link", { name: "Open technical events" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open incident evidence" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Support and reports" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Preflight checks" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Run verification" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Manage Seq" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Review Seq health" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=health",
    )
    expect(screen.getByText("Platform Owner only")).toBeInTheDocument()
  })

  it("shows owner-authorized self-test and an authoritative Seq URL only when returned", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        loggingHealth: {
          ...overviewResponse.loggingHealth,
          seq: {
            ...overviewResponse.loggingHealth.seq,
            status: "ready",
            configured: true,
            sinkEnabled: true,
            managementEnabled: true,
            reachable: true,
            serverUrl: "https://seq.internal.example/",
          },
        },
      }))),
    )

    renderPage()

    expect(await screen.findByRole("button", { name: "Run verification" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Manage Seq" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(screen.getByRole("link", { name: /Open Seq/i })).toHaveAttribute(
      "href",
      "https://seq.internal.example/",
    )
    expect(screen.getByRole("link", { name: /Open Seq/i })).toHaveAttribute("data-variant", "default")
  })

  it("rejects non-http Seq and active-context links in the browser", async () => {
    server.use(
      http.get("/api/operator/diagnostics/overview", () => HttpResponse.json(overview({
        loggingHealth: {
          ...overviewResponse.loggingHealth,
          seq: {
            ...overviewResponse.loggingHealth.seq,
            configured: true,
            sinkEnabled: true,
            serverUrl: "javascript:alert(1)",
          },
        },
        activeContext: {
          state: "attention",
          kind: "runtime",
          code: "incident_requires_attention",
          feature: "runtime",
          stage: null,
          resourceName: null,
          summary: "A safe incident summary.",
          updatedAtUtc: "2026-08-03T01:04:00Z",
          workspaceHref: "https://untrusted.example.test/workspace",
          incidentId: "inc_unsafe",
          incidentHref: "//untrusted.example.test/incident",
        },
      }))),
    )

    renderPage()

    expect(await screen.findByText("A safe incident summary.")).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: /Open Seq/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: /Open owning workspace/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: /^Open incident$/i })).not.toBeInTheDocument()
  })

  it("renders the command centre in German without exposing retired future cards", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderPage()

    expect(await screen.findByText("Diagnosezustand")).toBeInTheDocument()
    expect(screen.getByText(/Zuletzt aktualisiert .* UTC/)).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Vorfälle und technische Ereignisse" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Erweiterte Seq-Protokollierung" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Erweiterte Seq-Protokollierung" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(screen.getByRole("heading", { name: "Erweiterte Containerdiagnose" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Seq verwalten" })).toHaveAttribute("href", "/diagnostics/seq")
    expect(screen.queryByRole("link", { name: "Vorfall für Supportbericht auswählen" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Vorprüfung öffnen" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Support und Berichte" })).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "Vorprüfungen" })).not.toBeInTheDocument()
    expect(screen.queryByText("Später verfügbar")).not.toBeInTheDocument()
  })

  it("uses one overview request for the initial command-centre render", async () => {
    let requestCount = 0
    server.use(
      http.get("/api/operator/diagnostics/overview", () => {
        requestCount += 1
        return HttpResponse.json(overviewResponse)
      }),
    )

    renderPage()

    await screen.findByText("Diagnostics health")
    expect(requestCount).toBe(1)
  })

  it("keeps the safe deep-linked incident summary above the command centre", async () => {
    server.use(
      http.get(
        "/api/operator/diagnostics/incidents/inc_123",
        () => HttpResponse.json({
          incident: {
            incidentId: "inc_123",
            severity: "error",
            eventCode: "docker_operation_failed",
            feature: "migration",
            stage: "private-staging",
            message: "The private Synapse staging container exited before readiness.",
            firstSeenAtUtc: "2026-08-03T01:00:00Z",
            lastSeenAtUtc: "2026-08-03T01:01:00Z",
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
          },
          capabilities: overviewResponse.capabilities,
          relatedEventCount: 3,
          technicalEvents: null,
          operations: [],
          truncated: false,
          warnings: [],
        }),
      ),
    )

    renderPage("/diagnostics?incidentId=inc_123")

    expect(
      await screen.findByText(
        "The private Synapse staging container exited before readiness.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open owning workspace" })).toHaveAttribute(
      "href",
      "/migrations/mig_123",
    )
    expect(screen.getByText("Diagnostics health")).toBeInTheDocument()
  })

  it("shows Portainer, docker logs, and CLEF fallbacks when the API is unavailable", async () => {
    server.use(
      http.get(
        "/api/operator/diagnostics/overview",
        () => HttpResponse.json(
          { title: "Unavailable", detail: "The API is unavailable." },
          { status: 503 },
        ),
      ),
    )

    renderPage()

    expect(await screen.findByText("Control-plane diagnostics are unavailable")).toBeInTheDocument()
    expect(screen.getByText("sudo docker logs --tail 500 mem-control-plane")).toBeInTheDocument()
    expect(screen.getByText("/data/logs/control-plane/mem-control-plane-.clef")).toBeInTheDocument()
  })
})
