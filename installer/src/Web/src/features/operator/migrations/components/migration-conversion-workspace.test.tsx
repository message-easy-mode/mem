import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { MigrationConversionWorkspace } from "./migration-conversion-workspace"

const detail = {
  session: {
    migrationId: "mig_conversion",
    displayName: "Conversion test",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "source-assessment",
    status: "package-validated",
    nextAction: "review-source",
    createdAtUtc: "2026-07-17T00:00:00Z",
    updatedAtUtc: "2026-07-17T00:00:00Z",
    blockerCount: 0, warningCount: 0, advisoryCount: 0, needsAttention: false,
    sourceCount: 1, stackCount: 1,
    historicalCompatibility: {
      usesLegacyNeutralImportContract: false, legacyContractVersion: null, legacyContractStatus: null,
      legacyManifestSha256: null, usesCatalogRestorePath: false, catalogEntryCount: 0, restoreSessionCount: 0,
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

describe("MigrationConversionWorkspace", () => {
  it("starts conversion and reconstructs the verified candidate from durable attempts", async () => {
    const user = userEvent.setup()
    let started = false
    const candidate = {
      conversionAttemptId: "conv_001", migrationId: "mig_conversion", status: "completed", currentStep: "candidate-created",
      createdAtUtc: "2026-07-17T00:00:00Z", updatedAtUtc: "2026-07-17T00:01:00Z",
      startedAtUtc: "2026-07-17T00:00:01Z", completedAtUtc: "2026-07-17T00:01:00Z", resultCode: "0",
      failureCode: null, failureSummary: null, candidateArtifactId: "mca_001",
      candidateArtifactKind: "synapse-postgresql-conversion", candidateSourcePackageSha256: "2".repeat(64),
      candidateArtifactSha256: "3".repeat(64), candidateManifestSha256: "4".repeat(64),
      candidateChecksumsSha256: "5".repeat(64), candidateVerificationStatus: "verified", candidateRetentionState: "active",
      candidateCreatedAtUtc: "2026-07-17T00:01:00Z", candidateVerifiedAtUtc: "2026-07-17T00:01:00Z",
    }

    server.use(
      http.get("/api/operator/migrations/sessions/mig_conversion/conversion-attempts", () =>
        HttpResponse.json(started ? [candidate] : []),
      ),
      http.post("/api/operator/migrations/sessions/mig_conversion/conversion-attempts", () => {
        started = true
        return HttpResponse.json(candidate)
      }),
    )

    renderWithProviders(<MigrationConversionWorkspace detail={detail} />)
    expect(await screen.findByText("No conversion attempts have been recorded yet.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Start conversion" }))
    expect(await screen.findByText("mca_001")).toBeInTheDocument()
    expect(screen.getByText("3".repeat(64))).toBeInTheDocument()
    expect(screen.getByText(/No Backup Catalog item or Restore Session was created/)).toBeInTheDocument()
  })

  it("keeps a failed attempt visible and retries only that attempt", async () => {
    const user = userEvent.setup()
    let retryId: string | null | undefined
    const failed = {
      conversionAttemptId: "conv_failed", migrationId: "mig_conversion", status: "failed", currentStep: "failed",
      createdAtUtc: "2026-07-17T00:00:00Z", updatedAtUtc: "2026-07-17T00:01:00Z", startedAtUtc: null, completedAtUtc: null,
      resultCode: null, failureCode: "conversion_failed", failureSummary: "Conversion evidence was retained.",
      candidateArtifactId: null, candidateArtifactKind: null, candidateSourcePackageSha256: null, candidateArtifactSha256: null,
      candidateManifestSha256: null, candidateChecksumsSha256: null, candidateVerificationStatus: null, candidateRetentionState: null,
      candidateCreatedAtUtc: null, candidateVerifiedAtUtc: null,
    }
    server.use(
      http.get("/api/operator/migrations/sessions/mig_conversion/conversion-attempts", () => HttpResponse.json([failed])),
      http.post("/api/operator/migrations/sessions/mig_conversion/conversion-attempts", async ({ request }) => {
        retryId = (await request.json() as { retryOfConversionAttemptId?: string }).retryOfConversionAttemptId
        return HttpResponse.json({ ...failed, conversionAttemptId: "conv_retry", status: "completed" })
      }),
    )
    renderWithProviders(<MigrationConversionWorkspace detail={detail} />)
    expect(await screen.findByText("Conversion evidence was retained.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Retry conversion" }))
    await waitFor(() => expect(retryId).toBe("conv_failed"))
  })
})
