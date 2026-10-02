import { fireEvent, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { renderWithProviders } from "@/test/render-with-providers"

import { RestoreEvidenceTab } from "./restore-evidence-tab"

const mocks = vi.hoisted(() => ({
  useGenerateRestoreSupportReport: vi.fn(),
}))

vi.mock("@/features/operator/backups/hooks/use-restore-workspace", () => ({
  useGenerateRestoreSupportReport: mocks.useGenerateRestoreSupportReport,
}))

const restoreSessionId = "restore-evidence-de-1"

function createWorkspace(): RestoreWorkspaceResponse {
  const latestFailure = {
    code: "restore.validation.proof",
    category: "validation",
    title: "Raw validation proof from HostAgent",
    status: "failed",
    occurredAtUtc: "2026-07-03T05:30:00Z",
    eventCode: "restore.validation.future-proof",
    operationId: "op-restore-validation-1",
    stage: "validation-stage",
    description: "Raw evidence description from HostAgent.",
  }

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
      categories: [
        {
          code: "validation",
          title: "Raw validation category from HostAgent",
          status: "warning",
          itemCount: 1,
          latestOccurredAtUtc: "2026-07-03T05:30:00Z",
          items: [latestFailure],
        },
      ],
      latestFailure,
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

function renderTab(workspace = createWorkspace(), onViewLogs = vi.fn()) {
  renderWithProviders(
    <RestoreEvidenceTab
      restoreSessionId={restoreSessionId}
      workspace={workspace}
      onViewLogs={onViewLogs}
    />,
  )

  return { onViewLogs }
}

function mockSupportReport(overrides: Partial<{
  mutateAsync: ReturnType<typeof vi.fn>
  isError: boolean
  isPending: boolean
  error: unknown
}> = {}) {
  mocks.useGenerateRestoreSupportReport.mockReturnValue({
    mutateAsync: vi.fn(),
    isError: false,
    isPending: false,
    error: null,
    ...overrides,
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  mockSupportReport()
})

afterEach(() => {
  window.localStorage.clear()
})

describe("RestoreEvidenceTab", () => {
  it("renders browser-authored Evidence UI in German while preserving raw recorded evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const { onViewLogs } = renderTab()

    expect(screen.getByText("Nachweisübersicht")).toBeInTheDocument()
    expect(screen.getByText("1 Nachweis für diesen Wiederherstellungsversuch ist zur Prüfung verfügbar.")).toBeInTheDocument()
    expect(screen.getByText("Letztes Ergebnis")).toBeInTheDocument()
    expect(screen.getAllByText("Nachweise").length).toBeGreaterThan(0)
    expect(screen.getByText("Warnung")).toBeInTheDocument()
    const validationCategoryButton = screen.getByRole("button", {
      name: /Raw validation category from HostAgent/,
    })
    expect(validationCategoryButton).toBeInTheDocument()
    expect(screen.getByText("Raw evidence description from HostAgent.")).toBeInTheDocument()
    expect(screen.getByText("Zeitstempel der Nachweise werden in UTC angezeigt")).toBeInTheDocument()
    expect(screen.getAllByRole("button", { name: "Supportbericht erstellen" })).toHaveLength(2)
    expect(screen.getAllByRole("button", { name: "Protokolle öffnen" })).toHaveLength(1)
    expect(screen.queryByText("Evidence summary")).not.toBeInTheDocument()

    fireEvent.click(validationCategoryButton)

    expect(screen.getByText("Ausblenden")).toBeInTheDocument()
    expect(screen.getByText("Fehlgeschlagen")).toBeInTheDocument()
    expect(screen.getAllByText("Raw validation proof from HostAgent").length).toBeGreaterThan(0)
    expect(screen.getByText("restore.validation.future-proof")).toBeInTheDocument()
    expect(screen.getByText("op-restore-validation-1")).toBeInTheDocument()
    expect(screen.getByText("validation-stage")).toBeInTheDocument()
    expect(screen.getByText("restore.validation.proof")).toBeInTheDocument()

    const openLogsButtons = screen.getAllByRole("button", { name: "Protokolle öffnen" })
    expect(openLogsButtons).toHaveLength(2)
    fireEvent.click(openLogsButtons[0])
    expect(onViewLogs).toHaveBeenCalledTimes(1)
  })

  it("retains unknown future evidence identifiers and statuses verbatim", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const workspace = createWorkspace()
    workspace.evidence.categories[0] = {
      ...workspace.evidence.categories[0],
      code: "future-category",
      status: "custom-evidence-status",
      title: "Raw future evidence category",
      items: [
        {
          ...workspace.evidence.categories[0].items[0],
          status: "custom-evidence-status",
          code: "restore.future.evidence-code",
          eventCode: "restore.future.event-code",
          stage: "future-stage",
          title: "Raw future evidence title",
          description: "Raw future evidence description.",
        },
      ],
    }

    renderTab(workspace)

    expect(screen.getByText("custom-evidence-status")).toBeInTheDocument()
    const futureCategoryButton = screen.getByRole("button", {
      name: /Raw future evidence category/,
    })
    expect(futureCategoryButton).toBeInTheDocument()

    fireEvent.click(futureCategoryButton)

    expect(screen.getByText("restore.future.evidence-code")).toBeInTheDocument()
    expect(screen.getByText("restore.future.event-code")).toBeInTheDocument()
    expect(screen.getByText("future-stage")).toBeInTheDocument()
    expect(screen.getAllByText("Raw future evidence description.")).toHaveLength(2)
  })

  it("localises the empty and support-report failure states while retaining raw technical detail", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const workspace = createWorkspace()
    workspace.evidence = {
      categories: [],
      latestFailure: null,
      latestSuccess: null,
    }
    mockSupportReport({
      isError: true,
      error: new Error("Raw support report transport detail."),
    })

    renderTab(workspace)

    expect(screen.getByText("Noch keine Nachweise erfasst")).toBeInTheDocument()
    expect(screen.getByText("Supportbericht konnte nicht erstellt werden")).toBeInTheDocument()
    expect(screen.getByText("Technische Details")).toBeInTheDocument()
    expect(screen.getByText("Raw support report transport detail.")).toBeInTheDocument()
  })
})
