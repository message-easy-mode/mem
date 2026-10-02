import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import { MigrationFinalPackageHandoff } from "./migration-final-package-handoff"

const recipient =
  "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq"

function createDetail(
  finalStatus: "none" | "awaiting-package" | "package-validated",
): MigrationSessionDetail {
  const finalRevision = finalStatus === "none"
    ? []
    : [{
        packageRevisionId: "mpr_final",
        revisionNumber: 2,
        purpose: "final",
        status: finalStatus,
        retentionState: "active",
        active: true,
        transferMode: "encrypted",
        fileName: finalStatus === "package-validated"
          ? "final.memmigration.zip.age"
          : null,
        sizeBytes: finalStatus === "package-validated" ? 2048 : null,
        encryptedSha256: finalStatus === "package-validated" ? "c".repeat(64) : null,
        decryptedSha256: finalStatus === "package-validated" ? "d".repeat(64) : null,
        createdAtUtc: "2026-07-18T00:00:00Z",
        uploadedAtUtc: finalStatus === "package-validated" ? "2026-07-18T00:10:00Z" : null,
        validatedAtUtc: finalStatus === "package-validated" ? "2026-07-18T00:12:00Z" : null,
        expiresAtUtc: "2026-07-19T00:00:00Z",
        supersededAtUtc: null,
        retiredAtUtc: null,
        ageRecipient: recipient,
        recipientFingerprint: "AAAA-BBBB-CCCC-DDDD",
        archiveMigrationId: finalStatus === "package-validated" ? "source-final" : null,
        archiveSourceProduct: finalStatus === "package-validated" ? "MatrixEasyMode" : null,
        archiveSourceVersion: finalStatus === "package-validated" ? "0.1.0" : null,
        archiveStackCount: finalStatus === "package-validated" ? 1 : null,
        captureKind: finalStatus === "package-validated" ? "final" : null,
        sourceFrozen: finalStatus === "package-validated" ? true : null,
        rehearsalOnly: finalStatus === "package-validated" ? false : null,
        verifiedFileCount: finalStatus === "package-validated" ? 14 : null,
        verifiedExpandedBytes: finalStatus === "package-validated" ? 4096 : null,
        validationCode: finalStatus === "package-validated"
          ? "final-package-authority-selected"
          : null,
        validationSummary: finalStatus === "package-validated"
          ? "Final frozen package validated and source identity matched."
          : null,
      }]

  return {
    session: {
      migrationId: "mig_final_handoff",
      displayName: "Final handoff",
      sourceAdapter: "mem-v010",
      sourceDisplay: "Message Easy Mode 0.1.0",
      phase: "cutover",
      status: "staging-verified",
      nextAction: "review-cutover",
      createdAtUtc: "2026-07-16T00:00:00Z",
      updatedAtUtc: "2026-07-18T00:00:00Z",
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
      fileName: "preview.memmigration.zip.age",
      sizeBytes: 1024,
      encryptedSha256: "a".repeat(64),
      decryptedSha256: "b".repeat(64),
      uploadedAtUtc: "2026-07-16T00:10:00Z",
      validatedAtUtc: "2026-07-16T00:12:00Z",
      expiresAtUtc: "2026-07-17T00:00:00Z",
      ageRecipient: recipient,
      recipientFingerprint: "1111-2222-3333-4444",
      archiveMigrationId: "source-preview",
      archiveSourceProduct: "MatrixEasyMode",
      archiveSourceVersion: "0.1.0",
      archiveStackCount: 1,
    },
    packageRevisions: [
      {
        packageRevisionId: "mpr_preview",
        revisionNumber: 1,
        purpose: "preview",
        status: "package-validated",
        retentionState: "active",
        active: true,
        transferMode: "encrypted",
        fileName: "preview.memmigration.zip.age",
        sizeBytes: 1024,
        encryptedSha256: "a".repeat(64),
        decryptedSha256: "b".repeat(64),
        createdAtUtc: "2026-07-16T00:00:00Z",
        uploadedAtUtc: "2026-07-16T00:10:00Z",
        validatedAtUtc: "2026-07-16T00:12:00Z",
        expiresAtUtc: "2026-07-17T00:00:00Z",
        supersededAtUtc: null,
        retiredAtUtc: null,
        ageRecipient: recipient,
        recipientFingerprint: "1111-2222-3333-4444",
        archiveMigrationId: "source-preview",
        archiveSourceProduct: "MatrixEasyMode",
        archiveSourceVersion: "0.1.0",
        archiveStackCount: 1,
        captureKind: "preview",
        sourceFrozen: false,
        rehearsalOnly: true,
        verifiedFileCount: 14,
        verifiedExpandedBytes: 2048,
        validationCode: "validated",
        validationSummary: "Preview validated",
      },
      ...finalRevision,
    ],
    sources: [],
    findings: [],
    linkedObjects: [],
  }
}

describe("MigrationFinalPackageHandoff", () => {
  it("creates the final recipient through browser step-up and refetches the session", async () => {
    const user = userEvent.setup()
    const onChanged = vi.fn().mockResolvedValue(undefined)
    let attempts = 0
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig_final_handoff/package-revisions/final-recipient",
        () => {
          attempts += 1
          if (attempts === 1) {
            return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
          }
          return HttpResponse.json({
            migrationId: "mig_final_handoff",
            packageRevisionId: "mpr_final",
            revisionNumber: 2,
            purpose: "final",
            status: "awaiting-package",
            ageRecipient: recipient,
            recipientFingerprint: "AAAA-BBBB-CCCC-DDDD",
            createdAtUtc: "2026-07-18T00:00:00Z",
            expiresAtUtc: "2026-07-19T00:00:00Z",
            resumedExisting: false,
          })
        },
      ),
    )

    renderWithProviders(
      <MigrationFinalPackageHandoff detail={createDetail("none")} onChanged={onChanged} />,
    )

    await user.click(screen.getByRole("button", { name: "Create final recipient" }))
    expect(await screen.findByRole("button", { name: "Complete step-up" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))

    expect(attempts).toBe(2)
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  it("shows a revision-bound command and final upload control", async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <MigrationFinalPackageHandoff
        detail={createDetail("awaiting-package")}
        onChanged={vi.fn().mockResolvedValue(undefined)}
      />,
    )

    expect(screen.getByText("mpr_final")).toBeInTheDocument()
    expect(screen.queryByText(/AGE-SECRET-KEY/)).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Download final migration request" })).toBeInTheDocument()
    await user.click(screen.getByText("Advanced: use the command line instead"))
    await user.click(screen.getByRole("button", { name: "Show command" }))

    const command = screen.getByLabelText("Generated final source command")
    expect(command).toHaveTextContent("--package-revision-id 'mpr_final'")
    expect(command).toHaveTextContent("--require-final-frozen")
    expect(command).not.toHaveTextContent("--archive")
    expect(command).not.toHaveTextContent("AGE-SECRET-KEY")
    expect(
      screen.getByLabelText("Encrypted final migration package"),
    ).toBeInTheDocument()
  })

  it("downloads the revision-bound final source request", async () => {
    const user = userEvent.setup()
    const createObjectUrl = vi.fn(() => "blob:final-source-request")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)
    server.use(
      http.get(
        "/api/operator/migrations/sessions/mig_final_handoff/package-revisions/final/source-request",
        () => HttpResponse.json(
          { schema: "mem-secure-intake-request", schemaVersion: 1, requestKind: "final" },
          { headers: { "Content-Disposition": 'attachment; filename="final-request.json"' } },
        ),
      ),
    )

    renderWithProviders(
      <MigrationFinalPackageHandoff
        detail={createDetail("awaiting-package")}
        onChanged={vi.fn().mockResolvedValue(undefined)}
      />,
    )
    await user.click(screen.getByRole("button", { name: "Download final migration request" }))

    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(click).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:final-source-request")
  })

  it("uploads the final package through browser step-up and refetches durable state", async () => {
    const user = userEvent.setup()
    const onChanged = vi.fn().mockResolvedValue(undefined)
    let attempts = 0
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig_final_handoff/package-revisions/final/package",
        async ({ request }) => {
          attempts += 1
          if (attempts === 1) {
            return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
          }
          expect(request.headers.get("content-type")).toContain("multipart/form-data; boundary=")
          expect(request.body).not.toBeNull()
          return HttpResponse.json({
            migrationId: "mig_final_handoff",
            packageRevisionId: "mpr_final",
            revisionNumber: 2,
            status: "package-validated",
            packageFileName: "final.memmigration.zip.age",
            packageSizeBytes: 2048,
            encryptedPackageSha256: "c".repeat(64),
            decryptedArchiveSha256: "d".repeat(64),
            archiveMigrationId: "source-final",
            startSourceFingerprint: "e".repeat(64),
            completionSourceFingerprint: "e".repeat(64),
            archiveStackCount: 1,
            captureKind: "final",
            sourceFrozen: true,
            rehearsalOnly: false,
            validatedAtUtc: "2026-07-18T00:12:00Z",
            authoritySelected: true,
          })
        },
      ),
    )

    renderWithProviders(
      <MigrationFinalPackageHandoff
        detail={createDetail("awaiting-package")}
        onChanged={onChanged}
      />,
    )

    await user.upload(
      screen.getByLabelText("Encrypted final migration package"),
      new File(["encrypted"], "final.memmigration.zip.age"),
    )
    await user.click(
      screen.getByRole("button", { name: "Upload and validate final package" }),
    )
    expect(await screen.findByRole("button", { name: "Complete step-up" })).toBeInTheDocument()
    const packageInput = screen.getByLabelText("Encrypted final migration package") as HTMLInputElement
    expect(packageInput.files?.[0]?.name).toBe("final.memmigration.zip.age")
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))

    expect(attempts).toBe(2)
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  it("shows final authority evidence after validation", () => {
    renderWithProviders(
      <MigrationFinalPackageHandoff
        detail={createDetail("package-validated")}
        onChanged={vi.fn().mockResolvedValue(undefined)}
      />,
    )

    expect(screen.getByText("Final package revision validated")).toBeInTheDocument()
    expect(screen.getByText("Final frozen package validated and source identity matched.")).toBeInTheDocument()
    expect(screen.getByText("final")).toBeInTheDocument()
    expect(screen.getAllByText("Yes")).toHaveLength(1)
    expect(screen.getAllByText("No")).toHaveLength(1)
    expect(screen.getByText("c".repeat(64))).toBeInTheDocument()
    expect(screen.getByText("d".repeat(64))).toBeInTheDocument()
  })
})
