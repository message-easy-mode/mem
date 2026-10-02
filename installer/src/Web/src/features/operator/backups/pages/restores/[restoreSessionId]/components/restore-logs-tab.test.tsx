import { fireEvent, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type {
  RestoreLogPage,
  RestoreLogSummary,
} from "@/features/operator/backups/api/types/restore-logs.types"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { renderWithProviders } from "@/test/render-with-providers"

import { RestoreLogsTab } from "./restore-logs-tab"

const mocks = vi.hoisted(() => ({
  useRestoreLogs: vi.fn(),
  useGenerateRestoreSupportReport: vi.fn(),
}))

vi.mock("@/features/operator/backups/hooks/use-restore-workspace", () => ({
  useRestoreLogs: mocks.useRestoreLogs,
  useGenerateRestoreSupportReport: mocks.useGenerateRestoreSupportReport,
}))

const restoreSessionId = "restore-logs-de-1"

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
    logs: createSummary(),
    warnings: [],
    cancellation: null,
  }
}

function createSummary(): RestoreWorkspaceResponse["logs"] {
  return {
    totalEvents: 2,
    warningCount: 1,
    errorCount: 1,
    latestEvent: null,
    latestWarningOrError: null,
    supportReportAvailable: false,
    supportBundleAvailable: false,
    warnings: [],
  }
}

function createLogPage(): RestoreLogPage {
  const warningEvent = {
    schemaVersion: 1,
    eventId: "evt-warning-1",
    timestampUtc: "2026-07-03T05:30:00Z",
    restoreSessionId,
    operationId: "op-private-test-1",
    stage: "private-test",
    severity: "warning",
    eventCode: "restore.private-test.started",
    message: "Private staging started safely.",
    details: {
      containerName: "mem-restore-private-test",
      accessToken: "must-not-be-visible",
    },
  }
  const errorEvent = {
    schemaVersion: 1,
    eventId: "evt-error-1",
    timestampUtc: "2026-07-03T05:31:00Z",
    restoreSessionId,
    operationId: "op-private-test-1",
    stage: "private-test",
    severity: "error",
    eventCode: "restore.private-test.failed",
    message: "Raw HostAgent failure summary.",
    details: {
      failureCode: "private-test-health-failed",
    },
  }

  const summary: RestoreLogSummary = {
    totalEvents: 2,
    warningCount: 1,
    errorCount: 1,
    latestEvent: errorEvent,
    latestWarningOrError: errorEvent,
  }

  return {
    restoreSessionId,
    page: 1,
    pageSize: 20,
    totalEvents: 2,
    totalPages: 1,
    summary,
    events: [warningEvent, errorEvent],
    warnings: [],
  }
}

function renderTab(workspace = createWorkspace()) {
  return renderWithProviders(
    <RestoreLogsTab restoreSessionId={restoreSessionId} workspace={workspace} />,
  )
}

function mockSuccessfulLogs(page = createLogPage()) {
  mocks.useRestoreLogs.mockReturnValue({
    data: page,
    error: null,
    isError: false,
    isFetching: false,
    isLoading: false,
  })
  mocks.useGenerateRestoreSupportReport.mockReturnValue({
    mutateAsync: vi.fn(),
    isError: false,
    isPending: false,
    error: null,
  })
}

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  window.localStorage.clear()
})

describe("RestoreLogsTab", () => {
  it("renders browser-authored Logs UI in German while retaining raw log records", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mockSuccessfulLogs()

    renderTab()

    expect(screen.getByText("Aktueller Status")).toBeInTheDocument()
    expect(screen.getByText("Zusammenfassung nach Schweregrad")).toBeInTheDocument()
    expect(screen.getByLabelText("Wiederherstellungsprotokolle durchsuchen")).toBeInTheDocument()
    expect(screen.getByRole("option", { name: "Alle Phasen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Filter löschen" })).toBeDisabled()
    expect(screen.getByText("Strukturierte Protokolle für diesen Wiederherstellungsversuch. Sie sind keine vollständigen Host-Protokolle.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Sichtbare Einträge kopieren" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Supportbericht erstellen" })).toBeInTheDocument()
    expect(screen.getByText("Zeit (UTC)")).toBeInTheDocument()
    expect(screen.getByText("Ereigniscode")).toBeInTheDocument()
    expect(screen.getAllByText("Warnung").length).toBeGreaterThan(0)
    expect(screen.getByText("restore.private-test.started")).toBeInTheDocument()
    expect(screen.getAllByText("private-test").length).toBeGreaterThan(0)
    expect(screen.getAllByText("Private staging started safely.").length).toBeGreaterThan(0)
    expect(screen.queryByText("Latest status")).not.toBeInTheDocument()

    fireEvent.click(screen.getAllByText("Private staging started safely.")[0])

    expect(screen.getByText("Ereignisdetails")).toBeInTheDocument()
    expect(screen.getByText("Sicherer Kontext")).toBeInTheDocument()
    expect(screen.getByText("containerName")).toBeInTheDocument()
    expect(screen.getByText("mem-restore-private-test")).toBeInTheDocument()
    expect(screen.getByText("accessToken")).toBeInTheDocument()
    expect(screen.getByText("[redacted]")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Details kopieren" })).toBeInTheDocument()
  })

  it("retains raw unknown future log evidence instead of translating or discarding it", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const page = createLogPage()
    page.events = [
      {
        ...page.events[0],
        severity: "custom-severity",
        eventCode: "restore.future-step.not-documented",
        message: "Raw future event detail from HostAgent.",
      },
    ]
    page.totalEvents = 1
    page.summary = {
      totalEvents: 1,
      warningCount: 0,
      errorCount: 0,
      latestEvent: page.events[0],
      latestWarningOrError: null,
    }
    mockSuccessfulLogs(page)

    renderTab()

    expect(screen.getByText("custom-severity")).toBeInTheDocument()
    expect(screen.getByText("restore.future-step.not-documented")).toBeInTheDocument()
    expect(screen.getAllByText("Raw future event detail from HostAgent.").length).toBeGreaterThan(0)
  })

  it("localises logs and support-report failures while retaining raw technical detail", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mocks.useRestoreLogs.mockReturnValue({
      data: undefined,
      error: new Error("Raw HostAgent log transport detail."),
      isError: true,
      isFetching: false,
      isLoading: false,
    })
    mocks.useGenerateRestoreSupportReport.mockReturnValue({
      mutateAsync: vi.fn(),
      error: new Error("Raw support report transport detail."),
      isError: true,
      isPending: false,
    })

    renderTab()

    expect(screen.getByText("Protokolle konnten nicht geladen werden")).toBeInTheDocument()
    expect(screen.getByText("Supportbericht konnte nicht erstellt werden")).toBeInTheDocument()
    expect(screen.getAllByText("Technische Details")).toHaveLength(2)
    expect(screen.getByText("Raw HostAgent log transport detail.")).toBeInTheDocument()
    expect(screen.getByText("Raw support report transport detail.")).toBeInTheDocument()
  })
})
