import { act, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { MemoryRouter } from "react-router-dom"
import type { ReactElement } from "react"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import type { MigrationConversionAttempt } from "@/features/operator/migrations/api/migration-conversions"
import type { MigrationStagingRun } from "@/features/operator/migrations/api/migration-staging"

import { MigrationPrepareAndTestWorkspace } from "./migration-prepare-and-test-workspace"

const detail = {
  session: {
    migrationId: "mig_prepare_test",
    displayName: "Prepare and test",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "staging",
    status: "staging-ready",
    nextAction: "start-private-staging",
    createdAtUtc: "2026-07-25T00:00:00Z",
    updatedAtUtc: "2026-07-25T00:05:00Z",
    blockerCount: 0,
    warningCount: 0,
    advisoryCount: 0,
    needsAttention: false,
    sourceCount: 1,
    stackCount: 1,
    historicalCompatibility: {
      usesLegacyNeutralImportContract: false,
      legacyContractVersion: null,
      legacyContractStatus: null,
      legacyManifestSha256: null,
      usesCatalogRestorePath: false,
      catalogEntryCount: 0,
      restoreSessionCount: 0,
    },
  },
  package: {
    transferMode: "encrypted",
    status: "package-validated",
    fileName: "source.zip.age",
    sizeBytes: 4096,
    encryptedSha256: "1".repeat(64),
    decryptedSha256: "2".repeat(64),
    uploadedAtUtc: "2026-07-25T00:01:00Z",
    validatedAtUtc: "2026-07-25T00:02:00Z",
    expiresAtUtc: null,
    ageRecipient: null,
    recipientFingerprint: null,
    archiveMigrationId: "capture-1",
    archiveSourceProduct: "MatrixEasyMode",
    archiveSourceVersion: "0.1.0",
    archiveStackCount: 1,
  },
  sources: [],
  findings: [],
  linkedObjects: [],
}

const candidateAttempt: MigrationConversionAttempt = {
  conversionAttemptId: "conv_001",
  migrationId: "mig_prepare_test",
  status: "completed",
  currentStep: "completed",
  createdAtUtc: "2026-07-25T00:03:00Z",
  updatedAtUtc: "2026-07-25T00:04:00Z",
  startedAtUtc: "2026-07-25T00:03:00Z",
  completedAtUtc: "2026-07-25T00:04:00Z",
  resultCode: "conversion_completed",
  failureCode: null,
  failureSummary: null,
  sourceStackId: "9230081f-ba21-4e53-8fbe-b3cc7bd1b441",
  candidateArtifactId: "mca_001",
  candidateArtifactKind: "synapse-postgresql-conversion",
  candidateSourcePackageSha256: "3".repeat(64),
  candidateArtifactSha256: "4".repeat(64),
  candidateManifestSha256: "5".repeat(64),
  candidateChecksumsSha256: "6".repeat(64),
  candidateVerificationStatus: "verified",
  candidateRetentionState: "retained",
  candidateCreatedAtUtc: "2026-07-25T00:04:00Z",
  candidateVerifiedAtUtc: "2026-07-25T00:04:00Z",
}

const verifiedRun: MigrationStagingRun = {
  stagingRunId: "mstg_001",
  migrationId: "mig_prepare_test",
  candidateArtifactId: "mca_001",
  retryOfStagingRunId: null,
  status: "verified",
  currentStep: "private-verification-complete",
  createdAtUtc: "2026-07-25T00:05:00Z",
  updatedAtUtc: "2026-07-25T00:06:00Z",
  startedAtUtc: "2026-07-25T00:05:00Z",
  completedAtUtc: "2026-07-25T00:06:00Z",
  destroyedAtUtc: null,
  privateOnly: true,
  publicRoutesCreated: false,
  databaseImportSucceeded: true,
  synapseHealthPassed: true,
  elementConfigPresent: true,
  elementConfigSha256: "7".repeat(64),
  elementContainerStarted: true,
  elementHealthPassed: true,
  elementSynapseConnectivityPassed: true,
  elementNetworkAttached: true,
  elementImageReference: "sha256:element-approved",
  synapseImageReference: "sha256:synapse-approved",
  usersCount: 3,
  roomsCount: 2,
  eventsCount: 28,
  matrixServerName: "matrix.example.test",
  failureCode: null,
  failureSummary: null,
  retirementReviewAvailable: true,
}

const singleStackOptionsHandler = http.get(
  "/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts/options",
  () => HttpResponse.json({
    packageRevisionId: "mpr_preview",
    boundSourceStack: {
      sourceStackId: "9230081f-ba21-4e53-8fbe-b3cc7bd1b441",
      slug: "tester",
      matrixServerName: "matrix-tester.example.test",
    },
  }),
)

function candidateWorkspace() {
  return guidedWorkspace({ migrationId: detail.session.migrationId, detail, guided: {
    hasVerifiedCandidate: true, conversionAttemptId: candidateAttempt.conversionAttemptId, conversionStatus: "completed",
    operationRevisions: { conversion: "conv_001:completed", "private-test": "no-staging" },
  } })
}
let currentWorkspace = candidateWorkspace()
function conversionWorkspace() {
  return guidedWorkspace({ migrationId: detail.session.migrationId, detail, stage: "prepare-and-test", action: "start-conversion",
    guided: { hasVerifiedCandidate: false } })
}
function testedWorkspace(completed = false) {
  return guidedWorkspace({ migrationId: detail.session.migrationId, detail,
    stage: completed ? "finish-migration" : "create-new-server", state: completed ? "completed" : "ready",
    action: completed ? "open-baseline-backup" : "confirm-tested-data", guided: {
      ...candidateWorkspace().guided, currentStageCode: completed ? "finish-migration" : "create-new-server",
      stageState: completed ? "completed" : "ready", nextAction: { code: completed ? "open-baseline-backup" : "confirm-tested-data", enabled: true,
        relatedStage: completed ? "finish-migration" : "create-new-server" },
      stagingRunId: verifiedRun.stagingRunId, stagingStatus: "verified", stagingRetained: true,
      accepted: completed, baselineBackupStatus: completed ? "created" : "not-created",
      operationRevisions: { ...candidateWorkspace().guided.operationRevisions, "private-test": "mstg_001:verified" },
    } })
}
beforeEach(() => { sessionStorage.clear(); currentWorkspace = candidateWorkspace() })
function renderWithProviders(ui: ReactElement) { return renderGuided(<MemoryRouter>{ui}</MemoryRouter>, () => currentWorkspace) }

describe("MigrationPrepareAndTestWorkspace", () => {
  it("opens the authoritative retirement review without requiring the separate technical evidence read", async () => {
    currentWorkspace = guidedWorkspace({ migrationId: detail.session.migrationId, detail, state: "failed", action: "review-staging-retirement",
      guided: { ...candidateWorkspace().guided, stageState: "failed", stagingStatus: "failed", stagingRetained: true,
        stagingRunId: "mstg_failed", nextAction: { code: "review-staging-retirement", enabled: true, relatedStage: "prepare-and-test" } } })
    let posts = 0
    server.use(singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () => HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => HttpResponse.json({}, { status: 503 })),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs/mstg_failed/retirement", () => HttpResponse.json({
        migrationId: "mig_prepare_test", stagingRunId: "mstg_failed", displayName: "Failed private test", canRetire: false,
        blockerCode: "inspection-unavailable", reviewFingerprint: null, operation: null, containerCount: 0, networkCount: 0, workspacePresent: false,
      })),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/staging-runs/mstg_failed/retirement", () => { posts++; return HttpResponse.json({}) }),
    )
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)
    await userEvent.setup().click(await screen.findByRole("button", { name: "Review retirement" }))
    expect(await screen.findByRole("alertdialog", { name: "Retire migration staging" })).toBeInTheDocument()
    expect(await screen.findByText(/Current evidence is unavailable/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
    expect(posts).toBe(0)
  })

  it("rediscovers conversion after a lost start response without showing Failed to fetch", async () => {
    currentWorkspace = conversionWorkspace()
    let attempts: MigrationConversionAttempt[] = []
    let posts = 0
    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json(attempts)),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => HttpResponse.json([])),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () => {
        posts += 1
        attempts = [candidateAttempt]
        currentWorkspace = candidateWorkspace()
        return HttpResponse.error()
      }),
    )
    const onChanged = vi.fn(async () => undefined)
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} onChanged={onChanged} />)
    await userEvent.setup().click(await screen.findByRole("button", { name: "Prepare migration data" }))
    await waitFor(() => expect(screen.getByRole("button", { name: "Run private test" })).toBeEnabled())
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(screen.queryByText("Prepare and test action failed")).not.toBeInTheDocument()
    expect(onChanged).not.toHaveBeenCalled() // Recovery is owned by the canonical workspace observer.
    expect(posts).toBe(1)
  })

  it("rediscovers private staging after a lost start response without replaying the POST", async () => {
    let runs: MigrationStagingRun[] = []
    let posts = 0
    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => HttpResponse.json(runs)),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => {
        posts += 1
        runs = [verifiedRun]
        currentWorkspace = testedWorkspace()
        return HttpResponse.error()
      }),
    )
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)
    await waitFor(() => expect(screen.getByRole("button", { name: "Run private test" })).toBeEnabled())
    await userEvent.setup().click(screen.getByRole("button", { name: "Run private test" }))
    // The workspace confirms success before the evidence query resolves. Wait
    // for the detailed result, not the temporary summary with the same text.
    expect(await screen.findByRole("heading", { name: "Private test passed", level: 3 })).toBeInTheDocument()
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(posts).toBe(1)
  })

  it.each([
    ["conversion", "review-conversion"],
    ["staging", "destroy-private-staging"],
    ["completed", "view-completion"],
  ])("ignores stale session phase %s / action %s when the workspace allows private testing", async (phase, nextAction) => {
    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => HttpResponse.json([])),
    )
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={{
      ...detail, session: { ...detail.session, phase, nextAction },
    }} />)
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeEnabled()
  })

  it("withholds private start when the workspace blocks it, even if specialist evidence looks ready", async () => {
    currentWorkspace = guidedWorkspace({ migrationId: detail.session.migrationId, detail, state: "blocked", action: "open-technical-details" })
    server.use(singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () => HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => HttpResponse.json([])))
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeDisabled()
  })

  it("does not let unavailable technical staging evidence veto authoritative readiness", async () => {
    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json({ detail: "State unavailable" }, { status: 503 })),
    )
    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeEnabled()
  })

  it("presents one preparation action instead of separate conversion and staging workspaces", async () => {
    currentWorkspace = conversionWorkspace()
    const user = userEvent.setup()
    let attempts: MigrationConversionAttempt[] = []
    let posted = false

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json(attempts),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([]),
      ),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () => {
        posted = true
        attempts = [candidateAttempt]
        currentWorkspace = candidateWorkspace()
        return HttpResponse.json(candidateAttempt)
      }),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    const prepareTitle = await screen.findByText("Prepare and test")
    expect(prepareTitle).toBeInTheDocument()
    expect(prepareTitle.querySelector("svg")).toHaveClass("lucide-database", "shrink-0")
    expect(await screen.findByRole("heading", { name: "Prepare migration data" })).toBeInTheDocument()
    expect(screen.queryByText("Conversion workspace")).not.toBeInTheDocument()
    expect(screen.queryByText("Private staging workspace")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Prepare migration data" }))

    await waitFor(() => expect(posted).toBe(true))
    expect(await screen.findByText("Migration data prepared")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Run private test" })).toBeEnabled()
  })

  it("uses the existing step-up contract and shows a concise private-test result", async () => {
    const user = userEvent.setup()
    let runs: MigrationStagingRun[] = []
    let starts = 0

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json(runs),
      ),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () => {
        starts += 1
        if (starts === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        runs = [verifiedRun]
        currentWorkspace = testedWorkspace()
        return HttpResponse.json(verifiedRun)
      }),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    await user.click(await screen.findByRole("button", { name: "Run private test" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    // The workspace confirms success before the evidence query resolves. Wait
    // for the detailed result, not the temporary summary with the same text.
    expect(await screen.findByRole("heading", { name: "Private test passed", level: 3 })).toBeInTheDocument()
    expect(await screen.findByText("Matrix and Element started successfully in private, and no public route was created.")).toBeInTheDocument()
    expect(await screen.findByText("Database imported")).toBeInTheDocument()
    expect(await screen.findByText("Matrix and Element healthy")).toBeInTheDocument()
    expect(await screen.findByText("Private only; no public routes")).toBeInTheDocument()
    expect(await screen.findByText("28")).toBeInTheDocument()
    expect(await screen.findByText("The temporary test runtime is retained until the new server is accepted and its first native backup is created.")).toBeInTheDocument()
    expect(screen.getByText("Technical preparation evidence")).toBeInTheDocument()
    expect(starts).toBe(2) // One explicit step-up rejection, then one authorized submission.
  })

  it("keeps workspace-confirmed success visible while private-test evidence is loading", async () => {
    currentWorkspace = testedWorkspace()
    let releaseEvidence!: () => void
    const pendingEvidence = new Promise<void>((resolve) => { releaseEvidence = resolve })
    let evidenceReads = 0
    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt])),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", async () => {
        evidenceReads += 1
        await pendingEvidence
        return HttpResponse.json([verifiedRun])
      }),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)
    try {
      // Deliberately hold evidence so this assertion observes the summary path.
      expect(await screen.findByText("Private test passed")).toBeInTheDocument()
      expect(screen.queryByRole("heading", { name: "Private test passed", level: 3 })).not.toBeInTheDocument()
      expect(screen.queryByText("Database imported")).not.toBeInTheDocument()
      expect(screen.queryByRole("button", { name: "Run private test" })).not.toBeInTheDocument()
      await waitFor(() => expect(evidenceReads).toBe(1))
    } finally {
      await act(async () => { releaseEvidence() })
    }

    // Evidence enriches the confirmed result; it does not decide progression.
    expect(await screen.findByRole("heading", { name: "Private test passed", level: 3 })).toBeInTheDocument()
    expect(screen.getByText("Database imported")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Run private test" })).not.toBeInTheDocument()
  })

  it("presents an intentionally retired private test as removed rather than failed", async () => {
    currentWorkspace = guidedWorkspace({
      migrationId: detail.session.migrationId,
      detail,
      stage: "prepare-and-test",
      state: "action-required",
      action: "recreate-private-test",
      guided: {
        ...candidateWorkspace().guided,
        currentStageCode: "prepare-and-test",
        stageState: "action-required",
        stagingRunId: "mstg_removed",
        stagingStatus: "destroyed",
        stagingRetained: false,
        nextAction: { code: "recreate-private-test", enabled: true, relatedStage: "prepare-and-test" },
        operationRevisions: { ...candidateWorkspace().guided.operationRevisions, "private-test": "mstg_removed:destroyed" },
      },
    })
    const removedRun: MigrationStagingRun = {
      ...verifiedRun,
      stagingRunId: "mstg_removed",
      status: "destroyed",
      currentStep: "private-verification-complete-runtime-removed",
      destroyedAtUtc: "2026-07-25T01:00:00Z",
      retirementReviewAvailable: false,
    }

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([removedRun]),
      ),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    expect(await screen.findByText("Private test removed")).toBeInTheDocument()
    expect(screen.getByText("The previous private-test runtime was deliberately retired. Historical test evidence is retained. Create another private test before continuing.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create another private test" })).toBeEnabled()
    expect(screen.queryByText("Private test failed")).not.toBeInTheDocument()
    expect(screen.queryByText("The failed test evidence was retained. Review it before retrying.")).not.toBeInTheDocument()
  })

  it("shows completed automatic cleanup without offering premature manual destruction", async () => {
    currentWorkspace = testedWorkspace(true)
    const removedRun: MigrationStagingRun = {
      ...verifiedRun,
      currentStep: "private-verification-complete-runtime-removed",
      destroyedAtUtc: "2026-07-25T01:00:00Z",
      retirementReviewAvailable: false,
    }

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([removedRun]),
      ),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    expect(await screen.findByText("Temporary test runtime removed after migration completion.")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Retry cleanup" })).not.toBeInTheDocument()
  })

  it("offers a review, not immediate destruction, when completed-migration cleanup failed", async () => {
    currentWorkspace = testedWorkspace(true)
    const cleanupFailedRun: MigrationStagingRun = {
      ...verifiedRun,
      currentStep: "private-verification-complete-cleanup-required",
      failureCode: "staging_cleanup_failed",
      failureSummary: "Temporary runtime cleanup failed safely.",
      retirementReviewAvailable: true,
    }

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([cleanupFailedRun]),
      ),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    expect(await screen.findByText("Temporary test cleanup needs attention")).toBeInTheDocument()
    await userEvent.setup().click(screen.getByText("Technical preparation evidence"))
    expect(screen.getByRole("button", { name: "Review retirement" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Retire staging" })).not.toBeInTheDocument()
  })



  it("offers a review for an older completed migration without enabling direct cleanup", async () => {
    currentWorkspace = testedWorkspace(true)
    const completedDetail = {
      ...detail,
      session: {
        ...detail.session,
        phase: "completed",
        status: "completed",
        nextAction: "view-completion",
      },
    }

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([candidateAttempt]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([verifiedRun]),
      ),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={completedDetail} />)

    await userEvent.setup().click(await screen.findByText("Technical preparation evidence"))
    expect(screen.getByRole("button", { name: "Review retirement" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Retire staging" })).not.toBeInTheDocument()
  })

  it("shows the package-bound source stack and starts without a second selection", async () => {
    currentWorkspace = conversionWorkspace()
    const user = userEvent.setup()
    let postedBody: unknown = null

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([]),
      ),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", async ({ request }) => {
        postedBody = await request.json()
        return HttpResponse.json({ ...candidateAttempt, conversionAttemptId: "conv_bound" })
      }),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    expect(await screen.findByRole("heading", { name: "Source stack bound to this package" })).toBeInTheDocument()
    expect(screen.getByText("tester")).toBeInTheDocument()
    expect(screen.queryByRole("radio")).not.toBeInTheDocument()

    const prepare = screen.getByRole("button", { name: "Prepare migration data" })
    expect(prepare).toBeEnabled()
    await user.click(prepare)

    await waitFor(() => expect(postedBody).toEqual({}))
  })

  it("retains failed preparation evidence and sends retry ancestry", async () => {
    currentWorkspace = guidedWorkspace({ migrationId: detail.session.migrationId, detail, state: "failed", action: "retry-conversion",
      guided: { hasVerifiedCandidate: false, conversionAttemptId: "conv_failed", conversionStatus: "failed" } })
    const user = userEvent.setup()
    let retryId: string | null | undefined
    const failed: MigrationConversionAttempt = {
      ...candidateAttempt,
      conversionAttemptId: "conv_failed",
      status: "failed",
      currentStep: "conversion-failed",
      failureCode: "conversion_failed",
      failureSummary: "Conversion evidence was retained.",
      candidateArtifactId: null,
      candidateArtifactKind: null,
      candidateSourcePackageSha256: null,
      candidateArtifactSha256: null,
      candidateManifestSha256: null,
      candidateChecksumsSha256: null,
      candidateVerificationStatus: null,
      candidateRetentionState: null,
      candidateCreatedAtUtc: null,
      candidateVerifiedAtUtc: null,
    }

    server.use(
      singleStackOptionsHandler,
      http.get("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", () =>
        HttpResponse.json([failed]),
      ),
      http.get("/api/operator/migrations/sessions/mig_prepare_test/staging-runs", () =>
        HttpResponse.json([]),
      ),
      http.post("/api/operator/migrations/sessions/mig_prepare_test/conversion-attempts", async ({ request }) => {
        retryId = (await request.json() as { retryOfConversionAttemptId?: string }).retryOfConversionAttemptId
        return HttpResponse.json(candidateAttempt)
      }),
    )

    renderWithProviders(<MigrationPrepareAndTestWorkspace detail={detail} />)

    expect((await screen.findAllByText("Conversion evidence was retained.")).length).toBeGreaterThan(0)
    await user.click(screen.getByRole("button", { name: "Retry preparation" }))
    await waitFor(() => expect(retryId).toBe("conv_failed"))
  })
})
