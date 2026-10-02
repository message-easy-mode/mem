import { http, HttpResponse } from "msw"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types"

import { RestoreWorkspaceCancelAction } from "./restore-workspace-cancel-action"

const restoreSessionId = "restore-cancel-catalog-1"

function createWorkspace(
  cancellation: RestoreWorkspaceResponse["cancellation"],
): RestoreWorkspaceResponse {
  return {
    schemaVersion: 4,
    restoreSessionId,
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "backup-ready",
      createdAtUtc: "2026-06-30T06:00:00Z",
      updatedAtUtc: "2026-06-30T06:00:00Z",
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
      backupId: "20260630-060000Z",
      createdAtUtc: "2026-06-30T06:00:00Z",
      sizeBytes: 1024,
      matrixHost: "matrix-cool-stack.deltabox.dev",
      elementHost: "chat-cool-stack.deltabox.dev",
      validationStatus: "available",
      validationSummary: "Catalog payload is available.",
      catalogEntryId: "bkp-cancel-001",
      sourceDisplayName: "Cool Stack backup",
      sourceOriginKind: "local-captured",
      sourceDeleted: false,
    },
    target: {
      stackSlug: null,
      matrixHost: null,
      elementHost: null,
      availability: "not-selected",
      detail: "No target has been reserved.",
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
    cancellation,
  }
}

afterEach(() => {
  window.localStorage.clear()
})

describe("RestoreWorkspaceCancelAction", () => {
  it("renders its irreversible cancellation wording in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()

    renderWithProviders(
      <RestoreWorkspaceCancelAction
        workspace={createWorkspace({
          canCancel: true,
          reasonUnavailable: null,
          summary: "Cancelling releases temporary restore target claims.",
        })}
        onCancelled={vi.fn(async () => undefined)}
      />,
    )

    await user.click(
      screen.getByRole("button", { name: "Wiederherstellung abbrechen" }),
    )

    expect(
      screen.getByRole("heading", { name: "Wiederherstellung abbrechen?" }),
    ).toBeInTheDocument()
    expect(screen.getByText("Was der Abbruch ändert")).toBeInTheDocument()
    expect(
      screen.getByText(
        "Der Abbruch gibt temporäre Zielreservierungen frei und schließt diesen unvollständigen Arbeitsbereich. Die Quellsicherung und der Auditverlauf bleiben erhalten.",
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByText("Cancelling releases temporary restore target claims."),
    ).not.toBeInTheDocument()
  })
  it("cancels an active catalog workspace through the canonical session endpoint", async () => {
    let requestCount = 0
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/cancel`,
        ({ request }) => {
          requestCount += 1
          const url = new URL(request.url)
          expect(url.searchParams.get("acknowledgeCancel")).toBe("true")

          return HttpResponse.json({
            id: "00000000-0000-0000-0000-000000000001",
            restoreSessionId,
            sourceKind: "backup-catalog",
            sourceKey: "backup-catalog:bkp_cancel",
            sourceStackSlug: "cool-stack",
            sourceBackupId: "20260630-060000Z",
            status: "cancelled",
            currentStage: "cancelled",
            createdAtUtc: "2026-06-30T06:00:00Z",
            updatedAtUtc: "2026-06-30T06:05:00Z",
            terminalAtUtc: "2026-06-30T06:05:00Z",
            lastEventAtUtc: "2026-06-30T06:05:00Z",
            lastErrorCode: null,
            lastErrorSummary: null,
            warningCount: 0,
            errorCount: 0,
            sessionDirectoryPath: "/redacted",
            logDirectoryPath: null,
            supportReportPath: null,
            runtimeOperationId: null,
            backupCatalogEntryId: "00000000-0000-0000-0000-000000000010",
          })
        },
      ),
    )

    const user = userEvent.setup()
    const onCancelled = vi.fn(async () => undefined)

    renderWithProviders(
      <RestoreWorkspaceCancelAction
        workspace={createWorkspace({
          canCancel: true,
          reasonUnavailable: null,
          summary: "Cancelling releases temporary restore target claims.",
        })}
        onCancelled={onCancelled}
      />,
    )

    await user.click(screen.getByRole("button", { name: "Cancel restore" }))

    expect(screen.getByRole("heading", { name: "Cancel restore?" })).toBeInTheDocument()
    expect(
      screen.getByText(
        "Cancellation releases temporary target reservations and closes this unfinished workspace. The source backup and audit history are retained.",
      ),
    ).toBeInTheDocument()

    await user.click(
      within(screen.getByRole("alertdialog")).getByRole("button", {
        name: /^Cancel restore$/,
      }),
    )

    await waitFor(() => expect(requestCount).toBe(1))
    await waitFor(() => expect(onCancelled).toHaveBeenCalledTimes(1))
  })

  it("renders a known cancellation problem in German and retains raw technical detail", async () => {
    const detail = "Restore cannot be cancelled while it has a queued operation."
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/cancel`,
        () =>
          HttpResponse.json(
            {
              error: "restore_cancel_not_available",
              detail,
              message: {
                code: "restore.cancel.not-available",
                arguments: { restoreSessionId },
              },
            },
            { status: 400 },
          ),
      ),
    )

    const user = userEvent.setup()
    renderWithProviders(
      <RestoreWorkspaceCancelAction
        workspace={createWorkspace({
          canCancel: true,
          reasonUnavailable: null,
          summary: "Cancelling releases temporary restore target claims.",
        })}
        onCancelled={vi.fn(async () => undefined)}
      />,
    )

    await user.click(
      screen.getByRole("button", { name: "Wiederherstellung abbrechen" }),
    )
    await user.click(
      within(screen.getByRole("alertdialog")).getByRole("button", {
        name: /^Wiederherstellung abbrechen$/,
      }),
    )

    expect(
      await screen.findByText(
        "Die Wiederherstellungssitzungs-ID restore-cancel-catalog-1 kann in ihrem aktuellen Schritt nicht abgebrochen werden.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technische Details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
    expect(screen.getByText("Technische Details").closest("details")).not.toHaveAttribute(
      "open",
    )
  })

  it("honours the server-projected cancellation guard instead of guessing from active status", () => {
    renderWithProviders(
      <RestoreWorkspaceCancelAction
        workspace={createWorkspace({
          canCancel: false,
          reasonUnavailable: "A restore operation is queued or running. Wait for it to finish or fail before cancelling safely.",
          summary: "MEM will not cancel a live operation halfway through a mutation.",
        })}
        onCancelled={vi.fn(async () => undefined)}
      />,
    )

    expect(
      screen.queryByRole("button", { name: "Cancel restore" }),
    ).not.toBeInTheDocument()
    expect(
      screen.getByText(/queued or running/i),
    ).toBeInTheDocument()
  })
})
