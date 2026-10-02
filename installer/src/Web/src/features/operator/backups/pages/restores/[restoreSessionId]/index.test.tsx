import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"
import { http, HttpResponse } from "msw"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"

import { BackupRestoreWorkspaceRouteBoundary } from "./index"

const restoreSessionId = "restore-workspace-german-1"

function createWorkspace(): RestoreWorkspaceResponse {
  return {
    schemaVersion: 3,
    restoreSessionId,
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "backup-ready",
      createdAtUtc: "2026-06-29T05:00:00Z",
      updatedAtUtc: "2026-06-29T05:00:00Z",
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
      backupId: "20260629-023108Z",
      createdAtUtc: "2026-06-29T02:31:08Z",
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
    standardStages: [
      {
        code: "backup-ready",
        title: "Backup ready",
        description: "Confirm the backup is ready.",
        state: "completed",
        required: true,
        unlocked: true,
        completedAtUtc: "2026-06-29T05:00:00Z",
        primaryAction: null,
        secondaryActions: [],
        summary: "Backup ready.",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
      {
        code: "private-test",
        title: "Private test",
        description: "Optional private test.",
        state: "optional",
        required: false,
        unlocked: true,
        completedAtUtc: null,
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
      {
        code: "choose-restored-server-details",
        title: "Choose restored server details",
        description: "Choose target details.",
        state: "ready",
        required: true,
        unlocked: true,
        completedAtUtc: null,
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
      {
        code: "create-restored-chat-server",
        title: "Create restored server",
        description: "Create target server.",
        state: "not-started",
        required: true,
        unlocked: false,
        completedAtUtc: null,
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
      {
        code: "check-restored-server",
        title: "Check restored server",
        description: "Check target server.",
        state: "not-started",
        required: true,
        unlocked: false,
        completedAtUtc: null,
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
      {
        code: "complete-and-hand-over",
        title: "Complete and hand over",
        description: "Complete handover.",
        state: "not-started",
        required: true,
        unlocked: false,
        completedAtUtc: null,
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 0,
          latestOccurredAtUtc: null,
          latestStatus: null,
        },
        operationSummary: null,
        privateTestEvidence: null,
      },
    ],
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

function renderRoute(
  response = HttpResponse.json(createWorkspace()),
  initialEntry = `/restores/${restoreSessionId}`,
) {
  server.use(
    http.get(
      `/internal/host-agent/backups/restores/${restoreSessionId}/workspace`,
      () => response,
    ),
  )

  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route
          path="/restores/:restoreSessionId"
          element={<BackupRestoreWorkspaceRouteBoundary />}
        />
      </Routes>
    </MemoryRouter>,
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("BackupRestoreWorkspaceRouteBoundary", () => {
  it("renders the restore workspace shell and standard journey in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderRoute()

    expect(
      await screen.findByRole("heading", {
        name: "Diese Sicherung wiederherstellen",
      }),
    ).toBeInTheDocument()

    expect(
      screen.getByRole("tab", { name: "Wiederherstellungsschritte" }),
    ).toHaveAttribute("aria-selected", "true")
    expect(screen.getByRole("tab", { name: "Aktivität" })).toBeInTheDocument()
    expect(screen.getByRole("tab", { name: "Protokolle" })).toBeInTheDocument()
    expect(screen.getByRole("tab", { name: "Nachweise" })).toBeInTheDocument()
    expect(screen.getByText("Quellsicherung")).toBeInTheDocument()
    expect(screen.getByText("Sicherung bereit")).toBeInTheDocument()
    expect(screen.getByText("1,00 KB")).toBeInTheDocument()
  })

  it("does not expose the deferred Advanced tools tab and safely falls back from legacy links to Restore steps", async () => {
    renderRoute(
      HttpResponse.json(createWorkspace()),
      `/restores/${restoreSessionId}?tab=advanced`,
    )

    expect(
      await screen.findByRole("heading", { name: "Restore this backup" }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("tab", { name: "Restore steps" }),
    ).toHaveAttribute("aria-selected", "true")
    expect(
      screen.queryByRole("tab", { name: "Advanced tools" }),
    ).not.toBeInTheDocument()
    expect(screen.queryByText(/planned next/i)).not.toBeInTheDocument()
  })

  it("withholds the target-summary progression action for a cancelled restore", async () => {
    const workspace = createWorkspace()
    workspace.attempt.status = "cancelled"
    workspace.attempt.currentStage = "cancelled"
    workspace.attempt.terminalAtUtc = "2026-06-29T06:00:00Z"
    workspace.overallStatus = {
      code: "cancelled",
      title: "Restore cancelled",
      description: "No further restore actions will run for this attempt.",
      severity: "information",
      nextAction: null,
    }

    renderRoute(HttpResponse.json(workspace))

    expect(await screen.findByText("Restore cancelled")).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: /Choose details/i }),
    ).not.toBeInTheDocument()
  })

  it("renders a known restore-attempt problem in English and retains raw technical detail", async () => {
    const detail = "Restore attempt 'restore-problem-1' was not found."

    renderRoute(
      HttpResponse.json(
        {
          error: "restore_attempt_not_found",
          detail,
          message: {
            code: "restore.attempt.not-found",
            arguments: {
              restoreSessionId: "restore-problem-1",
            },
          },
        },
        { status: 404 },
      ),
    )

    expect(
      await screen.findByText(
        "The restore attempt with Restore Session ID restore-problem-1 was not found.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
    expect(screen.getByText("Technical details").closest("details")).not.toHaveAttribute(
      "open",
    )
  })

  it("renders a known restore-attempt problem in German without changing the literal session ID", async () => {
    const detail = "Restore attempt 'restore-problem-1' was not found."
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderRoute(
      HttpResponse.json(
        {
          error: "restore_attempt_not_found",
          detail,
          message: {
            code: "restore.attempt.not-found",
            arguments: {
              restoreSessionId: "restore-problem-1",
            },
          },
        },
        { status: 404 },
      ),
    )

    expect(
      await screen.findByText(
        "Der Wiederherstellungsversuch mit der Wiederherstellungssitzungs-ID restore-problem-1 wurde nicht gefunden.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technische Details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
  })

  it("renders a known restore-workspace request problem in English", async () => {
    renderRoute(
      HttpResponse.json(
        {
          error: "invalid_restore_workspace_request",
          detail: "The request cannot be processed in the current restore state.",
          message: {
            code: "restore.workspace-request.invalid",
            arguments: {
              restoreSessionId,
            },
          },
        },
        { status: 400 },
      ),
    )

    expect(
      await screen.findByText(
        "The restore workspace request for Restore Session ID restore-workspace-german-1 is invalid.",
      ),
    ).toBeInTheDocument()
  })

  it("keeps the generic fallback for an unknown structured problem while retaining raw detail", async () => {
    const detail = "A future HostAgent problem was returned."

    renderRoute(
      HttpResponse.json(
        {
          error: "future_restore_problem",
          detail,
          message: {
            code: "restore.future-problem",
            arguments: {
              restoreSessionId,
            },
          },
        },
        { status: 409 },
      ),
    )

    expect(
      await screen.findByText("Restore workspace could not be opened"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        "This route needs a canonical restore attempt reference. The restore may no longer be available, or this may be an older legacy link.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
  })

  it("keeps the generic fallback when an older HostAgent response has no structured descriptor", async () => {
    const detail = "Restore attempt lookup failed without a structured message."

    renderRoute(
      HttpResponse.json(
        {
          error: "restore_attempt_not_found",
          detail,
        },
        { status: 404 },
      ),
    )

    expect(
      await screen.findByText("Restore workspace could not be opened"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        "This route needs a canonical restore attempt reference. The restore may no longer be available, or this may be an older legacy link.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
  })
})
