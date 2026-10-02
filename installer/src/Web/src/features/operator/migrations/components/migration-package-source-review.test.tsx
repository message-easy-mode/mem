import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest"

import type { ReactElement } from "react"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"

import { MigrationPackageWorkspace } from "./migration-package-workspace"
import { MigrationSourceAssessment } from "./migration-source-assessment"

const baseSession = {
  migrationId: "mig_package_review",
  displayName: "Old server migration",
  sourceAdapter: "pending",
  sourceDisplay: "Package not yet received",
  phase: "package-transfer",
  status: "awaiting-package",
  nextAction: "upload-package",
  createdAtUtc: "2026-07-25T00:00:00Z",
  updatedAtUtc: "2026-07-25T00:05:00Z",
  blockerCount: 0,
  warningCount: 0,
  advisoryCount: 0,
  needsAttention: false,
  sourceCount: 0,
  stackCount: 0,
  historicalCompatibility: {
    usesLegacyNeutralImportContract: false,
    legacyContractVersion: null,
    legacyContractStatus: null,
    legacyManifestSha256: null,
    usesCatalogRestorePath: false,
    catalogEntryCount: 0,
    restoreSessionCount: 0,
  },
}

const awaitingDetail: MigrationSessionDetail = {
  session: baseSession,
  package: {
    transferMode: "encrypted",
    status: "awaiting-package",
    fileName: null,
    sizeBytes: null,
    encryptedSha256: null,
    decryptedSha256: null,
    uploadedAtUtc: null,
    validatedAtUtc: null,
    expiresAtUtc: "2026-07-26T00:00:00Z",
    ageRecipient: "age1targetrecipient",
    recipientFingerprint: "1111-2222-3333-4444",
    archiveMigrationId: null,
    archiveSourceProduct: null,
    archiveSourceVersion: null,
    archiveStackCount: null,
  },
  packageRevisions: [],
  sources: [],
  findings: [],
  linkedObjects: [],
}

const validatedDetail: MigrationSessionDetail = {
  session: {
    ...baseSession,
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "source-assessment",
    status: "package-validated",
    nextAction: "review-source",
    sourceCount: 1,
    stackCount: 1,
  },
  package: {
    ...awaitingDetail.package!,
    status: "package-validated",
    fileName: "old-server.memmigration.zip.age",
    sizeBytes: 4096,
    encryptedSha256: "a".repeat(64),
    decryptedSha256: "b".repeat(64),
    uploadedAtUtc: "2026-07-25T00:10:00Z",
    validatedAtUtc: "2026-07-25T00:12:00Z",
    archiveMigrationId: "source-capture-1",
    archiveSourceProduct: "MatrixEasyMode",
    archiveSourceVersion: "0.1.0",
    archiveStackCount: 1,
  },
  packageRevisions: [],
  sources: [
    {
      sourceId: "source-1",
      kind: "migration-package",
      product: "MatrixEasyMode",
      productVersion: "0.1.0",
      sourceFingerprint: "c".repeat(64),
      capturedAtUtc: "2026-07-25T00:08:00Z",
    },
  ],
  findings: [],
  linkedObjects: [],
}

const singleStackOptions = {
  packageRevisionId: "mpr_preview",
  boundSourceStack: {
    sourceStackId: "9230081f-ba21-4e53-8fbe-b3cc7bd1b441",
    slug: "tester",
    matrixServerName: "matrix-tester.example.test",
  },
}

let reviewWorkspace = guidedWorkspace({ migrationId: baseSession.migrationId, stage: "review-old-server", action: "start-conversion", detail: validatedDetail })
beforeEach(() => {
  sessionStorage.clear()
  reviewWorkspace = guidedWorkspace({ migrationId: baseSession.migrationId, stage: "review-old-server", action: "start-conversion", detail: validatedDetail })
})
function renderWithProviders(ui: ReactElement) {
  return renderGuided(ui, () => ui.type === MigrationPackageWorkspace
    ? guidedWorkspace({ migrationId: baseSession.migrationId, stage: "create-and-upload-package", action: "upload-package", detail: awaitingDetail })
    : reviewWorkspace)
}

describe("Migration package and source review simplification", () => {
  afterEach(() => {
    localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    vi.restoreAllMocks()
  })
  it("keeps the normal package flow to two clear steps and moves security facts behind details", async () => {
    const user = userEvent.setup()

    renderWithProviders(
      <MigrationPackageWorkspace detail={awaitingDetail} onChanged={vi.fn(async () => undefined)} />,
    )

    expect(screen.getByText("Migration package")).toBeInTheDocument()
    expect(screen.getByText(/Use MEM Migrate Source Assistant on the old server to create/)).toBeInTheDocument()
    expect(screen.getByText(/Select the .memmigration.zip.age package downloaded from the old server/)).toBeInTheDocument()
    expect(screen.queryByText(/Run one command on the old server/)).not.toBeInTheDocument()
    expect(screen.queryByText(/file created by the command/)).not.toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Send the request to the old server" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Download migration request" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Upload the package" })).toBeInTheDocument()

    const securitySummary = screen.getByText("Security and request details")
    const securityDetails = securitySummary.closest("details")
    expect(securityDetails).not.toBeNull()
    expect(securityDetails).not.toHaveAttribute("open")
    expect(within(securityDetails!).getByText("age1targetrecipient")).toBeInTheDocument()
    expect(within(securityDetails!).getByText("1111-2222-3333-4444")).toBeInTheDocument()

    await user.click(screen.getByText("Advanced: use the command line instead"))
    await user.click(screen.getByRole("button", { name: "Show command" }))
    expect(screen.getByLabelText("Generated source command")).toHaveTextContent(
      "mem-migrate source package-for-intake",
    )
  })

  it("downloads a versioned preview request for Source Assistant", async () => {
    const user = userEvent.setup()
    const createObjectUrl = vi.fn(() => "blob:source-request")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)
    server.use(
      http.get(
        "/api/operator/migrations/sessions/mig_package_review/package-revisions/preview/source-request",
        () => HttpResponse.json(
          { schema: "mem-secure-intake-request", schemaVersion: 1 },
          { headers: { "Content-Disposition": 'attachment; filename="preview-request.json"' } },
        ),
      ),
    )

    renderWithProviders(
      <MigrationPackageWorkspace detail={awaitingDetail} onChanged={vi.fn(async () => undefined)} />,
    )
    await user.click(screen.getByRole("button", { name: "Download migration request" }))

    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(click).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:source-request")
  })

  it("collapses hashes and retention detail after the package is verified", () => {
    renderWithProviders(
      <MigrationPackageWorkspace detail={validatedDetail} onChanged={vi.fn(async () => undefined)} />,
    )

    expect(screen.getByText("Migration package received and verified")).toBeInTheDocument()
    expect(screen.getByText("old-server.memmigration.zip.age")).toBeInTheDocument()
    expect(screen.getByText("4.0 KB")).toBeInTheDocument()

    const validationSummary = screen.getByText("Validation and retention details")
    const validationDetails = validationSummary.closest("details")
    expect(validationDetails).not.toBeNull()
    expect(validationDetails).not.toHaveAttribute("open")
    expect(within(validationDetails!).getByText("a".repeat(64))).toBeInTheDocument()
    expect(within(validationDetails!).getByText("b".repeat(64))).toBeInTheDocument()
  })

  it("presents a concise supported old-server summary and starts migration-data preparation", async () => {
    const user = userEvent.setup()
    const onChanged = vi.fn(async () => undefined)
    let postedBody: unknown = null
    server.use(
      http.get(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts/options",
        () => HttpResponse.json(singleStackOptions),
      ),
      http.get(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts",
        () => HttpResponse.json([]),
      ),
      http.post(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts",
        async ({ request }) => {
          postedBody = await request.json()
          return HttpResponse.json({
            conversionAttemptId: "conv_started",
            migrationId: "mig_package_review",
            status: "pending",
            currentStep: "queued",
            createdAtUtc: "2026-07-25T00:13:00Z",
            updatedAtUtc: "2026-07-25T00:13:00Z",
            startedAtUtc: null,
            completedAtUtc: null,
            resultCode: null,
            failureCode: null,
            failureSummary: null,
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
          })
        },
      ),
    )

    renderWithProviders(
      <MigrationSourceAssessment detail={validatedDetail} onChanged={onChanged} />,
    )

    expect(screen.getByText("Review the old server")).toBeInTheDocument()
    expect(screen.getByText("This server is supported for migration")).toBeInTheDocument()
    expect(screen.getAllByText("Message Easy Mode 0.1.0")).not.toHaveLength(0)
    expect(screen.getAllByText("MatrixEasyMode 0.1.0")).not.toHaveLength(0)

    const technicalSummary = screen.getByText("Technical identity details")
    const technicalDetails = technicalSummary.closest("details")
    expect(technicalDetails).not.toBeNull()
    expect(technicalDetails).not.toHaveAttribute("open")
    expect(within(technicalDetails!).getByText("mem-v010")).toBeInTheDocument()
    expect(within(technicalDetails!).getByText("c".repeat(64))).toBeInTheDocument()

    const prepare = screen.getByRole("button", { name: "Prepare migration data" })
    await waitFor(() => expect(prepare).toBeEnabled())
    await user.click(prepare)
    await waitFor(() => expect(postedBody).toEqual({}))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
  })

  it("shows the source-selected stack and never asks for a second target selection", async () => {
    const user = userEvent.setup()
    let postedBody: unknown = null

    server.use(
      http.get(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts/options",
        () => HttpResponse.json(singleStackOptions),
      ),
      http.get(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts",
        () => HttpResponse.json([]),
      ),
      http.post(
        "/api/operator/migrations/sessions/mig_package_review/conversion-attempts",
        async ({ request }) => {
          postedBody = await request.json()
          return HttpResponse.json({ conversionAttemptId: "conv_bound" })
        },
      ),
    )

    renderWithProviders(
      <MigrationSourceAssessment detail={validatedDetail} onChanged={vi.fn(async () => undefined)} />,
    )

    expect(await screen.findByRole("heading", { name: "Source stack bound to this package" })).toBeInTheDocument()
    expect(screen.getByText("tester")).toBeInTheDocument()
    expect(screen.queryByRole("radio")).not.toBeInTheDocument()

    const prepare = screen.getByRole("button", { name: "Prepare migration data" })
    expect(prepare).toBeEnabled()
    await user.click(prepare)

    await waitFor(() => expect(postedBody).toEqual({}))
  })

  it("reconciles an accepted conversion from the old-server review after its POST response is lost", async () => {
    const user = userEvent.setup()
    const onChanged = vi.fn(async () => undefined)
    let accepted = false
    let posts = 0
    const running = {
      conversionAttemptId: "conv_network_loss",
      migrationId: "mig_package_review",
      status: "running",
      currentStep: "worker-starting",
    }
    server.use(
      http.get("/api/operator/migrations/sessions/mig_package_review/conversion-attempts/options", () =>
        HttpResponse.json(singleStackOptions)),
      http.get("/api/operator/migrations/sessions/mig_package_review/conversion-attempts", () =>
        HttpResponse.json(accepted ? [running] : [])),
      http.post("/api/operator/migrations/sessions/mig_package_review/conversion-attempts", () => {
        posts += 1
        accepted = true
        reviewWorkspace = guidedWorkspace({ migrationId: baseSession.migrationId, stage: "prepare-and-test", state: "running", action: null,
          guided: { operationRevisions: { ...reviewWorkspace.guided.operationRevisions, conversion: "conv_network_loss:running" } } })
        return HttpResponse.error()
      }),
    )
    renderWithProviders(<MigrationSourceAssessment detail={validatedDetail} onChanged={onChanged} />)
    await waitFor(() => expect(screen.getByRole("button", { name: "Prepare migration data" })).toBeEnabled())
    await user.click(screen.getByRole("button", { name: "Prepare migration data" }))
    await waitFor(() => expect(screen.getByRole("button", { name: "Prepare migration data" })).toBeDisabled())
    expect(onChanged).not.toHaveBeenCalled()
    expect(posts).toBe(1)
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(screen.queryByText("Prepare and test action failed")).not.toBeInTheDocument()
  })


  it("describes the Source Assistant handoff in German while retaining the command-line alternative", () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderWithProviders(<MigrationPackageWorkspace detail={awaitingDetail} onChanged={vi.fn(async () => undefined)} />)
    expect(screen.getByText(/Erstellen Sie das verschlüsselte Paket mit MEM Migrate Source Assistant/)).toBeInTheDocument()
    expect(screen.getByText(/Wählen Sie das vom alten Server heruntergeladene .memmigration.zip.age-Paket/)).toBeInTheDocument()
    expect(screen.queryByText(/Führen Sie einen Befehl auf dem alten Server aus/)).not.toBeInTheDocument()
    expect(screen.getAllByRole("button").length).toBeGreaterThan(0)
  })

})
