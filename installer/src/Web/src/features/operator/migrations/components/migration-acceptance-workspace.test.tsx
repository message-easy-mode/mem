import { HttpResponse, http } from "msw"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { MigrationAcceptanceWorkspace } from "./migration-acceptance-workspace"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

const simplifiedAssurance = {
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

const blockedState = {
  source: "control-plane",
  status: "blocked",
  migrationId: "mig-new",
  acceptanceEligible: false,
  accepted: false,
  publicVerificationRequired: true,
  assurance: simplifiedAssurance,
  acceptance: null,
  legacyRetention: null,
  baselineBackup: null,
  blockers: ["Fresh durable production verification is required before acceptance."],
  warnings: ["Legacy source resources remain retained."],
  detail: "Acceptance remains unavailable until fresh durable production verification is complete.",
}

const readyState = { ...blockedState, status: "ready-for-acceptance", acceptanceEligible: true, blockers: [] }
const acceptedState = {
  ...readyState,
  status: "accepted-baseline-backup-created",
  acceptanceEligible: false,
  accepted: true,
  publicVerificationRequired: false,
  acceptance: {
    acceptanceId: "macc-1",
    executionId: "exec-1",
    candidateArtifactId: "mca-1",
    stagingRunId: "mstg-1",
    publicCutoverAtUtc: "2026-07-18T01:00:00Z",
    acceptedAtUtc: "2026-07-18T01:05:00Z",
    acceptedBy: "owner",
    note: null,
    publicVerification: {
      status: "passed",
      attempted: true,
      passed: true,
      checkCount: 12,
      failedCheckCount: 0,
      evidenceSha256: "a".repeat(64),
      detail: "The durable production-verification evidence was bound to acceptance.",
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
    createdAtUtc: "2026-07-18T01:05:00Z",
    retainUntilUtc: "2026-08-01T01:05:00Z",
    cleanupEligibleAtUtc: "2026-08-01T01:05:00Z",
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
    createdAtUtc: "2026-07-18T01:05:00Z",
    updatedAtUtc: "2026-07-18T01:06:00Z",
    startedAtUtc: "2026-07-18T01:05:00Z",
    completedAtUtc: "2026-07-18T01:06:00Z",
    targetStackSlug: "migrated-stack",
    candidateId: "candidate-1",
    privateRuntimeId: "11111111-1111-1111-1111-111111111111",
    backupId: "backup-1",
    catalogEntryId: "catalog-1",
    backupCreatedAtUtc: "2026-07-18T01:06:00Z",
    backupTotalBytes: 1234,
    backupTotalFiles: 5,
    backupWarningCount: 0,
    failureCode: null,
    failureSummary: null,
    detail: "The first native MEM baseline backup was created and entered the Backup Catalog.",
  },
  blockers: [],
  warnings: ["Quick rollback has ended."],
  detail: "Migration acceptance and retention are durable.",
}

const productionAdoptionState = {
  source: "control-plane",
  status: "production-verified",
  migrationId: "mig-new",
  planPrepared: true,
  detail: "Production adoption is verified.",
  plan: {
    runtimeStackId: "11111111-1111-1111-1111-111111111111",
    targetStackSlug: "migrated-stack",
    productionVerification: {
      verificationId: "mpv-1",
      status: "passed",
      startedAtUtc: "2026-07-18T01:01:00Z",
      completedAtUtc: "2026-07-18T01:02:00Z",
      validUntilUtc: "2026-07-18T02:02:00Z",
      fresh: true,
      passed: true,
      checkCount: 12,
      failedCheckCount: 0,
      evidenceSha256: "a".repeat(64),
      readinessReportId: "22222222-2222-2222-2222-222222222222",
      checks: [],
      failureCode: null,
      failureSummary: null,
    },
  },
}

const blockedProductionAdoptionState = {
  ...productionAdoptionState,
  status: "production-verification-failed",
  plan: {
    ...productionAdoptionState.plan,
    productionVerification: {
      ...productionAdoptionState.plan.productionVerification,
      status: "failed",
      fresh: false,
      passed: false,
      failedCheckCount: 2,
      failureCode: "production_verification_failed",
      failureSummary: "Two public checks failed.",
    },
  },
}

const acceptancePath = "/api/operator/migrations/sessions/mig-new/acceptance"
const adoptionPath = "/api/operator/migrations/sessions/mig-new/production-adoption"
const baselineRetryPath = "/api/operator/migrations/sessions/mig-new/baseline-backup/retry"
const completionReportPath = "/api/operator/migrations/sessions/mig-new/acceptance/report"

function useReadHandlers(acceptanceState: object, adoptionState: object = productionAdoptionState) {
  server.use(
    http.get(acceptancePath, () => HttpResponse.json(acceptanceState)),
    http.get(adoptionPath, () => HttpResponse.json(adoptionState)),
  )
}

describe("MigrationAcceptanceWorkspace", () => {
  it.skip("shows failed production-verification authority and keeps acceptance unavailable", async () => {
    useReadHandlers(blockedState, blockedProductionAdoptionState)
    renderWithProviders(<MigrationAcceptanceWorkspace migrationId="mig-new" />)

    expect(await screen.findByText("Acceptance and legacy retention")).toBeInTheDocument()
    expect(screen.getByText("Production verification is not ready for acceptance")).toBeInTheDocument()
    expect(screen.getByText("Two public checks failed.")).toBeInTheDocument()
    expect(screen.getByText("Fresh durable production verification is required before acceptance.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Accept migration" })).toBeDisabled()
  })

  it.skip("binds fresh verification through step-up and hands off to the normal stack and Backup Catalog", async () => {
    let first = true
    let body: Record<string, unknown> | null = null
    useReadHandlers(readyState)
    server.use(
      http.post(acceptancePath, async ({ request }) => {
        body = await request.json() as Record<string, unknown>
        if (first) {
          first = false
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationAcceptanceWorkspace migrationId="mig-new" />)

    await screen.findByText("Production verification passed")
    expect(screen.getByText("mpv-1")).toBeInTheDocument()
    expect(screen.getByText("12/12")).toBeInTheDocument()
    for (const checkbox of screen.getAllByRole("checkbox")) await user.click(checkbox)
    await user.click(screen.getByRole("button", { name: "Accept migration" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("macc-1")).toBeInTheDocument()
    expect(screen.getByText("Simplified assurance — operator-attested snapshot")).toBeInTheDocument()
    expect(screen.getByText("mpauth-1")).toBeInTheDocument()
    expect(screen.getByText("reduced")).toBeInTheDocument()
    expect(screen.getByText("mlr-1")).toBeInTheDocument()
    expect(screen.getByText("mbh-1")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open baseline backup" })).toHaveAttribute("href", "/backups/catalog/catalog-1")
    expect(screen.getAllByRole("link", { name: "Open normal Runtime Stack" })[0]).toHaveAttribute("href", "/stacks/migrated-stack")
    expect(screen.getByText("Source package retained")).toBeInTheDocument()
    expect(screen.getByText("Legacy source resources retained")).toBeInTheDocument()

    const serialized = JSON.stringify(body).toLowerCase()
    expect(serialized).not.toContain("executionid")
    expect(serialized).not.toContain("candidateartifactid")
    expect(serialized).not.toContain("stagingrunid")
    expect(serialized).not.toContain("runtimestackid")
    expect(serialized).not.toContain("verificationid")
    expect(serialized).not.toContain("catalog")
    expect(serialized).not.toContain("restore")
  })

  it("continues an accepted pending baseline through fresh step-up", async () => {
    let first = true
    const pendingState = {
      ...acceptedState,
      status: "accepted-baseline-backup-pending",
      baselineBackup: {
        ...acceptedState.baselineBackup,
        status: "pending",
        backupId: null,
        catalogEntryId: null,
        backupCreatedAtUtc: null,
        completedAtUtc: null,
        detail: "The first native MEM baseline backup is pending.",
      },
    }
    useReadHandlers(pendingState)
    server.use(
      http.post(baselineRetryPath, () => {
        if (first) {
          first = false
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationAcceptanceWorkspace migrationId="mig-new" />)
    await screen.findByText("Baseline backup pending")
    await user.click(screen.getByRole("button", { name: "Continue baseline backup" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("Baseline backup created")).toBeInTheDocument()
  })

  it("retries a failed baseline backup through fresh step-up without browser-owned runtime identifiers", async () => {
    let first = true
    const failedState = {
      ...acceptedState,
      status: "accepted-baseline-backup-failed",
      baselineBackup: {
        ...acceptedState.baselineBackup,
        status: "failed",
        attemptCount: 2,
        catalogEntryId: null,
        backupId: null,
        failureCode: "baseline_backup_failed",
        failureSummary: "Catalog registration failed.",
      },
    }
    useReadHandlers(failedState)
    server.use(
      http.post(baselineRetryPath, () => {
        if (first) {
          first = false
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        return HttpResponse.json(acceptedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationAcceptanceWorkspace migrationId="mig-new" />)
    await screen.findByText("Baseline backup failed")
    expect(screen.getByText("baseline_backup_failed")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Retry baseline backup" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("Baseline backup created")).toBeInTheDocument()
  })
  it("downloads the hash-bound completion report after step-up", async () => {
    let attempts = 0
    const createObjectUrl = vi.fn(() => "blob:migration-completion")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)

    useReadHandlers(acceptedState)
    server.use(
      http.get(completionReportPath, () => {
        attempts += 1
        if (attempts === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        return new HttpResponse(JSON.stringify({ schemaVersion: "mem.migration.acceptance-completion.v1" }), {
          headers: {
            "Content-Type": "application/json",
            "Content-Disposition": 'attachment; filename="mcompletion-macc-1.mem-migration-completion.json"',
          },
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationAcceptanceWorkspace migrationId="mig-new" />)

    await user.click(await screen.findByRole("button", { name: "Download migration completion report" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(attempts).toBe(2)
    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(anchorClick).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:migration-completion")
    anchorClick.mockRestore()
  })

})
