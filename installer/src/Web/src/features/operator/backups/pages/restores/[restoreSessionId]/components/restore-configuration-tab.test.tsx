import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { renderWithProviders } from "@/test/render-with-providers"

import { RestoreConfigurationTab } from "./restore-configuration-tab"

const restoreSessionId = "restore-configuration-de-1"

function createWorkspace(): RestoreWorkspaceResponse {
  return {
    schemaVersion: 3,
    restoreSessionId,
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "backup-ready",
      createdAtUtc: "2026-07-03T05:00:00Z",
      updatedAtUtc: "2026-07-03T05:00:00Z",
      terminalAtUtc: null,
      lastEventAtUtc: null,
      lastErrorCode: null,
      lastErrorSummary: null,
      warningCount: 0,
      errorCount: 0,
      currentOperationId: null,
    },
    source: {
      kind: "backup-catalog",
      stackSlug: "cool-stack",
      backupId: "20260703-023108Z",
      createdAtUtc: "2026-07-03T02:31:08Z",
      sizeBytes: 1024,
      matrixHost: "matrix-cool-stack.deltabox.dev",
      elementHost: "chat-cool-stack.deltabox.dev",
      validationStatus: "available",
      validationSummary: "Catalog payload is ready.",
      catalogEntryId: "bkp-test-001",
      sourceDisplayName: "Test backup",
      sourceOriginKind: "local-captured",
      sourceDeleted: false,
    },
    target: {
      stackSlug: null,
      matrixHost: null,
      elementHost: null,
      availability: "not-selected",
      detail: "No target is reserved.",
      claims: [],
    },
    overallStatus: {
      code: "ready",
      title: "Ready to continue",
      description: "The backup is ready.",
      severity: "information",
      nextAction: null,
    },
    standardStages: [],
    advancedTools: [],
    verification: {
      status: "not-available",
      hasRun: false,
      allPassed: null,
      checkedAtUtc: null,
      checks: [],
      summary: "Not available.",
    },
    evidence: {
      categories: [],
      latestFailure: null,
      latestSuccess: null,
    },
    logs: {
      totalEvents: 0,
      warningCount: 0,
      errorCount: 0,
      latestEvent: null,
      latestWarningOrError: null,
      supportReportAvailable: false,
      supportBundleAvailable: false,
      warnings: [],
    },
    warnings: [],
    cancellation: null,
  }
}

function renderTab(workspace = createWorkspace()) {
  return renderWithProviders(
    <MemoryRouter>
      <RestoreConfigurationTab
        restoreSessionId={restoreSessionId}
        workspace={workspace}
        onViewEvidence={vi.fn()}
        onViewLogs={vi.fn()}
      />
    </MemoryRouter>,
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("RestoreConfigurationTab", () => {
  it("renders browser-authored configuration UI in German while preserving raw workspace evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    const workspace = createWorkspace()
    workspace.source.elementHost = null

    renderTab(workspace)

    expect(screen.getByText("Konfigurationsübersicht")).toBeInTheDocument()
    expect(screen.getByText("Schreibgeschützt")).toBeInTheDocument()
    expect(screen.getAllByRole("button", { name: "Sichere Konfigurationsübersicht als JSON herunterladen" })).toHaveLength(2)
    expect(screen.getByText("Nicht im Arbeitsbereich erfasst")).toBeInTheDocument()
    expect(screen.getByText("Kein Laufzeitvorgang erfasst")).toBeInTheDocument()
    expect(screen.getByText("Sicherungskatalog")).toBeInTheDocument()
    expect(screen.getByText("Lokal erfasst")).toBeInTheDocument()
    expect(screen.getByText("Bereit")).toBeInTheDocument()
    expect(screen.getAllByText("Nicht ausgewählt").length).toBeGreaterThan(0)
    expect(screen.getByText("Catalog payload is ready.")).toBeInTheDocument()
    expect(screen.getByText("matrix-cool-stack.deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("bkp-test-001")).toBeInTheDocument()
    expect(screen.queryByText("Configuration overview")).not.toBeInTheDocument()
  })

  it("localises known status and count presentations without rewriting unknown recorded evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    const workspace = createWorkspace()
    workspace.target.stackSlug = "restored-stack"
    workspace.target.matrixHost = "matrix-restored-stack.deltabox.dev"
    workspace.target.elementHost = "chat-restored-stack.deltabox.dev"
    workspace.target.availability = "available"
    workspace.target.claims = [
      {
        resourceType: "custom-runtime-route",
        resourceValue: "matrix-restored-stack.deltabox.dev",
        status: "active",
        claimedAtUtc: "2026-07-03T05:30:00Z",
        releasedAtUtc: null,
        releaseReason: null,
      },
      {
        resourceType: "custom-element-route",
        resourceValue: "chat-restored-stack.deltabox.dev",
        status: "released",
        claimedAtUtc: "2026-07-03T05:31:00Z",
        releasedAtUtc: "2026-07-03T05:32:00Z",
        releaseReason: "manual-review",
      },
    ]
    workspace.verification = {
      status: "available",
      hasRun: true,
      allPassed: true,
      checkedAtUtc: "2026-07-03T05:33:00Z",
      summary: "Raw verification summary from HostAgent.",
      checks: [
        {
          code: "restore.route.matrix",
          title: "Raw Matrix route proof",
          status: "available",
        },
      ],
    }

    renderTab(workspace)

    expect(screen.getByText("2 Ressourcenreservierungen erfasst")).toBeInTheDocument()
    expect(screen.getByText("Zielreservierungen")).toBeInTheDocument()
    expect(screen.getAllByText("Verfügbar").length).toBeGreaterThan(0)
    expect(screen.getAllByText("Bestanden").length).toBeGreaterThan(0)
    expect(screen.getAllByText(/^Reserviert .* UTC$/)).toHaveLength(2)
    expect(screen.getAllByText(/^Freigegeben .* UTC$/)).toHaveLength(1)
    expect(screen.getByText("custom-runtime-route")).toBeInTheDocument()
    expect(screen.getByText("manual-review")).toBeInTheDocument()
    expect(screen.getByText("Raw verification summary from HostAgent.")).toBeInTheDocument()
    expect(screen.getByText("Raw Matrix route proof")).toBeInTheDocument()
  })
})
