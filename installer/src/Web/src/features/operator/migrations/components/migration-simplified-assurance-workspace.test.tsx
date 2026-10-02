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

import type { MigrationProductionAuthorityState } from "@/features/operator/migrations/api/migration-production-authority"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import { useMigrationProductionAuthorityState } from "@/features/operator/migrations/hooks/use-migration-production-authority"
import { MigrationSimplifiedAssuranceWorkspace } from "./migration-simplified-assurance-workspace"

const migrationId = "mig_assurance"
const detail: MigrationSessionDetail = {
  session: { migrationId, displayName: "Simplified assurance test", sourceAdapter: "mem-v010", sourceDisplay: "Message Easy Mode 0.1.0", phase: "staging", status: "staging-verified", nextAction: "review-private-staging", createdAtUtc: "2026-07-21T00:00:00Z", updatedAtUtc: "2026-07-21T03:02:00Z", blockerCount: 0, warningCount: 0, advisoryCount: 0, needsAttention: false, sourceCount: 1, stackCount: 1, historicalCompatibility: { usesLegacyNeutralImportContract: false, legacyContractVersion: null, legacyContractStatus: null, legacyManifestSha256: null, usesCatalogRestorePath: false, catalogEntryCount: 0, restoreSessionCount: 0 } },
  package: { transferMode: "encrypted", status: "package-validated", fileName: "preview.memmigration.zip.age", sizeBytes: 4096, encryptedSha256: "2".repeat(64), decryptedSha256: "3".repeat(64), uploadedAtUtc: "2026-07-21T00:05:00Z", validatedAtUtc: "2026-07-21T00:06:00Z", expiresAtUtc: null, ageRecipient: null, recipientFingerprint: null, archiveMigrationId: "source-capture", archiveSourceProduct: "MatrixEasyMode", archiveSourceVersion: "0.1.0", archiveStackCount: 1 },
  packageRevisions: [{ packageRevisionId: "mpr_preview_1", revisionNumber: 1, purpose: "preview", status: "package-validated", retentionState: "active", active: true, transferMode: "encrypted", fileName: "preview.memmigration.zip.age", sizeBytes: 4096, encryptedSha256: "2".repeat(64), decryptedSha256: "3".repeat(64), createdAtUtc: "2026-07-21T00:00:00Z", uploadedAtUtc: "2026-07-21T00:05:00Z", validatedAtUtc: "2026-07-21T00:06:00Z", expiresAtUtc: null, supersededAtUtc: null, retiredAtUtc: null, ageRecipient: null, recipientFingerprint: null, archiveMigrationId: "source-capture", archiveSourceProduct: "MatrixEasyMode", archiveSourceVersion: "0.1.0", archiveStackCount: 1, captureKind: "preview", sourceFrozen: false, rehearsalOnly: true, verifiedFileCount: 22, verifiedExpandedBytes: 4096, validationCode: "validated", validationSummary: "Preview package validated." }],
  sources: [{ sourceId: "source-1", kind: "migration-package", product: "MatrixEasyMode", productVersion: "0.1.0", sourceFingerprint: "4".repeat(64), capturedAtUtc: "2026-07-20T23:57:07Z" }], findings: [], linkedObjects: [],
}
const retained = { stagingRunId: "mstg_verified", migrationId, candidateArtifactId: "mca_verified", retryOfStagingRunId: null, status: "verified", currentStep: "verified", createdAtUtc: "2026-07-21T02:00:00Z", updatedAtUtc: "2026-07-21T03:01:45Z", startedAtUtc: "2026-07-21T03:01:45Z", completedAtUtc: "2026-07-21T03:02:00Z", destroyedAtUtc: null, privateOnly: true, publicRoutesCreated: false, databaseImportSucceeded: true, synapseHealthPassed: true, elementConfigPresent: true, elementConfigSha256: "5".repeat(64), elementContainerStarted: true, elementHealthPassed: true, elementSynapseConnectivityPassed: true, elementNetworkAttached: true, elementImageReference: "sha256:element", synapseImageReference: "sha256:synapse", usersCount: 3, roomsCount: 2, eventsCount: 28, matrixServerName: "matrix-e01affa9.matrixeasyhost.com", failureCode: null, failureSummary: null, retirementReviewAvailable: true }
const empty: MigrationProductionAuthorityState = { status: "not-created", migrationId, authorizesProduction: false, authority: null, detail: "No production authority." }
const active: MigrationProductionAuthorityState = { status: "active", migrationId, authorizesProduction: true, detail: "Authorized.", authority: { productionAuthorityId: "mpa_operator_1", authorityType: "operator-attested-snapshot", status: "active", packageRevisionId: "mpr_preview_1", candidateArtifactId: "mca_verified", stagingRunId: "mstg_verified", encryptedPackageSha256: "2".repeat(64), decryptedArchiveSha256: "3".repeat(64), sourceMigrationId: "source-capture", sourceFingerprint: "4".repeat(64), matrixServerName: "matrix-e01affa9.matrixeasyhost.com", signingKeyIdentitySha256: "6".repeat(64), captureKind: "preview", sourceFrozen: false, rehearsalOnly: true, capturedAtUtc: "2026-07-20T23:57:07Z", usersCount: 3, roomsCount: 2, eventsCount: 28, evidenceSchemaVersion: "migration-production-authority-v1", evidenceSha256: "7".repeat(64), acknowledgementsSchemaVersion: "operator-attested-snapshot-v1", acknowledgementsSha256: "8".repeat(64), createdAtUtc: "2026-07-21T03:10:00Z" } }

function Harness() {
  const query = useMigrationProductionAuthorityState(migrationId)
  return <MigrationSimplifiedAssuranceWorkspace detail={detail} authorityState={query.data} authorityLoading={query.isLoading} authorityError={query.error} authorityFetching={query.isFetching} onRefresh={() => void query.refetch()} />
}

describe("MigrationSimplifiedAssuranceWorkspace", () => {
  it("creates authority only after acknowledgements and step-up", async () => {
    const user = userEvent.setup(); let state = empty; let attempts = 0; let body: unknown
    server.use(
      http.get(`/api/operator/migrations/sessions/${migrationId}/production-authority`, () => HttpResponse.json(state)),
      http.get(`/api/operator/migrations/sessions/${migrationId}/staging-runs`, () => HttpResponse.json([retained])),
      http.post(`/api/operator/migrations/sessions/${migrationId}/production-authority/operator-attested-snapshot`, async ({ request }) => { attempts += 1; body = await request.json(); if (attempts === 1) return HttpResponse.json({ error: "step_up_required", detail: "Step-up" }, { status: 403 }); state = active; return HttpResponse.json(active) }),
    )
    renderWithProviders(<Harness />)
    expect(await screen.findByText("Use this verified snapshot for production")).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText("Users").parentElement).toHaveTextContent("3"))
    const button = screen.getByRole("button", { name: "Use this snapshot for production" }); expect(button).toBeDisabled()
    for (const label of [/Users were instructed/, /writes made after/, /exact verified snapshot/, /retain the old source/, /formal source-freeze/, /rollback assurance is reduced/]) await user.click(screen.getByLabelText(label))
    await user.click(button); await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    await waitFor(() => expect(attempts).toBe(2))
    expect(body).toEqual({ acknowledgeUsersWereInstructedNotToUseSource: true, acknowledgePostCaptureWritesWillNotMigrate: true, acknowledgeSelectedSnapshotBecomesAuthoritative: true, acknowledgeSourceWillBeRetainedUntilVerification: true, acknowledgeNoFormalSourceFreezeEvidence: true, acknowledgeReducedRollbackAssurance: true })
    expect(await screen.findByText("Simplified production authority is active")).toBeInTheDocument()
  })

  it("blocks creation without retained verified staging", async () => {
    server.use(http.get(`/api/operator/migrations/sessions/${migrationId}/production-authority`, () => HttpResponse.json(empty)), http.get(`/api/operator/migrations/sessions/${migrationId}/staging-runs`, () => HttpResponse.json([])))
    renderWithProviders(<Harness />)
    expect(await screen.findByText("A retained verified staging runtime is required")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Use this snapshot for production" })).toBeDisabled()
  })
})
