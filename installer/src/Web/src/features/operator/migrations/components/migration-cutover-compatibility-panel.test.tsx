import { HttpResponse, http } from "msw"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { MigrationCutoverCompatibilityPanel } from "./migration-cutover-compatibility-panel"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({ OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) => open ? <button onClick={onVerified}>Complete step-up</button> : null }))

describe("MigrationCutoverCompatibilityPanel", () => {
  it("requires readiness, final acknowledgements and fresh browser step-up before public cutover execution", async () => {
    let executionRequest: Record<string, unknown> | null = null
    server.use(
      http.post("/internal/host-agent/backups/catalog/catalog-1/production-restore/candidates/recreate-private", () => HttpResponse.json({ status: "private_candidate_ready", catalogEntryId: "catalog-1", restoreSessionId: "restore-1", candidateCreated: true, candidateResumed: false, candidate: { candidateId: "candidate-1", status: "private_candidate_ready", privateRuntimeStatus: "ready", matrixServerName: "matrix.example.test", safety: { privateOnly: true, publicRoutesCreated: false, productionExecutionLocked: true }, runtime: { synapseHealthPassed: true, elementHealthPassed: true }, warnings: [], errors: [] }, detail: "ready" })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/production-restore/cutover-preview", () => HttpResponse.json({ status: "ready_for_review", catalogEntryId: "catalog-1", candidateId: "candidate-1", preview: { previewId: "preview-1", status: "ready_for_review", productionExecutionLocked: true, blockers: [], warnings: [], errors: [] }, detail: "preview" })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/production-restore/cutover-confirmations/evaluate", () => HttpResponse.json({ status: "confirmed_ready_for_execution", catalogEntryId: "catalog-1", previewId: "preview-1", candidateId: "candidate-1", confirmation: { confirmationId: "confirmation-1", status: "confirmed_ready_for_execution", productionExecutionLocked: true, executionAvailable: true, blockers: [], warnings: [], errors: [] }, detail: "confirmed" })),
      http.get("/internal/host-agent/backups/catalog/catalog-1/production-restore/pre-cutover-readiness", () => HttpResponse.json({ source: "control-plane", status: "ready", catalogEntryId: "catalog-1", catalog: { sourceStackSlug: "davids-stack" }, executionReady: true, blockers: [], warnings: [], detail: "ready" })),
      http.get("/internal/host-agent/backups/catalog/catalog-1/production-restore/post-cutover", () => HttpResponse.json({ source: "control-plane", status: "ok", catalogEntryId: "catalog-1", restoreSessionId: "restore-1", cutover: { state: "cutover_completed", executionRecorded: true, routeTransitionCompleted: true, publicVerificationPassed: true, finalBackupCaptured: true, rolledBack: false, oldRuntimeRetainedForRollback: true, executionId: "execution-1", candidateId: "candidate-1", confirmationId: "confirmation-1", startedAtUtc: "2026-07-14T09:00:00Z", finishedAtUtc: "2026-07-14T09:05:00Z", detail: "completed" }, latestExecution: null, latestSuccessfulExecution: { executionId: "execution-1", status: "completed", finishedAtUtc: "2026-07-14T09:05:00Z", detail: "completed" }, executionCount: 1, canOpenRestoreWorkspace: true, warnings: [], detail: "Catalog-backed public cutover completed and public verification passed." })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/production-restore/cutover-executions/execute", async ({ request }) => {
        executionRequest = await request.json() as Record<string, unknown>
        return HttpResponse.json({ source: "control-plane", status: "completed", catalogEntryId: "catalog-1", restoreSessionId: "restore-1", confirmationId: "confirmation-1", candidateId: "candidate-1", execution: { executionId: "execution-1", status: "completed", oldRuntimeStackSlug: "davids-stack", finalBackup: { captured: true, backupId: "backup-final", backupCatalogEntryId: "catalog-final", detail: "captured" }, oldRuntime: { retainedForRollback: true, matrixContainerStopped: true, elementContainerStopped: true, detail: "retained" }, routes: { allRequiredRoutesSucceeded: true, anyRouteMutated: true, detail: "switched" }, publicVerification: { attempted: true, passed: true, detail: "passed" }, rollback: { required: false, attempted: false, completed: false, detail: "not required", warnings: [] }, blockers: [], warnings: [], errors: [], detail: "completed" }, detail: "completed" })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationCutoverCompatibilityPanel catalogEntryId="catalog-1" payloadAvailable />)

    expect(screen.getByRole("button", { name: "Execute public cutover" })).toBeDisabled()

    await user.click(screen.getByRole("button", { name: "Create private candidate" }))
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("candidate-1")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Create read-only preview" }))
    expect(await screen.findByText("preview-1")).toBeInTheDocument()

    for (const checkbox of screen.getAllByRole("checkbox")) await user.click(checkbox)
    await user.click(screen.getByRole("button", { name: "Save confirmation" }))
    expect(await screen.findByText("confirmation-1")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Check readiness" }))
    expect(await screen.findByText("Ready for activation handoff")).toBeInTheDocument()
    expect(screen.getByDisplayValue("davids-stack")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Execute public cutover" })).toBeDisabled()

    const checkboxes = screen.getAllByRole("checkbox")
    for (const checkbox of checkboxes.slice(-5)) await user.click(checkbox)
    expect(screen.getByRole("button", { name: "Execute public cutover" })).toBeEnabled()

    await user.click(screen.getByRole("button", { name: "Execute public cutover" }))
    expect(executionRequest).toBeNull()
    await user.click(screen.getAllByRole("button", { name: "Complete step-up" }).at(-1)!)

    expect(await screen.findByText("Public cutover completed")).toBeInTheDocument()
    expect(screen.getAllByText("execution-1").length).toBeGreaterThan(0)
    expect(await screen.findByText("Verified target is public")).toBeInTheDocument()
    expect(screen.getByText("Pending — separate stepped-up action")).toBeInTheDocument()
    expect(executionRequest).toMatchObject({
      confirmationId: "confirmation-1",
      candidateId: "candidate-1",
      oldRuntimeStackSlug: "davids-stack",
      execute: true,
      acknowledgeFinalApproval: true,
      acknowledgeFinalBackupWillBeCaptured: true,
      acknowledgeOldRuntimeWillBeStopped: true,
      acknowledgePublicRouteMutation: true,
      acknowledgeManualRollback: true,
    })
  }, 10_000)
})
