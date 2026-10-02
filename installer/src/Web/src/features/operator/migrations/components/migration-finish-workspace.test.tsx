import { HttpResponse, http } from "msw"
import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import type { ReactElement } from "react"
import type { MigrationAcceptanceState } from "../api/migration-acceptance"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"
import { MigrationFinishWorkspace } from "./migration-finish-workspace"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onOpenChange, onVerified }: {
    open: boolean; onOpenChange: (open: boolean) => void; onVerified?: () => void
  }) => open ? <div>
    <button onClick={() => { onOpenChange(false); onVerified?.() }}>Complete step-up</button>
    <button onClick={() => onOpenChange(false)}>Cancel step-up</button>
  </div> : null,
}))

const migrationId = "mig-finish"
const acceptancePath = `/api/operator/migrations/sessions/${migrationId}/acceptance`
const finishPath = `${acceptancePath}/finish`
const retryPath = `/api/operator/migrations/sessions/${migrationId}/baseline-backup/retry`

const assurance = {
  authorityType: "operator-attested-snapshot",
  productionAuthorityId: "mpauth-1",
  authorityEvidenceSha256: "f".repeat(64),
  packageRevisionId: "mpr-preview-1",
  captureKind: "preview",
  sourceFrozen: false,
  rehearsalOnly: true,
  formalSourceFreezeEvidenceCollected: false,
  finalRecapturePerformed: false,
  postCaptureWritesIndependentlyExcluded: false,
  rollbackAssurance: "reduced",
  twoServerQualificationRequired: false,
  detail: "Simplified assurance uses the verified operator-attested snapshot.",
}

const readyState = {
  source: "control-plane",
  status: "ready-for-acceptance",
  migrationId,
  acceptanceEligible: true,
  accepted: false,
  publicVerificationRequired: true,
  assurance,
  acceptance: null,
  legacyRetention: null,
  baselineBackup: null,
  blockers: [],
  warnings: [],
  detail: "Passed production verification is ready for acceptance.",
}

const acceptedState = {
  ...readyState,
  status: "accepted-baseline-backup-created",
  acceptanceEligible: false,
  accepted: true,
  publicVerificationRequired: false,
  acceptance: {
    acceptanceId: "macc-1",
    executionId: "mpce-1",
    candidateArtifactId: "mca-1",
    stagingRunId: "mstg-1",
    publicCutoverAtUtc: "2026-07-25T06:00:00Z",
    acceptedAtUtc: "2026-07-25T06:05:00Z",
    acceptedBy: "owner",
    note: null,
    publicVerification: {
      status: "passed",
      attempted: true,
      passed: true,
      checkCount: 24,
      failedCheckCount: 0,
      evidenceSha256: "a".repeat(64),
      detail: "The verified production evidence was bound to acceptance.",
    },
    freshPublicVerificationAcknowledged: true,
    targetWriteDivergenceAcknowledged: true,
    rollbackBoundaryAcknowledged: true,
    legacyRetentionAcknowledged: true,
    noAutomaticLegacyDeletionAcknowledged: true,
  },
  legacyRetention: {
    retentionRecordId: "mlr-1",
    status: "active",
    createdAtUtc: "2026-07-25T06:05:00Z",
    retainUntilUtc: "2026-08-08T06:05:00Z",
    cleanupEligibleAtUtc: "2026-08-08T06:05:00Z",
    sourceMigrationId: "source-1",
    sourceProduct: "Message Easy Mode",
    sourceVersion: "0.1.0",
    sourcePackageRetained: true,
    candidateArtifactRetained: true,
    privateStagingEvidenceRetained: true,
    legacySourceResourcesRetained: true,
    automaticDeletionAllowed: false,
    summary: "Cleanup is separate and never automatic.",
  },
  baselineBackup: {
    handoffId: "mbh-1",
    status: "created",
    attemptCount: 1,
    createdAtUtc: "2026-07-25T06:05:00Z",
    updatedAtUtc: "2026-07-25T06:06:00Z",
    startedAtUtc: "2026-07-25T06:05:00Z",
    completedAtUtc: "2026-07-25T06:06:00Z",
    targetStackSlug: "migrated-stack",
    candidateId: "mca-1",
    privateRuntimeId: "11111111-1111-1111-1111-111111111111",
    backupId: "backup-1",
    catalogEntryId: "catalog-1",
    backupCreatedAtUtc: "2026-07-25T06:06:00Z",
    backupTotalBytes: 1234,
    backupTotalFiles: 12,
    backupWarningCount: 0,
    failureCode: null,
    failureSummary: null,
    detail: "The first native MEM baseline backup was created.",
  },
  blockers: [],
  warnings: ["The old server is retained and is not synchronized."],
  detail: "Migration acceptance and retention are durable.",
}

let canonicalState: MigrationAcceptanceState = readyState
beforeEach(() => { sessionStorage.clear(); localStorage.removeItem(LANGUAGE_STORAGE_KEY); canonicalState = readyState })
afterEach(() => { localStorage.removeItem(LANGUAGE_STORAGE_KEY) })
function useStateHandler(state: MigrationAcceptanceState) {
  canonicalState = state
  server.use(http.get(acceptancePath, () => HttpResponse.json(canonicalState)))
}
function finishWorkspace() {
  const state = canonicalState
  const status = state.baselineBackup?.status ?? "not-created"
  const workspace = guidedWorkspace({ migrationId, stage: "finish-migration",
    state: state.accepted ? status === "created" ? "completed" : status === "failed" ? "action-required" : "running"
      : state.acceptanceEligible ? "ready" : "blocked",
    action: state.accepted ? status === "created" ? "open-baseline-backup" : status === "failed" ? "retry-baseline-backup" : null
      : state.acceptanceEligible ? "finish-migration" : "open-technical-details",
    guided: { accepted: state.accepted, acceptanceId: state.acceptance?.acceptanceId ?? null,
      baselineBackupStatus: status, baselineBackupId: state.baselineBackup?.handoffId ?? null,
      baselineCatalogEntryId: state.baselineBackup?.catalogEntryId ?? null,
    },
  })
  workspace.guided.operationRevisions["finish-migration"] = `${state.acceptance?.acceptanceId}:${status}`
  workspace.guided.operationRevisions["baseline-backup"] = `${state.baselineBackup?.handoffId}:${status}:${state.baselineBackup?.attemptCount}`
  workspace.target.stackSlug = state.baselineBackup?.targetStackSlug ?? "migrated-stack"
  return workspace
}
function renderWithProviders(ui: ReactElement) { return renderGuided(ui, finishWorkspace) }

describe("MigrationFinishWorkspace", () => {
  it("checks an unconfirmed baseline with reads instead of starting a second backup", async () => {
    useStateHandler({ ...acceptedState, status: "accepted-baseline-backup-pending",
      baselineBackup: { ...acceptedState.baselineBackup, status: "pending", catalogEntryId: null, backupId: null } })
    const retry = vi.fn(() => HttpResponse.json(acceptedState))
    server.use(http.post(retryPath, retry))
    renderWithProviders(<MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />)
    await userEvent.setup().click(await screen.findByRole("button", { name: "Check the server state again" }))
    expect(retry).not.toHaveBeenCalled()
    expect(screen.queryByRole("button", { name: "Continue first backup" })).not.toBeInTheDocument()
  })

  it("reuses a fresh go-live step-up grant and finishes without another prompt", async () => {
    let attempts = 0
    useStateHandler(readyState)
    server.use(
      http.post(finishPath, () => {
        attempts += 1
        canonicalState = acceptedState
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />)

    await user.click(await screen.findByRole("button", { name: "Finish migration" }))

    await waitFor(() => expect(attempts).toBe(1))
    expect(screen.queryByRole("button", { name: "Complete step-up" })).not.toBeInTheDocument()
    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
  })

  it("submits inline finish decisions through step-up without a duplicate confirmation", async () => {
    let attempts = 0
    let requestBody: Record<string, unknown> | null = null
    const bodies: unknown[] = []
    useStateHandler(readyState)
    server.use(
      http.post(finishPath, async ({ request }) => {
        attempts += 1
        requestBody = await request.json() as Record<string, unknown>
        bodies.push(requestBody)
        if (attempts === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        canonicalState = acceptedState
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    const onChanged = vi.fn(async () => undefined)
    renderWithProviders(
      <MigrationFinishWorkspace migrationId={migrationId} onChanged={onChanged} />,
    )

    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText("Keep the old server for"), "21")
    expect(screen.getByText(/new server becomes the authoritative server/i)).toBeInTheDocument()
    expect(screen.getByText(/not synchronized back/i)).toBeInTheDocument()
    expect(screen.getByText(/will not delete it automatically/i)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Finish migration" }))
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    const verify = await screen.findByRole("button", { name: "Complete step-up" })
    expect(screen.getByLabelText("Keep the old server for")).toBeDisabled()
    expect(screen.getByRole("button", { name: "Finish migration" })).toBeDisabled()
    await user.click(verify)

    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
    expect(requestBody).toEqual({
      retentionDays: 21,
      confirmVerifiedServerIsAuthoritative: true,
      confirmRecoveryBoundaryChanges: true,
      confirmRetainOldServerAndNoAutomaticDeletion: true,
    })
    expect(JSON.stringify(requestBody).toLowerCase()).not.toMatch(
      /executionid|candidateartifactid|stagingrunid|runtimestackid|catalogentryid/,
    )
    expect(onChanged).toHaveBeenCalled()
    expect(attempts).toBe(2)
    expect(bodies[1]).toEqual(bodies[0])
  })

  it("rediscovers durable acceptance when the finish response is lost", async () => {
    let stateReads = 0
    server.use(
      http.get(acceptancePath, () => {
        stateReads += 1
        return HttpResponse.json(stateReads === 1 ? readyState : acceptedState)
      }),
      http.post(finishPath, () => { canonicalState = acceptedState; return HttpResponse.error() }),
    )

    const user = userEvent.setup()
    const onChanged = vi.fn(async () => undefined)
    renderWithProviders(
      <MigrationFinishWorkspace migrationId={migrationId} onChanged={onChanged} />,
    )

    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Finish migration" }))
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()

    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
    expect(screen.queryByText("The finish action did not complete")).not.toBeInTheDocument()
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(stateReads).toBeGreaterThanOrEqual(2)
    expect(onChanged).not.toHaveBeenCalled() // The canonical workspace observer owns recovery.
  })


  it("shows finish progress immediately, prevents duplicate submission, and preserves success when a follow-up read fails", async () => {
    const user = userEvent.setup()
    let release!: () => void
    const pending = new Promise<void>((resolve) => { release = resolve })
    let posts = 0
    useStateHandler(readyState)
    server.use(http.post(finishPath, async () => {
      posts += 1
      await pending
      canonicalState = acceptedState
        return HttpResponse.json(acceptedState)
    }))
    renderWithProviders(<MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => { throw new Error("Follow-up read unavailable") }} />)
    const finish = await screen.findByRole("button", { name: "Finish migration" })
    try {
      await user.dblClick(finish)
      const busy = await screen.findByRole("button", { name: "Finishing migration..." })
      expect(busy).toBeDisabled()
      expect(busy).toHaveAttribute("aria-busy", "true")
      expect(busy.querySelector("svg.animate-spin")).not.toBeNull()
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
      await waitFor(() => expect(posts).toBe(1))
    } finally {
      await act(async () => release())
    }
    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
    expect(screen.queryByText("The finish action did not complete")).not.toBeInTheDocument()
  })

  it("does not finish when identity verification is cancelled", async () => {
    const user = userEvent.setup()
    let posts = 0
    useStateHandler(readyState)
    server.use(http.post(finishPath, () => {
      posts += 1
      return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
    }))
    renderWithProviders(<MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />)
    await user.click(await screen.findByRole("button", { name: "Finish migration" }))
    await user.click(await screen.findByRole("button", { name: "Cancel step-up" }))
    expect(screen.queryByRole("button", { name: "Complete step-up" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Finish migration" })).toBeEnabled()
    expect(screen.getByLabelText("Keep the old server for")).toBeEnabled()
    expect(posts).toBe(1)
    expect(screen.queryByText("Migration complete")).not.toBeInTheDocument()
  })

  it("shows completion links while keeping identifiers secondary", async () => {
    useStateHandler(acceptedState)
    renderWithProviders(
      <MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />,
    )

    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
    expect(await screen.findByText("Baseline backup created")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open server" })).toHaveAttribute(
      "href",
      "/stacks/migrated-stack",
    )
    expect(screen.getByRole("link", { name: "Review voice / video TURN" })).toHaveAttribute(
      "href",
      "/stacks/migrated-stack/services",
    )
    expect(screen.getByRole("link", { name: "View first backup" })).toHaveAttribute(
      "href",
      "/backups/catalog/catalog-1",
    )
    const technicalDetails = (await screen.findByText("Technical finish evidence")).closest("details")
    expect(technicalDetails).not.toHaveAttribute("open")
    expect(within(technicalDetails!).getByText("2026-08-08T06:05:00.000Z")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open server" })).toHaveAttribute("data-variant", "default")
    expect(screen.getByRole("button", { name: "Download completion report" })).toHaveAttribute("data-variant", "ghost")
    expect(screen.queryByText("Migration handoff completed")).not.toBeInTheDocument()
    expect(screen.queryByText("Old server retained until")).not.toBeInTheDocument()
    expect(screen.getByText(/Keep the old server available for this retention period/)).toBeInTheDocument()
  })

  it("keeps acceptance durable and retries only the baseline backup", async () => {
    let first = true
    const failedState = {
      ...acceptedState,
      status: "accepted-baseline-backup-failed",
      baselineBackup: {
        ...acceptedState.baselineBackup,
        status: "failed",
        backupId: null,
        catalogEntryId: null,
        failureCode: "baseline_backup_failed",
        failureSummary: "Catalog registration failed.",
      },
    }
    useStateHandler(failedState)
    server.use(
      http.post(retryPath, () => {
        if (first) {
          first = false
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        canonicalState = acceptedState
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(
      <MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />,
    )

    expect(await screen.findByText("Migration accepted; baseline backup needs attention")).toBeInTheDocument()
    expect(await screen.findByText("Catalog registration failed.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Retry baseline backup" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("Migration complete")).toBeInTheDocument()
  })

  it("withholds the finish action while acceptance is blocked", async () => {
    useStateHandler({
      ...readyState,
      status: "blocked",
      acceptanceEligible: false,
      blockers: ["Passed production verification is required."],
    })
    renderWithProviders(
      <MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />,
    )

    expect(await screen.findByText("Finishing is not ready")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Finish migration" })).not.toBeInTheDocument()
  })

  it("keeps German completion concise, with server navigation primary and exact UTC evidence retained", async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    useStateHandler(acceptedState)
    renderWithProviders(<MigrationFinishWorkspace migrationId={migrationId} onChanged={async () => undefined} />)
    expect(await screen.findByText("Migration abgeschlossen")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Server öffnen" })).toHaveAttribute("data-variant", "default")
    expect(screen.getByRole("link", { name: "Erste Sicherung ansehen" })).toHaveAttribute("href", "/backups/catalog/catalog-1")
    expect(await screen.findByText(/Halten Sie den alten Server während dieser Aufbewahrungsfrist verfügbar/)).toBeInTheDocument()
    expect(screen.getByText("2026-08-08T06:05:00.000Z")).toBeInTheDocument()
  })

})
