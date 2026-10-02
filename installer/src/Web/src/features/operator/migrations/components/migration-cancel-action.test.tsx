import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { server } from "@/test/msw-server"
import { readPendingGuidedOperation, writePendingGuidedOperation } from "../api/migration-guided-state"
import type { MigrationSessionCancelRequest } from "../api/migration-sessions"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import { MigrationCancelAction } from "./migration-cancel-action"

const migrationId = "mig_guided"
const endpoint = `/api/operator/migrations/sessions/${migrationId}/lifecycle/cancel`
let workspace = makeWorkspace()
let posts: MigrationSessionCancelRequest[] = []

function makeWorkspace(kind: "empty-session" | "package" | "progressed" = "empty-session") {
  const result = guidedWorkspace({ stage: "create-and-upload-package", action: "upload-package", guided: {
    cancellation: { canCancel: true, confirmationKind: kind, lifecycleStatus: "active", stateVersion: 4,
      blockedCode: null, blockedReason: null },
  } })
  result.guided.operationRevisions["cancel-migration"] = "initial-cancel"
  return result
}
function markCancelled() {
  workspace.guided.operationRevisions["cancel-migration"] = "cancelled-by-operator"
  workspace.guided.cancellation = { ...workspace.guided.cancellation!, lifecycleStatus: "cancelled",
    canCancel: false, confirmationKind: "unavailable", stateVersion: 5 }
  workspace.guided.nextAction = null
}
function Destination() {
  const location = useLocation()
  return <><h1>Migration inventory destination</h1><output>{JSON.stringify(location.state)}</output></>
}
function renderAction() {
  return renderGuided(<MemoryRouter initialEntries={[`/migrations/${migrationId}`]}>
    <Routes>
      <Route path="/migrations/:migrationId" element={<MigrationCancelAction />} />
      <Route path="/migrations" element={<Destination />} />
    </Routes>
  </MemoryRouter>, () => workspace)
}
async function openDialog() {
  const user = userEvent.setup()
  await user.click(screen.getByRole("button", { name: "Cancel migration" }))
  return { user, dialog: screen.getByRole("alertdialog") }
}

beforeEach(() => {
  sessionStorage.clear()
  localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  workspace = makeWorkspace()
  posts = []
  server.use(http.post(endpoint, async ({ request }) => {
    posts.push(await request.json() as MigrationSessionCancelRequest)
    markCancelled()
    return HttpResponse.json({ resultCode: "migration_session_cancelled" })
  }))
})

describe("visible guided early cancellation", () => {
  it("closes an empty session with one explicit confirmation and no retention selector", async () => {
    renderAction()
    const { user, dialog } = await openDialog()
    expect(within(dialog).getByText(/No package has been uploaded and no new server has been created/)).toBeInTheDocument()
    expect(within(dialog).queryByRole("combobox")).not.toBeInTheDocument()
    expect(within(dialog).queryByRole("checkbox")).not.toBeInTheDocument()
    expect(within(dialog).getByRole("button", { name: "Keep migration" })).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toEqual([{ expectedStateVersion: 4, acknowledgeSourceUnaffected: true, encryptedPackageRetention: "remove" }])
    expect(screen.getByText(/cancelledMigrationName/)).toHaveTextContent("Guided migration")
    expect(readPendingGuidedOperation(migrationId)).toBeNull()
  })

  it("keeps the session without submitting when the operator dismisses confirmation", async () => {
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Keep migration" }))
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(posts).toHaveLength(0)
    expect(workspace.guided.cancellation!.lifecycleStatus).toBe("active")
  })

  it("uses the same short empty-session confirmation in German", async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderAction()
    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: "Migration abbrechen" }))
    const dialog = screen.getByRole("alertdialog")
    expect(within(dialog).getByRole("button", { name: "Migration beibehalten" })).toBeInTheDocument()
    expect(within(dialog).getByText(/Der alte Server wird nicht verändert/)).toBeInTheDocument()
    expect(within(dialog).queryByRole("combobox")).not.toBeInTheDocument()
  })

  it("keeps the explicit retention and source acknowledgement for an uploaded package", async () => {
    workspace = makeWorkspace("package")
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(posts).toHaveLength(0)
    expect(within(dialog).getByText("Choose whether the target encrypted package should be removed or retained.")).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByRole("combobox", { name: "Target encrypted package" }), "retain-encrypted")
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(posts).toHaveLength(0)
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts[0].encryptedPackageRetention).toBe("retain-encrypted")
  })

  it("reviews progressed pre-production cleanup before submitting cancellation", async () => {
    workspace = makeWorkspace("progressed")
    renderAction()
    const { user, dialog } = await openDialog()
    expect(within(dialog).getByText(/prepared target-side migration data/)).toBeInTheDocument()
    expect(within(dialog).getByText(/conversion\/candidate working files/)).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByRole("combobox", { name: "Target encrypted package" }), "retain-encrypted")
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toEqual([{
      expectedStateVersion: 4,
      acknowledgeSourceUnaffected: true,
      encryptedPackageRetention: "retain-encrypted",
    }])
  })

  it("does not infer cancellation from an independently plausible early stage", () => {
    workspace.guided.cancellation = undefined
    renderAction()
    expect(screen.queryByRole("button", { name: "Cancel migration" })).not.toBeInTheDocument()
  })

  it("withholds cancellation when the server blocks it after conversion", () => {
    workspace.guided.cancellation!.canCancel = false
    workspace.guided.cancellation!.blockedCode = "migration_session_conversion_active"
    renderAction()
    expect(screen.queryByRole("button", { name: "Cancel migration" })).not.toBeInTheDocument()
  })

  it("keeps cancellation discoverable when retained staging must be retired first", async () => {
    const stagingRunId = "mst_retained"
    workspace = makeWorkspace("progressed")
    workspace.guided.cancellation = {
      ...workspace.guided.cancellation!,
      canCancel: false,
      confirmationKind: "unavailable",
      blockedCode: "migration_session_staging_retained",
      blockedReason: "Destroy the retained private staging runtime before closing this Session.",
    }
    workspace.guided.stagingRunId = stagingRunId
    workspace.guided.stagingStatus = "verified"
    workspace.guided.stagingRetained = true
    server.use(http.get(
      `/api/operator/migrations/sessions/${migrationId}/staging-runs/${stagingRunId}/retirement`,
      () => HttpResponse.json({
        migrationId, stagingRunId, displayName: "Guided migration", canRetire: true, blockerCode: null,
        reviewFingerprint: "review-retained", containerCount: 3, networkCount: 1, workspacePresent: true,
        operation: null,
      }),
    ))

    renderAction()
    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: "Cancel migration" }))
    const blocker = screen.getByRole("alertdialog")
    expect(within(blocker).getByText("Retire the private test before cancelling")).toBeInTheDocument()
    expect(within(blocker).getByText(/temporary private-test runtime must be retired first/)).toBeInTheDocument()
    await user.click(within(blocker).getByRole("button", { name: "Review staging retirement" }))

    expect(await screen.findByText("Retire migration staging")).toBeInTheDocument()
    expect(await screen.findByText(/3 containers and 1 networks/)).toBeInTheDocument()
    expect(posts).toHaveLength(0)
  })

  it("invalidates an open empty-session confirmation when a package arrives", async () => {
    const view = renderAction()
    const { dialog } = await openDialog()
    workspace = makeWorkspace("package")
    workspace.guided.cancellation!.stateVersion = 5
    await act(async () => { await view.refresh() })
    expect(within(dialog).getByText(/This migration changed after you opened the confirmation/)).toBeInTheDocument()
    expect(within(dialog).getByRole("button", { name: "Cancel migration" })).toBeDisabled()
    expect(posts).toHaveLength(0)
  })

  it("resumes only the explicitly confirmed body after the existing step-up boundary", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      if (posts.length === 1) return HttpResponse.json({ code: "step_up_required" }, { status: 403 })
      markCancelled()
      return HttpResponse.json({ resultCode: "migration_session_cancelled" })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toHaveLength(2)
    expect(posts[1]).toEqual(posts[0])
  })

  it("does not continue a stale empty-session request after step-up", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      return HttpResponse.json({ code: "step_up_required" }, { status: 403 })
    }))
    const view = renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    await screen.findByRole("button", { name: "Complete step-up" })
    workspace.guided.cancellation!.stateVersion = 5
    workspace.guided.cancellation!.confirmationKind = "package"
    await act(async () => { await view.refresh() })
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument()
    expect(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Cancel migration" })).toBeDisabled()
    expect(posts).toHaveLength(1)
  })

  it("observes a cancellation after a lost POST response without repeating it", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      markCancelled()
      return HttpResponse.error()
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toHaveLength(1)
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
  })

  it("keeps an unconfirmed cancellation uncertain and blocks repeated submission", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      return HttpResponse.error()
    }))
    const view = renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    await screen.findByText(/The cancellation result is not yet known/)
    expect(within(dialog).getByRole("button", { name: "Cancel migration" })).toBeDisabled()
    await user.click(within(dialog).getByRole("button", { name: "Close" }))
    expect(screen.getByRole("button", { name: "Cancel migration" })).toBeDisabled()
    workspace.guided.revision = "unrelated"
    workspace.guided.operationRevisions["upload-package"] = "new-upload"
    await act(async () => { await view.refresh() })
    expect(readPendingGuidedOperation(migrationId)?.operation).toBe("cancel-migration")
    expect(screen.queryByRole("heading", { name: "Migration inventory destination" })).not.toBeInTheDocument()
    expect(posts).toHaveLength(1)
  })

  it("treats a documented stale-state rejection as rejected, without replay", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      workspace.guided.cancellation!.stateVersion = 5
      workspace.guided.cancellation!.canCancel = false
      return HttpResponse.json({ code: "migration_session_state_stale", message: "The session changed." }, { status: 409 })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByText("The session changed.")).toBeInTheDocument()
    await waitFor(() => expect(readPendingGuidedOperation(migrationId)).toBeNull())
    expect(posts).toHaveLength(1)
    expect(screen.queryByRole("heading", { name: "Migration inventory destination" })).not.toBeInTheDocument()
  })

  it("does not claim cancellation from a successful response while the workspace is unchanged", async () => {
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      return HttpResponse.json({ resultCode: "migration_session_cancelled" })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByText(/The cancellation result is not yet known/)).toBeInTheDocument()
    expect(posts).toHaveLength(1)
    expect(readPendingGuidedOperation(migrationId)?.operation).toBe("cancel-migration")
    expect(screen.queryByRole("heading", { name: "Migration inventory destination" })).not.toBeInTheDocument()
  })

  it("shows a documented pre-commit cleanup failure rather than checking forever", async () => {
    workspace = makeWorkspace("package")
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      return HttpResponse.json({ code: "migration_session_package_cleanup_failed", message: "Target package cleanup could not finish." }, { status: 503 })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.selectOptions(within(dialog).getByRole("combobox"), "remove")
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByText("Target package cleanup could not finish.")).toBeInTheDocument()
    expect(posts).toHaveLength(1)
    expect(readPendingGuidedOperation(migrationId)).toBeNull()
    expect(screen.queryByRole("heading", { name: "Migration inventory destination" })).not.toBeInTheDocument()
  })

  it("surfaces a progressed pre-commit cleanup failure rather than checking forever", async () => {
    workspace = makeWorkspace("progressed")
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      return HttpResponse.json({ code: "migration_session_progressed_cleanup_failed", message: "Prepared target cleanup could not finish." }, { status: 503 })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.selectOptions(within(dialog).getByRole("combobox"), "remove")
    await user.click(within(dialog).getByRole("checkbox"))
    await user.click(within(dialog).getByRole("button", { name: "Cancel migration" }))
    expect(await screen.findByText("Prepared target cleanup could not finish.")).toBeInTheDocument()
    expect(posts).toHaveLength(1)
    expect(readPendingGuidedOperation(migrationId)).toBeNull()
  })

  it("restores a pending cancellation receipt and follows confirmed cancellation after reload", async () => {
    writePendingGuidedOperation(migrationId, { migrationId, operation: "cancel-migration", before: "initial-cancel" })
    markCancelled()
    renderAction()
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toHaveLength(0)
    expect(readPendingGuidedOperation(migrationId)).toBeNull()
  })

  it("leaves cancelled history readable when there is no pending submission", () => {
    markCancelled()
    renderAction()
    expect(screen.queryByRole("heading", { name: "Migration inventory destination" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Cancel migration" })).not.toBeInTheDocument()
    expect(posts).toHaveLength(0)
  })

  it("blocks duplicate clicks while the cancellation POST is in flight", async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => { release = resolve })
    server.use(http.post(endpoint, async ({ request }) => {
      posts.push(await request.json() as MigrationSessionCancelRequest)
      await gate
      markCancelled()
      return HttpResponse.json({ resultCode: "migration_session_cancelled" })
    }))
    renderAction()
    const { user, dialog } = await openDialog()
    await user.dblClick(within(dialog).getByRole("button", { name: "Cancel migration" }))
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(within(dialog).getByRole("button", { name: "Cancelling migration…" })).toBeDisabled()
    await act(async () => { release() })
    expect(await screen.findByRole("heading", { name: "Migration inventory destination" })).toBeInTheDocument()
    expect(posts).toHaveLength(1)
  })
})
