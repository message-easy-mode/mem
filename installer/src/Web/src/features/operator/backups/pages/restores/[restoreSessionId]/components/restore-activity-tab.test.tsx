import { fireEvent, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type { RestoreLogPage } from "@/features/operator/backups/api/types/restore-logs.types"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"
import { renderWithProviders } from "@/test/render-with-providers"

import { RestoreActivityTab } from "./restore-activity-tab"

const mocks = vi.hoisted(() => ({
  useRestoreLogs: vi.fn(),
}))

vi.mock("@/features/operator/backups/hooks/use-restore-workspace", () => ({
  useRestoreLogs: mocks.useRestoreLogs,
}))

const restoreSessionId = "restore-activity-de-1"

function createWorkspace(): RestoreWorkspaceResponse {
  return {
    schemaVersion: 3,
    restoreSessionId,
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "backup-ready",
      createdAtUtc: "2026-07-03T05:00:00Z",
      updatedAtUtc: "2026-07-03T05:30:00Z",
      terminalAtUtc: null,
      lastEventAtUtc: null,
      lastErrorCode: null,
      lastErrorSummary: null,
      warningCount: 1,
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
      title: "Raw overall status from HostAgent",
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
      totalEvents: 1,
      warningCount: 1,
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

function createLogPage(): RestoreLogPage {
  const event = {
    schemaVersion: 1,
    eventId: "evt-activity-warning-1",
    timestampUtc: "2026-07-03T05:30:00Z",
    restoreSessionId,
    operationId: "op-activity-1",
    stage: "private-test-stage",
    severity: "warning",
    eventCode: "restore.future-activity.warning",
    message: "Raw timeline detail from HostAgent.",
    details: {
      diagnosticReference: "raw-reference-1",
    },
  }

  return {
    restoreSessionId,
    page: 1,
    pageSize: 100,
    totalEvents: 1,
    totalPages: 1,
    summary: {
      totalEvents: 1,
      warningCount: 1,
      errorCount: 0,
      latestEvent: event,
      latestWarningOrError: event,
    },
    events: [event],
    warnings: [],
  }
}

function createKnownPrivateTestLogPage(): RestoreLogPage {
  const page = createLogPage()
  const event = {
    ...page.events[0],
    eventId: "evt-activity-private-test-passed-1",
    operationId: "op-private-test-passed-1",
    severity: "information",
    eventCode: "restore.private-test.passed",
    message: "Raw private test detail from HostAgent.",
  }

  return {
    ...page,
    summary: {
      ...page.summary,
      warningCount: 0,
      latestEvent: event,
      latestWarningOrError: null,
    },
    events: [event],
    warnings: [],
  }
}

function mockSuccessfulLogs(page = createLogPage()) {
  mocks.useRestoreLogs.mockReturnValue({
    data: page,
    error: null,
    isError: false,
    isFetching: false,
    isLoading: false,
  })
}

function renderTab(workspace = createWorkspace(), onViewLogs = vi.fn()) {
  renderWithProviders(
    <RestoreActivityTab
      restoreSessionId={restoreSessionId}
      workspace={workspace}
      onViewLogs={onViewLogs}
    />,
  )

  return { onViewLogs }
}

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  window.localStorage.clear()
})

describe("RestoreActivityTab", () => {
  it("renders browser-authored Activity UI in German while preserving raw timeline evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mockSuccessfulLogs()
    const { onViewLogs } = renderTab()

    expect(screen.getByText("Aktivitätszeitleiste der Wiederherstellung")).toBeInTheDocument()
    expect(screen.getByLabelText("Aktivitätszeitleiste der Wiederherstellung filtern")).toBeInTheDocument()
    expect(screen.getByRole("option", { name: "Alle Meilensteine" })).toBeInTheDocument()
    expect(screen.getByRole("option", { name: "Nach Phase gruppieren" })).toBeInTheDocument()
    expect(screen.getAllByRole("button", { name: "Aktivitäts-JSON herunterladen" })).toHaveLength(2)
    expect(screen.getByText("Aktivitätsübersicht")).toBeInTheDocument()
    expect(screen.getByText("Wichtige Punkte")).toBeInTheDocument()
    expect(screen.getByText("Raw overall status from HostAgent")).toBeInTheDocument()
    expect(screen.getAllByText("Warnung").length).toBeGreaterThan(0)
    expect(screen.getAllByText("Raw timeline detail from HostAgent.").length).toBeGreaterThan(0)
    expect(screen.getAllByText("03. Juli 2026", { exact: false }).length).toBeGreaterThan(0)
    expect(screen.queryByText("Restore activity timeline")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: /restore\.future-activity\.warning/ })).toBeInTheDocument()
    expect(screen.queryByText("Restore · Future Activity · Warning")).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: /restore\.future-activity\.warning/ }))

    expect(screen.getByText("Ereigniscode")).toBeInTheDocument()
    expect(screen.getByText("Vorgangs-ID")).toBeInTheDocument()
    expect(screen.getByText("Datensatzquelle")).toBeInTheDocument()
    expect(screen.getAllByText("restore.future-activity.warning").length).toBeGreaterThan(0)
    expect(screen.getAllByText("private-test-stage").length).toBeGreaterThan(0)
    expect(screen.getByText("op-activity-1")).toBeInTheDocument()
    expect(screen.getByText("Strukturiertes Wiederherstellungsprotokoll")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Vorgangs-ID kopieren" })).toBeInTheDocument()

    fireEvent.change(
      screen.getByLabelText("Aktivitätszeitleiste der Wiederherstellung gruppieren"),
      { target: { value: "stage" } },
    )
    expect(screen.getAllByText("private-test-stage").length).toBeGreaterThan(0)

    fireEvent.click(screen.getAllByRole("button", { name: "Wiederherstellungsprotokolle öffnen" })[0])
    expect(onViewLogs).toHaveBeenCalledTimes(1)
  })

  it("renders a recognised event code with German Web wording while preserving raw activity evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mockSuccessfulLogs(createKnownPrivateTestLogPage())
    renderTab()

    expect(screen.getByText("Privater Test bestanden")).toBeInTheDocument()
    expect(screen.getByText("Raw private test detail from HostAgent.")).toBeInTheDocument()
    expect(screen.queryByText("Private Test passed")).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: /Privater Test bestanden/ }))

    expect(screen.getAllByText("restore.private-test.passed").length).toBeGreaterThan(0)
    expect(screen.getByText("Raw private test detail from HostAgent.")).toBeInTheDocument()
    expect(screen.getByText("Strukturiertes Wiederherstellungsprotokoll")).toBeInTheDocument()
  })

  it("localises the filtered empty state without rewriting raw activity evidence", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mockSuccessfulLogs()
    renderTab()

    fireEvent.change(
      screen.getByLabelText("Aktivitätszeitleiste der Wiederherstellung filtern"),
      { target: { value: "successful" } },
    )

    expect(screen.getByText("Keine Meilensteine entsprechen diesem Filter")).toBeInTheDocument()
    expect(screen.getByText("Alle Meilensteine anzeigen")).toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: "Alle Meilensteine anzeigen" }))
    expect(screen.getAllByText("Raw timeline detail from HostAgent.").length).toBeGreaterThan(0)
  })

  it("localises log-loading failure while retaining raw technical detail", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    mocks.useRestoreLogs.mockReturnValue({
      data: undefined,
      error: new Error("Raw Activity transport detail from HostAgent."),
      isError: true,
      isFetching: false,
      isLoading: false,
    })

    renderTab()

    expect(screen.getByText("Aktuelle strukturierte Aktivität konnte nicht geladen werden")).toBeInTheDocument()
    expect(screen.getByText("Technische Details")).toBeInTheDocument()
    expect(screen.getByText("Raw Activity transport detail from HostAgent.")).toBeInTheDocument()
    expect(screen.getByText("Noch keine Aktivität erfasst")).toBeInTheDocument()
  })
})
