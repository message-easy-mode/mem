import { HttpResponse, http } from "msw"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type {
  MigrationTwoServerQualificationClosureEnvelope,
  MigrationTwoServerQualificationClosureState,
  MigrationTwoServerQualificationState,
  MigrationTwoServerSourceEvidenceEnvelope,
} from "@/features/operator/migrations/api/migration-two-server-qualification"
import { MigrationTwoServerQualificationWorkspace } from "./migration-two-server-qualification-workspace"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

const path = "/api/operator/migrations/sessions/mig-qualified/two-server-qualification"
const sourceEvidencePath = `${path}/source-evidence`
const closurePath = `${path}/closure`
const closureReportPath = `${closurePath}/report`

const highAssurance = {
  authorityType: "final-frozen",
  productionAuthorityId: null,
  authorityEvidenceSha256: null,
  packageRevisionId: "mpr-final-01",
  captureKind: "final",
  sourceFrozen: true,
  rehearsalOnly: false,
  formalSourceFreezeEvidenceCollected: true,
  finalRecapturePerformed: true,
  postCaptureWritesIndependentlyExcluded: true,
  rollbackAssurance: "coordinated",
  twoServerQualificationRequired: true,
  detail: "High assurance uses a final frozen recapture.",
}

const simplifiedAssurance = {
  authorityType: "operator-attested-snapshot",
  productionAuthorityId: "mpauth-01",
  authorityEvidenceSha256: "e".repeat(64),
  packageRevisionId: "mpr-preview-01",
  captureKind: "preview",
  sourceFrozen: false,
  rehearsalOnly: true,
  formalSourceFreezeEvidenceCollected: false,
  finalRecapturePerformed: false,
  postCaptureWritesIndependentlyExcluded: false,
  rollbackAssurance: "reduced",
  twoServerQualificationRequired: false,
  detail: "Simplified assurance uses the operator-attested verified snapshot.",
}

const sourceHost = {
  machineIdSha256: "1".repeat(64),
  machineName: "legacy-mem-01",
  operatingSystem: "Ubuntu 22.04",
  architecture: "X64",
  dockerEngineIdSha256: "2".repeat(64),
  dockerName: "legacy-docker",
  dockerServerVersion: "27.5.1",
}

const targetHost = {
  machineIdSha256: "3".repeat(64),
  machineName: "mem-02-target",
  operatingSystem: "Ubuntu 24.04",
  architecture: "X64",
  dockerEngineIdSha256: "4".repeat(64),
  dockerName: "target-docker",
  dockerServerVersion: "27.5.1",
}

const sourceContainer = {
  role: "matrix",
  containerId: "container-matrix-01",
  containerName: "legacy-synapse",
  imageId: "sha256:legacy-synapse",
  writerContainer: true,
  wasRunningBeforeFreeze: true,
  currentState: "exited",
  currentlyRunning: false,
  currentRestartPolicy: "no",
  identityMatched: true,
  frozenStatePreserved: true,
}

const envelope: MigrationTwoServerSourceEvidenceEnvelope = {
  schemaVersion: "mem.migration.two-server-source-evidence.v1",
  payloadSha256: "5".repeat(64),
  payload: {
    qualificationAttemptId: "mm01e-source-20260720",
    generatedAtUtc: "2026-07-20T03:00:00Z",
    migrationId: "source-migration-01",
    intakeId: "mig-qualified",
    packageRevisionId: "mpr-final-01",
    encryptedPackageSha256: "6".repeat(64),
    encryptedPackageBytes: 1048576,
    sourceArchiveSha256: "7".repeat(64),
    freezeAttemptId: "freeze-01",
    freezePlanId: "freeze-plan-01",
    freezePlanSha256: "8".repeat(64),
    sourceFingerprint: "9".repeat(64),
    sourceStackSlug: "legacy-stack",
    matrixServerName: "matrix.example.test",
    sourceFrozen: true,
    publicRoutingMutationOccurred: false,
    developmentExternalControlPlane: false,
    sourceHost,
    containers: [sourceContainer],
  },
}

const blockedState: MigrationTwoServerQualificationState = {
  source: "control-plane",
  status: "blocked",
  migrationId: "mig-qualified",
  qualificationEligible: false,
  qualified: false,
  assurance: highAssurance,
  qualification: null,
  blockers: ["The first native MEM baseline backup must exist in the Backup Catalog."],
  detail: "Two-server qualification remains blocked until migration acceptance and the first native MEM baseline backup are complete.",
}

const readyState: MigrationTwoServerQualificationState = {
  ...blockedState,
  status: "ready-for-source-evidence",
  qualificationEligible: true,
  blockers: [],
  detail: "Import the hash-bound source qualification envelope from the retained MEM 0.1.0 server.",
}

const qualifiedState: MigrationTwoServerQualificationState = {
  source: "control-plane",
  status: "qualified",
  migrationId: "mig-qualified",
  qualificationEligible: false,
  qualified: true,
  assurance: highAssurance,
  blockers: [],
  detail: "The accepted migration is qualified against distinct source and target hosts.",
  qualification: {
    qualificationId: "mtq-01",
    status: "qualified",
    importedAtUtc: "2026-07-20T03:05:00Z",
    qualifiedAtUtc: "2026-07-20T03:05:00Z",
    sourceEvidenceAttemptId: envelope.payload.qualificationAttemptId,
    sourceEvidenceSha256: envelope.payloadSha256,
    qualificationEvidenceSha256: "a".repeat(64),
    sourceMigrationId: envelope.payload.migrationId,
    packageRevisionId: envelope.payload.packageRevisionId,
    sourceStackSlug: envelope.payload.sourceStackSlug,
    matrixServerName: envelope.payload.matrixServerName,
    adoptionPlanId: "madp-01",
    runtimeStackId: "11111111-1111-1111-1111-111111111111",
    productionVerificationId: "mpv-01",
    acceptanceId: "macc-01",
    baselineBackupHandoffId: "mbh-01",
    baselineCatalogEntryId: "bkp-01",
    sourceHost,
    targetHost,
    distinctMachineIdentity: true,
    distinctDockerEngineIdentity: true,
    sourceContainers: [sourceContainer],
  },
}


const readyClosureState: MigrationTwoServerQualificationClosureState = {
  source: "control-plane",
  status: "ready-to-close",
  migrationId: "mig-qualified",
  closureEligible: true,
  closed: false,
  assurance: highAssurance,
  closure: null,
  blockers: [],
  detail: "Review the accepted migration, distinct-host evidence, retained source boundary, normal Runtime Stack, and first native baseline backup before producing the final MEM 0.2.0 qualification handoff.",
}

const closedEnvelope: MigrationTwoServerQualificationClosureEnvelope = {
  schemaVersion: "mem.migration.two-server-qualification-closure.v2",
  payloadSha256: "b".repeat(64),
  payload: {
    closureId: "mtqc-01",
    closedAtUtc: "2026-07-20T04:30:00Z",
    memVersion: "0.2.0",
    migrationId: "mig-qualified",
    assurance: highAssurance,
    sourceMigrationId: envelope.payload.migrationId,
    packageRevisionId: envelope.payload.packageRevisionId,
    encryptedPackageSha256: envelope.payload.encryptedPackageSha256,
    sourceFingerprint: envelope.payload.sourceFingerprint,
    sourceStackSlug: envelope.payload.sourceStackSlug,
    matrixServerName: envelope.payload.matrixServerName,
    qualificationId: "mtq-01",
    qualificationEvidenceSha256: "a".repeat(64),
    sourceEvidenceAttemptId: envelope.payload.qualificationAttemptId,
    sourceEvidenceSha256: envelope.payloadSha256,
    adoptionPlanId: "madp-01",
    runtimeStackId: "11111111-1111-1111-1111-111111111111",
    runtimeStackSlug: "migrated-production",
    productionVerificationId: "mpv-01",
    productionVerificationEvidenceSha256: "c".repeat(64),
    productionVerificationCompletedAtUtc: "2026-07-20T03:30:00Z",
    acceptanceId: "macc-01",
    acceptanceEvidenceSha256: "c".repeat(64),
    acceptedAtUtc: "2026-07-20T03:40:00Z",
    baselineBackupHandoffId: "mbh-01",
    baselineBackupId: "backup-native-01",
    baselineCatalogEntryId: "bkp-01",
    baselineCatalogPayloadState: "available",
    baselineCatalogIntegrityStatus: "verified",
    baselineCompletedAtUtc: "2026-07-20T03:50:00Z",
    baselineBackupBytes: 1048576,
    baselineBackupFiles: 42,
    baselineBackupWarnings: 0,
    sourceHost,
    targetHost,
    distinctMachineIdentity: true,
    distinctDockerEngineIdentity: true,
    sourceContainers: [sourceContainer],
    publicRoutes: [{
      serviceKey: "matrix",
      publicHost: "matrix.example.test",
      publicBaseUrl: "https://matrix.example.test",
      provider: "npm",
      providerRouteId: "route-matrix-01",
      forwardScheme: "http",
      forwardHost: "mem-matrix-production",
      forwardPort: 8008,
      isPublic: true,
      sslExpected: true,
      sslConfigured: true,
      forceSsl: true,
      status: "verified",
      lastVerifiedAtUtc: "2026-07-20T04:20:00Z",
    }, {
      serviceKey: "element-web",
      publicHost: "chat.example.test",
      publicBaseUrl: "https://chat.example.test",
      provider: "npm",
      providerRouteId: "route-element-01",
      forwardScheme: "http",
      forwardHost: "mem-element-production",
      forwardPort: 80,
      isPublic: true,
      sslExpected: true,
      sslConfigured: true,
      forceSsl: true,
      status: "verified",
      lastVerifiedAtUtc: "2026-07-20T04:20:00Z",
    }],
    qualificationEvidenceReviewed: true,
    distinctHostEvidenceReviewed: true,
    normalLifecycleReviewed: true,
    sourceRetentionReviewed: true,
    releaseHandoffAcknowledged: true,
    note: "Release qualification reviewed.",
  },
}

const closedClosureState: MigrationTwoServerQualificationClosureState = {
  source: "control-plane",
  status: "closed",
  migrationId: "mig-qualified",
  closureEligible: false,
  closed: true,
  assurance: highAssurance,
  closure: closedEnvelope,
  blockers: [],
  detail: "The MEM 0.1.0 to MEM 0.2.0 migration is production-qualified and closed.",
}

const simplifiedState: MigrationTwoServerQualificationState = {
  source: "control-plane",
  status: "not-applicable-simplified-assurance",
  migrationId: "mig-qualified",
  qualificationEligible: false,
  qualified: false,
  assurance: simplifiedAssurance,
  qualification: null,
  blockers: [],
  detail: "Two-server source-freeze qualification is not part of simplified assurance.",
}

describe("MigrationTwoServerQualificationWorkspace", () => {
  it("treats two-server source-freeze qualification as not applicable for simplified assurance", async () => {
    server.use(http.get(path, () => HttpResponse.json(simplifiedState)))

    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    expect(await screen.findByText("Simplified migration complete")).toBeInTheDocument()
    expect(screen.getByText("operator-attested-snapshot")).toBeInTheDocument()
    expect(screen.getByText("mpauth-01")).toBeInTheDocument()
    expect(screen.getByText("reduced")).toBeInTheDocument()
    expect(screen.queryByLabelText("Choose source qualification evidence")).not.toBeInTheDocument()
  })

  it("shows server-projected blockers and does not offer evidence import early", async () => {
    server.use(http.get(path, () => HttpResponse.json(blockedState)))

    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    expect(await screen.findByText("Two-server production qualification")).toBeInTheDocument()
    expect(screen.getByText("Two-server qualification is blocked")).toBeInTheDocument()
    expect(screen.getByText("The first native MEM baseline backup must exist in the Backup Catalog.")).toBeInTheDocument()
    expect(screen.queryByLabelText("Choose source qualification evidence")).not.toBeInTheDocument()
  })

  it("reviews the source envelope, requires step-up, and reconstructs distinct-host qualification", async () => {
    let first = true
    let submitted: unknown = null
    server.use(
      http.get(path, () => HttpResponse.json(readyState)),
      http.get(closurePath, () => HttpResponse.json(readyClosureState)),
      http.post(sourceEvidencePath, async ({ request }) => {
        submitted = await request.json()
        if (first) {
          first = false
          return HttpResponse.json({ code: "step_up_required", detail: "Verify identity." }, { status: 403 })
        }
        return HttpResponse.json(qualifiedState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    const input = await screen.findByLabelText("Choose source qualification evidence")
    await user.upload(input, new File([JSON.stringify(envelope)], "source-evidence.json", { type: "application/json" }))

    expect(await screen.findByText("Review source evidence before import")).toBeInTheDocument()
    expect(screen.getByText("mm01e-source-20260720")).toBeInTheDocument()
    expect(screen.getByText("legacy-mem-01")).toBeInTheDocument()
    expect(screen.getByText("Browser review is not qualification")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Import source evidence and qualify" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Two-server production qualification complete")).toBeInTheDocument()
    expect(screen.getByText("mem-02-target")).toBeInTheDocument()
    expect(screen.getByText("Distinct Linux machine identities")).toBeInTheDocument()
    expect(screen.getByText("Distinct Docker Engine identities")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open first native baseline backup" })).toHaveAttribute("href", "/backups/catalog/bkp-01")
    expect(submitted).toEqual(envelope)
  })

  it("reconstructs durable qualified evidence after refresh", async () => {
    server.use(
      http.get(path, () => HttpResponse.json(qualifiedState)),
      http.get(closurePath, () => HttpResponse.json(readyClosureState)),
    )

    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    expect(await screen.findByText("mtq-01")).toBeInTheDocument()
    expect(screen.getByText("legacy-synapse")).toBeInTheDocument()
    expect(screen.getByText("Frozen and verified")).toBeInTheDocument()
    expect(screen.getByText("11111111-1111-1111-1111-111111111111")).toBeInTheDocument()
    expect(screen.queryByLabelText("Choose source qualification evidence")).not.toBeInTheDocument()
  })

  it("requires all acknowledgements and step-up before closing production qualification", async () => {
    let closeAttempts = 0
    let submitted: unknown = null
    server.use(
      http.get(path, () => HttpResponse.json(qualifiedState)),
      http.get(closurePath, () => HttpResponse.json(readyClosureState)),
      http.post(closurePath, async ({ request }) => {
        submitted = await request.json()
        closeAttempts += 1
        if (closeAttempts === 1) {
          return HttpResponse.json({ code: "step_up_required", detail: "Verify identity." }, { status: 403 })
        }
        return HttpResponse.json(closedClosureState)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    expect(await screen.findByText("Ready to close production qualification")).toBeInTheDocument()
    const closeButton = screen.getByRole("button", { name: "Close production qualification" })
    expect(closeButton).toBeDisabled()

    await user.click(screen.getByLabelText(/reviewed the durable source, target, production-verification/i))
    await user.click(screen.getByLabelText(/genuinely distinct Linux hosts and Docker Engines/i))
    await user.click(screen.getByLabelText(/accepted normal Runtime Stack/i))
    await user.click(screen.getByLabelText(/does not delete the retained MEM 0.1.0 source/i))
    await user.click(screen.getByLabelText(/downloaded hash-bound closure report/i))
    await user.type(screen.getByLabelText("Operator closure note"), "Release qualification reviewed.")

    expect(closeButton).toBeEnabled()
    await user.click(closeButton)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Production qualification closed")).toBeInTheDocument()
    expect(screen.getByText("mtqc-01")).toBeInTheDocument()
    expect(screen.getByText("route-matrix-01")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open accepted Runtime Stack" })).toHaveAttribute(
      "href",
      "/stacks/migrated-production",
    )
    expect(screen.getByRole("button", { name: "Download release handoff" })).toBeInTheDocument()
    expect(submitted).toEqual({
      qualificationEvidenceReviewed: true,
      distinctHostEvidenceReviewed: true,
      normalLifecycleReviewed: true,
      sourceRetentionReviewed: true,
      releaseHandoffAcknowledged: true,
      note: "Release qualification reviewed.",
    })
  })

  it("requires step-up and downloads the durable release handoff", async () => {
    let reportAttempts = 0
    const createObjectUrl = vi.fn(() => "blob:qualification-closure")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)

    server.use(
      http.get(path, () => HttpResponse.json(qualifiedState)),
      http.get(closurePath, () => HttpResponse.json(closedClosureState)),
      http.get(closureReportPath, () => {
        reportAttempts += 1
        if (reportAttempts === 1) {
          return HttpResponse.json({ code: "step_up_required", detail: "Verify identity." }, { status: 403 })
        }
        return HttpResponse.json(closedEnvelope, {
          headers: {
            "Content-Disposition": 'attachment; filename="mtqc-01.mem-production-qualification-closure.json"',
          },
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationTwoServerQualificationWorkspace migrationId="mig-qualified" />)

    await user.click(await screen.findByRole("button", { name: "Download release handoff" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(reportAttempts).toBe(2)
    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(anchorClick).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:qualification-closure")

    anchorClick.mockRestore()
  })
})
