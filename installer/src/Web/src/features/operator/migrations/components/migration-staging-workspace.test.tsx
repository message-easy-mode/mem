import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import type { MigrationStagingRun } from "@/features/operator/migrations/api/migration-staging"

import { MigrationStagingWorkspace } from "./migration-staging-workspace"

const detail = {
  session: {
    migrationId: "mig_staging",
    displayName: "Private staging test",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "staging",
    status: "staging-ready",
    nextAction: "start-private-staging",
    createdAtUtc: "2026-07-17T00:00:00Z",
    updatedAtUtc: "2026-07-17T00:01:00Z",
    blockerCount: 0, warningCount: 0, advisoryCount: 0, needsAttention: false,
    sourceCount: 1, stackCount: 1,
    historicalCompatibility: {
      usesLegacyNeutralImportContract: false, legacyContractVersion: null,
      legacyContractStatus: null, legacyManifestSha256: null,
      usesCatalogRestorePath: false, catalogEntryCount: 0, restoreSessionCount: 0,
    },
  },
  package: {
    transferMode: "encrypted", status: "package-validated", fileName: "source.zip.age", sizeBytes: 100,
    encryptedSha256: "1".repeat(64), decryptedSha256: "2".repeat(64), uploadedAtUtc: "2026-07-17T00:00:00Z",
    validatedAtUtc: "2026-07-17T00:00:00Z", expiresAtUtc: null, ageRecipient: null, recipientFingerprint: null,
    archiveMigrationId: "source", archiveSourceProduct: "MatrixEasyMode", archiveSourceVersion: "0.1.0", archiveStackCount: 1,
  },
  sources: [], findings: [], linkedObjects: [],
}

const verifiedRun: MigrationStagingRun = {
  stagingRunId: "mstg_001",
  migrationId: "mig_staging",
  candidateArtifactId: "mca_001",
  retryOfStagingRunId: null,
  status: "verified",
  currentStep: "private-verification-complete",
  createdAtUtc: "2026-07-17T00:02:00Z",
  updatedAtUtc: "2026-07-17T00:03:00Z",
  startedAtUtc: "2026-07-17T00:02:00Z",
  completedAtUtc: "2026-07-17T00:03:00Z",
  destroyedAtUtc: null,
  privateOnly: true,
  publicRoutesCreated: false,
  databaseImportSucceeded: true,
  synapseHealthPassed: true,
  elementConfigPresent: true,
  elementConfigSha256: "f".repeat(64),
  elementContainerStarted: true,
  elementHealthPassed: true,
  elementSynapseConnectivityPassed: true,
  elementNetworkAttached: true,
  elementImageReference: "sha256:element-approved",
  synapseImageReference: "sha256:synapse-approved",
  usersCount: 3,
  roomsCount: 2,
  eventsCount: 23,
  matrixServerName: "matrix.example.test",
  failureCode: null,
  failureSummary: null,
  retirementReviewAvailable: true,
}

describe("MigrationStagingWorkspace", () => {
  it("uses browser step-up, reconstructs verified evidence, and offers reviewed retirement", async () => {
    const user = userEvent.setup()
    let runs: typeof verifiedRun[] = []
    let startAttempts = 0

    server.use(
      http.get("/api/operator/migrations/sessions/mig_staging/staging-runs", () => HttpResponse.json(runs)),
      http.post("/api/operator/migrations/sessions/mig_staging/staging-runs", () => {
        startAttempts += 1
        if (startAttempts === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        runs = [verifiedRun]
        return HttpResponse.json(verifiedRun)
      }),

    )

    renderWithProviders(<MigrationStagingWorkspace detail={detail} />)
    expect(await screen.findByText("No private staging runs have been recorded yet.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Start private staging" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("mca_001")).toBeInTheDocument()
    expect(screen.getByText("The runtime is private and no public route was created.")).toBeInTheDocument()
    expect(screen.getByText("23")).toBeInTheDocument()
    expect(screen.getByText("Element runtime health")).toBeInTheDocument()
    expect(screen.getByText("Element is running privately, can serve its patched configuration, and can reach Synapse.")).toBeInTheDocument()
    expect(screen.getByText("f".repeat(64))).toBeInTheDocument()
    expect(screen.getByText(/No Backup Catalog item, normal Restore Session/)).toBeInTheDocument()

    expect(screen.getByRole("button", { name: "Review retirement" })).toBeEnabled()
    expect(screen.queryByRole("button", { name: "Destroy private staging" })).not.toBeInTheDocument()
    expect(startAttempts).toBe(2)
  })

  it("retains failed evidence and sends durable retry ancestry", async () => {
    const user = userEvent.setup()
    let retryId: string | null | undefined
    const failed = {
      ...verifiedRun,
      stagingRunId: "mstg_failed",
      status: "failed-cleaned",
      currentStep: "private-verification-failed-cleaned",
      privateOnly: true,
      databaseImportSucceeded: false,
      synapseHealthPassed: false,
      elementContainerStarted: false,
      elementHealthPassed: false,
      elementSynapseConnectivityPassed: false,
      elementNetworkAttached: false,
      failureCode: "staging_verification_failed",
      failureSummary: "Private evidence was retained.",
      retirementReviewAvailable: false,
    }

    server.use(
      http.get("/api/operator/migrations/sessions/mig_staging/staging-runs", () => HttpResponse.json([failed])),
      http.post("/api/operator/migrations/sessions/mig_staging/staging-runs", async ({ request }) => {
        retryId = (await request.json() as { retryOfStagingRunId?: string }).retryOfStagingRunId
        return HttpResponse.json({ ...verifiedRun, stagingRunId: "mstg_retry", retryOfStagingRunId: "mstg_failed" })
      }),
    )

    renderWithProviders(<MigrationStagingWorkspace detail={detail} />)
    expect(await screen.findByText("Private evidence was retained.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Retry private staging" }))
    await waitFor(() => expect(retryId).toBe("mstg_failed"))
  })
  it("refreshes durable staging history when start returns a recorded failure", async () => {
    const user = userEvent.setup()
    let runs: MigrationStagingRun[] = []
    const failed: MigrationStagingRun = {
      ...verifiedRun,
      stagingRunId: "mstg_recorded_failure",
      status: "failed",
      currentStep: "failed",
      privateOnly: false,
      databaseImportSucceeded: false,
      synapseHealthPassed: false,
      elementConfigPresent: false,
      elementConfigSha256: null,
      elementContainerStarted: false,
      elementHealthPassed: false,
      elementSynapseConnectivityPassed: false,
      elementNetworkAttached: false,
      elementImageReference: null,
      synapseImageReference: null,
      usersCount: null,
      roomsCount: null,
      eventsCount: null,
      failureCode: "staging_failed",
      failureSummary: "The canonical archive material could not be resolved.",
      retirementReviewAvailable: false,
    }

    server.use(
      http.get("/api/operator/migrations/sessions/mig_staging/staging-runs", () => HttpResponse.json(runs)),
      http.post("/api/operator/migrations/sessions/mig_staging/staging-runs", () => {
        runs = [failed]
        return HttpResponse.json(
          { status: "staging_failed", detail: "Private migration staging failed. Review retained evidence before retrying." },
          { status: 500 },
        )
      }),
    )

    renderWithProviders(<MigrationStagingWorkspace detail={detail} />)
    expect(await screen.findByText("No private staging runs have been recorded yet.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Start private staging" }))

    expect(await screen.findByText("Private migration staging failed. Review retained evidence before retrying.")).toBeInTheDocument()
    expect(await screen.findByText("The canonical archive material could not be resolved.")).toBeInTheDocument()
    expect(screen.getByText("mstg_recorded_failure")).toBeInTheDocument()
  })

})
