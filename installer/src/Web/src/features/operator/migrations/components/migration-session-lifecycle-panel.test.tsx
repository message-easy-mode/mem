import { HttpResponse, http } from "msw"
import { screen, waitFor, within } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import type { MigrationSessionLifecycleInspection } from "@/features/operator/migrations/api/migration-sessions"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"

import { MigrationSessionLifecyclePanel } from "./migration-session-lifecycle-panel"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onVerified,
  }: {
    open: boolean
    onVerified?: () => void
  }) => open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

const migrationId = "mig-lifecycle-actions"
const endpoint =
  `/api/operator/migrations/sessions/${migrationId}/lifecycle/cancel`
const deleteEndpoint =
  `/api/operator/migrations/sessions/${migrationId}/lifecycle/delete`

function renderPanel(lifecycle: MigrationSessionLifecycleInspection) {
  return renderWithProviders(
    <MemoryRouter>
      <MigrationSessionLifecyclePanel lifecycle={lifecycle} />
    </MemoryRouter>,
  )
}

const lifecycle: MigrationSessionLifecycleInspection = {
  migrationId,
  lifecycleStatus: "active",
  archived: false,
  archivedAtUtc: null,
  archivedBy: null,
  closedAtUtc: null,
  closureKind: null,
  stateVersion: 4,
  currentOperation: "none",
  packageState: "retained",
  candidateArtifactState: "none",
  privateStagingRuntimeState: "none",
  productionRuntimeState: "none",
  publicRoutesState: "none",
  sourceState: "running",
  capabilities: {
    canArchive: false,
    canUnarchive: false,
    canCancel: true,
    canDelete: false,
    cancelBlockedCode: null,
    cancelBlockedReason: null,
    deleteBlockedCode: "migration_session_active",
    deleteBlockedReason: "Active Migration Sessions cannot be permanently deleted.",
  },
  sourceUnaffectedNotice:
    "This target-side action does not delete or change files, containers, services, or data on the source server.",
}

describe("MigrationSessionLifecyclePanel", () => {
  it("requires explicit target retention and source acknowledgement, then resumes after step-up", async () => {
    let attempts = 0
    const bodies: unknown[] = []

    server.use(
      http.post(endpoint, async ({ request }) => {
        attempts += 1
        bodies.push(await request.json())
        if (attempts === 1) {
          return HttpResponse.json(
            { status: "step_up_required" },
            { status: 403 },
          )
        }

        return HttpResponse.json({
          resultCode: "migration_session_cancelled",
          idempotent: false,
          lifecycle: {
            ...lifecycle,
            lifecycleStatus: "cancelled",
            closedAtUtc: "2026-07-29T08:30:00Z",
            closureKind: "operator-cancelled",
            stateVersion: 5,
            packageState: "retired",
            capabilities: {
              ...lifecycle.capabilities,
              canArchive: true,
              canCancel: false,
              cancelBlockedCode: "migration_session_lifecycle_terminal",
              cancelBlockedReason:
                "This Session lifecycle is not eligible for early cancellation.",
            },
          },
        })
      }),
    )

    const user = userEvent.setup()
    renderPanel(lifecycle)

    expect(screen.getAllByText("Session lifecycle").length).toBeGreaterThan(0)
    expect(screen.getByText("Deletion reason")).toBeInTheDocument()
    expect(screen.getByText(/Active Migration Sessions cannot be permanently deleted/)).toBeInTheDocument()
    expect(screen.queryByText("Target encrypted package")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Cancel migration" }))

    const dialog = screen.getByRole("dialog")
    expect(within(dialog).getByText(/source server is unaffected/i)).toBeInTheDocument()
    expect(within(dialog).getByText(/decrypted package is always removed/i)).toBeInTheDocument()
    expect(within(dialog).getByText(/audit evidence only/i)).toBeInTheDocument()

    await user.selectOptions(
      within(dialog).getByLabelText("Target encrypted package"),
      "retain-encrypted",
    )
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(
      within(dialog).getByRole("button", { name: "Cancel migration" }),
    )

    await user.click(
      await screen.findByRole("button", { name: "Complete step-up" }),
    )

    await waitFor(() => expect(attempts).toBe(2))
    expect(bodies).toEqual([
      {
        expectedStateVersion: 4,
        acknowledgeSourceUnaffected: true,
        encryptedPackageRetention: "retain-encrypted",
      },
      {
        expectedStateVersion: 4,
        acknowledgeSourceUnaffected: true,
        encryptedPackageRetention: "retain-encrypted",
      },
    ])
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    )
  })

  it("requires exact Migration ID, source acknowledgement, and step-up for deletion", async () => {
    let attempts = 0
    const bodies: unknown[] = []

    server.use(
      http.post(deleteEndpoint, async ({ request }) => {
        attempts += 1
        bodies.push(await request.json())
        if (attempts === 1) {
          return HttpResponse.json(
            { status: "step_up_required" },
            { status: 403 },
          )
        }

        return HttpResponse.json({
          resultCode: "migration_session_deleted",
          idempotent: false,
          migrationId,
          deletedAtUtc: "2026-07-29T08:31:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderPanel({
      ...lifecycle,
      lifecycleStatus: "cancelled",
      closedAtUtc: "2026-07-29T08:30:00Z",
      closureKind: "operator-cancelled",
      packageState: "retired",
      capabilities: {
        ...lifecycle.capabilities,
        canCancel: false,
        canDelete: true,
        cancelBlockedCode: "migration_session_lifecycle_terminal",
        cancelBlockedReason:
          "This Session lifecycle is not eligible for early cancellation.",
        deleteBlockedCode: null,
        deleteBlockedReason: null,
      },
    })

    await user.click(screen.getByRole("button", { name: "Delete permanently" }))
    const dialog = screen.getByRole("dialog")
    await user.type(
      within(dialog).getByLabelText("Enter the exact Migration ID"),
      "wrong-id",
    )
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(
      within(dialog).getByRole("button", { name: "Delete permanently" }),
    )

    expect(within(dialog).getByText(/exact Migration ID before permanently deleting/i)).toBeInTheDocument()
    expect(attempts).toBe(0)

    const confirmation = within(dialog).getByLabelText("Enter the exact Migration ID")
    await user.clear(confirmation)
    await user.type(confirmation, migrationId)
    await user.click(
      within(dialog).getByRole("button", { name: "Delete permanently" }),
    )
    await user.click(
      await screen.findByRole("button", { name: "Complete step-up" }),
    )

    await waitFor(() => expect(attempts).toBe(2))
    expect(bodies).toEqual([
      {
        expectedStateVersion: 4,
        confirmationMigrationId: migrationId,
        acknowledgeSourceUnaffected: true,
      },
      {
        expectedStateVersion: 4,
        confirmationMigrationId: migrationId,
        acknowledgeSourceUnaffected: true,
      },
    ])
  })

  it("explains why a completed session cannot be cancelled", () => {
    renderPanel({
      ...lifecycle,
      lifecycleStatus: "completed",
      packageState: "retired",
      capabilities: {
        ...lifecycle.capabilities,
        canArchive: true,
        canCancel: false,
        cancelBlockedCode: "migration_session_completed",
        cancelBlockedReason:
          "Accepted or completed Migration Sessions and their first native backup boundary must be retained.",
      },
    })

    expect(screen.getByText("Cancellation is unavailable")).toBeInTheDocument()
    expect(
      screen.getByText("Accepted or completed Migration Sessions and their first native backup boundary must be retained."),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Cancel migration" }),
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Archive migration" }),
    ).toBeInTheDocument()
  })
})
