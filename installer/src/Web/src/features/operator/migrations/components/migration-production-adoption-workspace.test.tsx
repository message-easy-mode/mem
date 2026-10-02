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
import type {
  MigrationProductionAdoptionState,
} from "@/features/operator/migrations/api/migration-production-adoption"

import { MigrationProductionAdoptionWorkspace } from "./migration-production-adoption-workspace"

const detail = {
  session: {
    migrationId: "mig_adoption",
    displayName: "Production adoption test",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "cutover",
    status: "staging-verified",
    nextAction: "prepare-cutover",
    createdAtUtc: "2026-07-19T00:00:00Z",
    updatedAtUtc: "2026-07-19T00:01:00Z",
    blockerCount: 0, warningCount: 0, advisoryCount: 0, needsAttention: false,
    sourceCount: 1, stackCount: 1,
    historicalCompatibility: {
      usesLegacyNeutralImportContract: false, legacyContractVersion: null,
      legacyContractStatus: null, legacyManifestSha256: null,
      usesCatalogRestorePath: false, catalogEntryCount: 0, restoreSessionCount: 0,
    },
  },
  package: {
    transferMode: "encrypted", status: "package-validated", fileName: "final.zip.age", sizeBytes: 100,
    encryptedSha256: "1".repeat(64), decryptedSha256: "2".repeat(64), uploadedAtUtc: "2026-07-19T00:00:00Z",
    validatedAtUtc: "2026-07-19T00:00:00Z", expiresAtUtc: null, ageRecipient: null, recipientFingerprint: null,
    archiveMigrationId: "source-final", archiveSourceProduct: "MatrixEasyMode", archiveSourceVersion: "0.1.0", archiveStackCount: 1,
  },
  sources: [], findings: [], linkedObjects: [],
}

const notPrepared: MigrationProductionAdoptionState = {
  source: "control-plane",
  status: "not-prepared",
  migrationId: "mig_adoption",
  planPrepared: false,
  plan: null,
  detail: "No plan",
}

const prepared: MigrationProductionAdoptionState = {
  source: "control-plane",
  status: "prepared",
  migrationId: "mig_adoption",
  planPrepared: true,
  detail: "Prepared",
  plan: {
    adoptionPlanId: "madp_001",
    migrationId: "mig_adoption",
    packageRevisionId: "mpr_final",
    candidateArtifactId: "mca_final",
    stagingRunId: "mstg_final",
    status: "prepared",
    revisionNumber: 1,
    planSha256: "a".repeat(64),
    createdAtUtc: "2026-07-19T00:02:00Z",
    updatedAtUtc: "2026-07-19T00:02:00Z",
    preparedAtUtc: "2026-07-19T00:02:00Z",
    runtimeStackId: "11111111-1111-1111-1111-111111111111",
    targetStackSlug: "davids-stack",
    targetDisplayName: "Davids stack",
    matrixInstanceId: "22222222-2222-2222-2222-222222222222",
    elementInstanceId: "33333333-3333-3333-3333-333333333333",
    matrixServerName: "matrix-davids.deltabox.dev",
    matrixPublicHost: "matrix-davids.deltabox.dev",
    matrixPublicBaseUrl: "https://matrix-davids.deltabox.dev",
    elementPublicHost: "chat-davids.deltabox.dev",
    elementPublicBaseUrl: "https://chat-davids.deltabox.dev",
    runtimeNetworkName: "mem-gateway",
    matrixContainerName: "mem-matrix-davids-stack",
    elementContainerName: "mem-element-davids-stack",
    matrixImageReference: "sha256:synapse-approved",
    matrixImageId: "sha256:synapse-local",
    elementImageReference: "sha256:element-approved",
    elementImageId: "sha256:element-local",
    databaseEngine: "postgres",
    databaseHost: "mem-postgres",
    databasePort: 5432,
    databaseName: "mem_davids_stack_11111111",
    databaseUsername: "mem_davids_stack_11111111",
    databasePasswordSecretKind: "matrix_postgres_password",
    expectedUsersCount: 3,
    expectedRoomsCount: 2,
    expectedEventsCount: 23,
    routes: [
      {
        serviceKey: "matrix",
        publicHost: "matrix-davids.deltabox.dev",
        publicBaseUrl: "https://matrix-davids.deltabox.dev",
        forwardScheme: "http",
        forwardHost: "mem-matrix-davids-stack",
        forwardPort: 8008,
        provider: "npm",
        publicMutationDeferred: true,
      },
      {
        serviceKey: "element-web",
        publicHost: "chat-davids.deltabox.dev",
        publicBaseUrl: "https://chat-davids.deltabox.dev",
        forwardScheme: "http",
        forwardHost: "mem-element-davids-stack",
        forwardPort: 80,
        provider: "npm",
        publicMutationDeferred: true,
      },
    ],
    collisions: [],
    collisionFree: true,
    runtimeRecordsCreated: false,
    publicRoutesCreated: false,
    materialization: {
      materializationId: null,
      status: "not-started",
      startedAtUtc: null,
      completedAtUtc: null,
      productionDatabaseImported: false,
      matrixContainerStarted: false,
      matrixHealthPassed: false,
      elementContainerStarted: false,
      elementHealthPassed: false,
      runtimeManifestSaved: false,
      databaseOwnershipSaved: false,
      runtimeRecordsCreated: false,
      userInventorySynchronized: false,
      publicRoutesCreated: false,
      failureCode: null,
      failureSummary: null,
    },
    cutover: {
      preview: {
        previewId: null,
        status: "not-prepared",
        createdAtUtc: null,
        expiresAtUtc: null,
        snapshotSha256: null,
        routes: [],
        blockers: [],
      },
      execution: {
        executionId: null,
        status: "not-started",
        startedAtUtc: null,
        completedAtUtc: null,
        targetPublicAtUtc: null,
        matrixRouteId: null,
        elementRouteId: null,
        runtimePromotionCompleted: false,
        publicRoutesCreated: false,
        routeCompensationAttempted: false,
        routeCompensationCompleted: false,
        failureCode: null,
        failureSummary: null,
      },
    },
    productionVerification: {
      verificationId: null,
      status: "not-started",
      startedAtUtc: null,
      completedAtUtc: null,
      validUntilUtc: null,
      fresh: false,
      passed: false,
      checkCount: 0,
      failedCheckCount: 0,
      evidenceSha256: null,
      readinessReportId: null,
      checks: [],
      failureCode: null,
      failureSummary: null,
    },
    rollback: {
      preview: {
        previewId: null,
        status: "not-prepared",
        createdAtUtc: null,
        expiresAtUtc: null,
        snapshotSha256: null,
        routes: [],
        blockers: [],
      },
      execution: {
        executionId: null,
        status: "not-started",
        startedAtUtc: null,
        completedAtUtc: null,
        routesRestored: false,
        runtimeRoutesRemoved: false,
        targetContainersStopped: false,
        targetRouteCompensationAttempted: false,
        targetRouteCompensationCompleted: false,
        sourceHandoffId: null,
        sourceHandoffSha256: null,
        failureCode: null,
        failureSummary: null,
      },
      completion: {
        status: "not-received",
        restorationAttemptId: null,
        completionSha256: null,
        importedAtUtc: null,
        completedAtUtc: null,
        sourceRestored: false,
        restartPoliciesRestored: false,
        originalRunningStatesRestored: false,
        matrixVerified: false,
        elementVerified: false,
        targetRollbackAuthorityVerified: false,
        targetRollbackStillIntact: false,
        developmentExternalControlPlane: false,
      },
    },
    blockerSummary: null,
  },
}

describe("MigrationProductionAdoptionWorkspace", () => {
  it("prepares and reconstructs a collision-free normal runtime ownership plan without public mutation", async () => {
    const user = userEvent.setup()
    let state = notPrepared
    let requestBody: unknown

    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () =>
        HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/plan", async ({ request }) => {
        requestBody = await request.json()
        state = {
          ...prepared,
          plan: {
            ...prepared.plan!,
            elementPublicHost: "element-davids.deltabox.dev",
            elementPublicBaseUrl: "https://element-davids.deltabox.dev",
            routes: prepared.plan!.routes.map((route) =>
              route.serviceKey === "element-web"
                ? {
                    ...route,
                    publicHost: "element-davids.deltabox.dev",
                    publicBaseUrl: "https://element-davids.deltabox.dev",
                  }
                : route),
          },
        }
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    expect(await screen.findByText(/No production adoption plan has been prepared/)).toBeInTheDocument()
    await user.type(screen.getByLabelText("Target stack slug"), "Davids Stack")
    await user.type(screen.getByLabelText("Element public host"), "Element-Davids.DeltaBox.Dev.")
    await user.click(screen.getByRole("button", { name: "Prepare adoption plan" }))

    await waitFor(() => expect(requestBody).toEqual({
      targetStackSlug: "Davids Stack",
      elementPublicHost: "Element-Davids.DeltaBox.Dev.",
    }))
    expect(await screen.findByText("madp_001")).toBeInTheDocument()
    expect(screen.getByText("Normal MEM ownership plan is collision-free")).toBeInTheDocument()
    expect(screen.getByText("mem-matrix-davids-stack")).toBeInTheDocument()
    expect(screen.getByText("mem-element-davids-stack")).toBeInTheDocument()
    expect(screen.getAllByDisplayValue("element-davids.deltabox.dev")).toHaveLength(1)
    expect(screen.getAllByText("element-davids.deltabox.dev").length).toBeGreaterThan(0)
    expect(screen.getByText(/Matrix server identity remains locked/)).toBeInTheDocument()
    expect(screen.getAllByText("mem_davids_stack_11111111")).toHaveLength(2)
    expect(screen.getAllByText("Public route mutation is deferred to MIG-PRODUCTION-01B.")).toHaveLength(2)
    expect(screen.getByText(/Planning is read-only/)).toBeInTheDocument()
  })

  it("renders durable collision evidence and keeps recheck available", async () => {
    const blocked: MigrationProductionAdoptionState = {
      ...prepared,
      status: "blocked",
      plan: {
        ...prepared.plan!,
        status: "blocked",
        collisionFree: false,
        blockerSummary: "1 production ownership collision(s) must be resolved before public cutover.",
        collisions: [{
          code: "runtime_stack_collision",
          resourceType: "runtime-stack",
          resourceValue: "davids-stack",
          detail: "A normal MEM runtime stack already owns the planned stack identity or slug.",
        }],
      },
    }

    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () =>
        HttpResponse.json(blocked)),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    expect(await screen.findByText("Production ownership collisions found")).toBeInTheDocument()
    expect(screen.getByText("A normal MEM runtime stack already owns the planned stack identity or slug.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Recheck adoption plan" })).toBeEnabled()
  })

  it("requires acknowledgements and browser step-up before private normal-runtime materialisation", async () => {
    const user = userEvent.setup()
    let attempts = 0
    let requestBody: unknown
    const materialized: MigrationProductionAdoptionState = {
      ...prepared,
      status: "private-runtime-ready",
      plan: {
        ...prepared.plan!,
        status: "private-runtime-ready",
        runtimeRecordsCreated: true,
        materialization: {
          materializationId: "mpm_001",
          status: "private-runtime-ready",
          startedAtUtc: "2026-07-19T00:03:00Z",
          completedAtUtc: "2026-07-19T00:04:00Z",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
          publicRoutesCreated: false,
          failureCode: null,
          failureSummary: null,
        },
      },
    }

    let state = prepared
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () =>
        HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/materialize", async ({ request }) => {
        attempts += 1
        requestBody = await request.json()
        if (attempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent step-up required." },
            { status: 403 },
          )
        }
        state = materialized
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    const button = await screen.findByRole("button", { name: "Materialise private production runtime" })
    expect(button).toBeDisabled()

    for (const label of [
      /creates normal MEM Runtime Stack/,
      /creates and imports the target database/,
      /starts production-named Matrix and Element/,
      /must not create or change NPM routes/,
      /automatic public-cutover rollback is not part/,
    ]) {
      await user.click(screen.getByLabelText(label))
    }

    expect(button).toBeEnabled()
    await user.click(button)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(attempts).toBe(2))
    expect(requestBody).toEqual({
      operator: null,
      note: null,
      executePrivateProductionMaterialization: true,
      acknowledgeCreatesNormalRuntimeRecords: true,
      acknowledgeMutatesProductionPostgres: true,
      acknowledgeStartsProductionContainers: true,
      acknowledgeNoPublicRoutes: true,
      acknowledgeNoAutomaticRollback: true,
    })
    expect(await screen.findByText("Normal MEM runtime is privately materialised")).toBeInTheDocument()
    expect(screen.getByText("mpm_001")).toBeInTheDocument()
    expect(screen.getByText("No public routes created")).toBeInTheDocument()
  })

  it("requires step-up when controlled public cutover has no fresh authorization", async () => {
    const user = userEvent.setup()
    let executionAttempts = 0
    let executionBody: unknown
    const materialized: MigrationProductionAdoptionState = {
      ...prepared,
      status: "private-runtime-ready",
      plan: {
        ...prepared.plan!,
        status: "private-runtime-ready",
        runtimeRecordsCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          materializationId: "mpm_001",
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
      },
    }
    const previewReady: MigrationProductionAdoptionState = {
      ...materialized,
      status: "cutover-preview-ready",
      plan: {
        ...materialized.plan!,
        status: "cutover-preview-ready",
        cutover: {
          ...materialized.plan!.cutover,
          preview: {
            previewId: "mpcv_001",
            status: "ready",
            createdAtUtc: "2026-07-19T00:05:00Z",
            expiresAtUtc: "2099-07-19T00:20:00Z",
            snapshotSha256: "b".repeat(64),
            blockers: [],
            routes: [
              {
                serviceKey: "matrix", publicHost: "matrix-davids.deltabox.dev",
                desiredForwardHost: "mem-matrix-davids-stack", desiredForwardPort: 8008,
                existingRouteFound: true, existingRouteId: 1, existingForwardScheme: "http",
                existingForwardHost: "old-matrix", existingForwardPort: 8008, existingCertificateId: 3,
                existingSslForced: true, existingHttp2: true, existingEnabled: true,
                existingAdvancedConfigSha256: "c".repeat(64), alreadyTargetsProductionRuntime: false, action: "update",
                selectedCertificateId: 3, selectedCertificateRecordId: "cert-deltabox", selectedCertificateName: "*.deltabox.dev",
              },
              {
                serviceKey: "element-web", publicHost: "chat-davids.deltabox.dev",
                desiredForwardHost: "mem-element-davids-stack", desiredForwardPort: 80,
                existingRouteFound: false, existingRouteId: null, existingForwardScheme: null,
                existingForwardHost: null, existingForwardPort: null, existingCertificateId: null,
                existingSslForced: null, existingHttp2: null, existingEnabled: null,
                existingAdvancedConfigSha256: "d".repeat(64), alreadyTargetsProductionRuntime: false, action: "create",
                selectedCertificateId: 3, selectedCertificateRecordId: "cert-deltabox", selectedCertificateName: "*.deltabox.dev",
              },
            ],
          },
        },
      },
    }
    const publicState: MigrationProductionAdoptionState = {
      ...previewReady,
      status: "public-awaiting-verification",
      plan: {
        ...previewReady.plan!,
        status: "public-awaiting-verification",
        publicRoutesCreated: true,
        cutover: {
          ...previewReady.plan!.cutover,
          execution: {
            executionId: "mpce_001", status: "public-awaiting-verification",
            startedAtUtc: "2026-07-19T00:06:00Z", completedAtUtc: "2026-07-19T00:07:00Z",
            targetPublicAtUtc: "2026-07-19T00:07:00Z", matrixRouteId: "11", elementRouteId: "12",
            runtimePromotionCompleted: true, publicRoutesCreated: true, routeCompensationAttempted: false,
            routeCompensationCompleted: false, failureCode: null, failureSummary: null,
          },
        },
      },
    }

    let state = materialized
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/cutover/preview", () => {
        state = previewReady
        return HttpResponse.json(state)
      }),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/cutover/execution", async ({ request }) => {
        executionAttempts += 1
        executionBody = await request.json()
        if (executionAttempts === 1) {
          return HttpResponse.json({ error: "step_up_required" }, { status: 403 })
        }
        state = publicState
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)
    await user.click(await screen.findByRole("button", { name: "Prepare route snapshot" }))
    expect(await screen.findByText("mpcv_001")).toBeInTheDocument()
    expect(screen.getByText("old-matrix:8008 → update")).toBeInTheDocument()
    expect(screen.getAllByText("Certificate *.deltabox.dev · NPM #3")).toHaveLength(2)

    for (const label of [
      /source writers are frozen/, /runtime is privately healthy/, /route snapshot/,
      /creates or changes public Matrix/, /does not change DNS/, /does not create or import certificates/,
      /rollback assurance depends on the selected production authority/, /production verification is required/,
    ]) {
      await user.click(screen.getByLabelText(label))
    }

    await user.click(screen.getByRole("button", { name: "Execute controlled public cutover" }))
    await waitFor(() => expect(executionAttempts).toBe(1))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    await waitFor(() => expect(executionAttempts).toBe(2))
    expect(executionBody).toMatchObject({
      previewId: "mpcv_001",
      executeNpmRouteMutation: true,
      acknowledgeRollbackIsNextSlice: true,
      acknowledgePostCutoverVerificationRequired: true,
    })
    expect(await screen.findByText("Target is public and awaiting verification")).toBeInTheDocument()
    expect(screen.getByText("mpce_001")).toBeInTheDocument()
  })


  it("keeps an offset-less UTC route preview ready in Pacific/Auckland", async () => {
    vi.stubEnv("TZ", "Pacific/Auckland")
    const now = vi.spyOn(Date, "now").mockReturnValue(
      Date.parse("2026-07-22T02:20:00Z"),
    )

    try {
      const state: MigrationProductionAdoptionState = {
        ...prepared,
        status: "cutover-preview-ready",
        plan: {
          ...prepared.plan!,
          status: "cutover-preview-ready",
          runtimeRecordsCreated: true,
          materialization: {
            ...prepared.plan!.materialization,
            materializationId: "mpm_utc_preview",
            status: "private-runtime-ready",
            productionDatabaseImported: true,
            matrixContainerStarted: true,
            matrixHealthPassed: true,
            elementContainerStarted: true,
            elementHealthPassed: true,
            runtimeManifestSaved: true,
            databaseOwnershipSaved: true,
            runtimeRecordsCreated: true,
            userInventorySynchronized: true,
          },
          cutover: {
            ...prepared.plan!.cutover,
            preview: {
              previewId: "mpcv_offsetless_utc",
              status: "ready",
              createdAtUtc: "2026-07-22T02:19:25",
              expiresAtUtc: "2026-07-22T02:34:25",
              snapshotSha256: "7".repeat(64),
              blockers: [],
              routes: [],
            },
          },
        },
      }

      server.use(
        http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () =>
          HttpResponse.json(state)),
      )

      renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

      expect(await screen.findByText("mpcv_offsetless_utc")).toBeInTheDocument()
      expect(screen.queryByText("Route snapshot retained")).not.toBeInTheDocument()
      expect(screen.getByRole("button", { name: "Execute controlled public cutover" })).toBeInTheDocument()
    } finally {
      now.mockRestore()
      vi.unstubAllEnvs()
    }
  })

  it("keeps a ready route snapshot executable when only its legacy expiry metadata elapsed", async () => {
    const retainedState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "cutover-preview-ready",
      plan: {
        ...prepared.plan!,
        status: "cutover-preview-ready",
        runtimeRecordsCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          materializationId: "mpm_retained",
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
        cutover: {
          ...prepared.plan!.cutover,
          preview: {
            previewId: "mpcv_retained",
            status: "ready",
            createdAtUtc: "2026-07-19T00:05:00Z",
            expiresAtUtc: "2026-07-19T00:20:00Z",
            snapshotSha256: "e".repeat(64),
            blockers: [],
            routes: [],
          },
        },
      },
    }

    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () =>
        HttpResponse.json(retainedState)),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    expect(await screen.findByText("mpcv_retained")).toBeInTheDocument()
    expect(screen.getByText("ready")).toBeInTheDocument()
    expect(screen.queryByText(/Expires/)).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Execute controlled public cutover" })).toBeInTheDocument()
  })

  it("clears cutover acknowledgements when a replacement snapshot is prepared", async () => {
    const user = userEvent.setup()
    const readyState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "cutover-preview-ready",
      plan: {
        ...prepared.plan!,
        status: "cutover-preview-ready",
        runtimeRecordsCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          materializationId: "mpm_refresh",
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
        cutover: {
          ...prepared.plan!.cutover,
          preview: {
            previewId: "mpcv_before_refresh",
            status: "ready",
            createdAtUtc: "2099-07-19T00:05:00Z",
            expiresAtUtc: "2099-07-19T00:20:00Z",
            snapshotSha256: "f".repeat(64),
            blockers: [],
            routes: [],
          },
        },
      },
    }
    const refreshedState: MigrationProductionAdoptionState = {
      ...readyState,
      plan: {
        ...readyState.plan!,
        cutover: {
          ...readyState.plan!.cutover,
          preview: {
            ...readyState.plan!.cutover.preview,
            previewId: "mpcv_after_refresh",
            snapshotSha256: "9".repeat(64),
          },
        },
      },
    }
    let state = readyState
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/cutover/preview", () => {
        state = refreshedState
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(
      <MigrationProductionAdoptionWorkspace
        detail={detail}
        productionAuthorityType="operator-attested-snapshot"
      />,
    )

    for (const label of [
      /active operator-attested snapshot authority/, /runtime is privately healthy/, /route snapshot/,
      /creates or changes public Matrix/, /does not change DNS/, /does not create or import certificates/,
      /rollback assurance depends on the selected production authority/, /production verification is required/,
    ]) {
      await user.click(await screen.findByLabelText(label))
    }

    await user.click(screen.getByRole("button", { name: "Refresh route snapshot" }))
    expect(await screen.findByText("mpcv_after_refresh")).toBeInTheDocument()

    for (const checkbox of screen.getAllByRole("checkbox")) {
      expect(checkbox).not.toBeChecked()
    }
  })

  it("uses production-authority acknowledgement instead of a false source-frozen claim for simplified cutover", async () => {
    const user = userEvent.setup()
    let requestBody: unknown
    const state: MigrationProductionAdoptionState = {
      ...prepared,
      status: "cutover-preview-ready",
      plan: {
        ...prepared.plan!, status: "cutover-preview-ready", runtimeRecordsCreated: true,
        materialization: { ...prepared.plan!.materialization, materializationId: "mpm_operator_1", status: "private-runtime-ready", productionDatabaseImported: true, matrixContainerStarted: true, matrixHealthPassed: true, elementContainerStarted: true, elementHealthPassed: true, runtimeManifestSaved: true, databaseOwnershipSaved: true, runtimeRecordsCreated: true, userInventorySynchronized: true },
        cutover: { ...prepared.plan!.cutover, preview: { previewId: "mpcv_operator_1", status: "ready", createdAtUtc: "2026-07-19T00:05:00Z", expiresAtUtc: "2099-07-19T00:20:00Z", snapshotSha256: "b".repeat(64), blockers: [], routes: [] } },
      },
    }
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/cutover/execution", async ({ request }) => { requestBody = await request.json(); return HttpResponse.json(state) }),
    )
    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} productionAuthorityType="operator-attested-snapshot" />)
    expect(await screen.findByLabelText(/active operator-attested snapshot authority/)).toBeInTheDocument()
    expect(screen.queryByLabelText(/source writers are frozen/)).not.toBeInTheDocument()
    for (const label of [/active operator-attested snapshot authority/, /runtime is privately healthy/, /route snapshot/, /creates or changes public Matrix/, /does not change DNS/, /does not create or import certificates/, /rollback assurance depends on the selected production authority/, /production verification is required/]) await user.click(screen.getByLabelText(label))
    await user.click(screen.getByRole("button", { name: "Execute controlled public cutover" }))
    await waitFor(() => expect(requestBody).toMatchObject({ previewId: "mpcv_operator_1", acknowledgeSourceFrozen: false, acknowledgeProductionAuthority: true, acknowledgeRollbackIsNextSlice: true }))
  })

  it("runs production verification and reconstructs fresh durable evidence", async () => {
    const user = userEvent.setup()
    let requestBody: unknown
    const publicState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "public-awaiting-verification",
      plan: {
        ...prepared.plan!,
        status: "public-awaiting-verification",
        runtimeRecordsCreated: true,
        publicRoutesCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
        cutover: {
          ...prepared.plan!.cutover,
          execution: {
            ...prepared.plan!.cutover.execution,
            executionId: "mpce_001",
            status: "public-awaiting-verification",
            targetPublicAtUtc: "2026-07-20T09:30:00Z",
            matrixRouteId: "11",
            elementRouteId: "12",
            runtimePromotionCompleted: true,
            publicRoutesCreated: true,
          },
        },
      },
    }
    const verifiedState: MigrationProductionAdoptionState = {
      ...publicState,
      status: "production-verification-passed",
      plan: {
        ...publicState.plan!,
        status: "production-verification-passed",
        productionVerification: {
          verificationId: "mpvf_001",
          status: "passed",
          startedAtUtc: "2026-07-20T09:31:00Z",
          completedAtUtc: "2026-07-20T09:32:00Z",
          validUntilUtc: "2026-07-20T10:02:00Z",
          fresh: true,
          passed: true,
          checkCount: 2,
          failedCheckCount: 0,
          evidenceSha256: "8".repeat(64),
          readinessReportId: "44444444-4444-4444-4444-444444444444",
          checks: [
            {
              code: "migration-production.runtime-stack.identity",
              name: "Normal runtime ownership",
              success: true,
              detail: "The normal RuntimeStack and service records match the adoption plan.",
              url: null,
              statusCode: null,
            },
            {
              code: "migration-production.readiness.matrix-public",
              name: "Public Matrix readiness",
              success: true,
              detail: "The public Matrix endpoint returned a ready response.",
              url: "https://matrix-davids.deltabox.dev/_matrix/client/versions",
              statusCode: 200,
            },
          ],
          failureCode: null,
          failureSummary: null,
        },
      },
    }
    let state = publicState
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/verification", async ({ request }) => {
        requestBody = await request.json()
        state = verifiedState
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    expect(await screen.findByText("Production verification gate")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Run production verification" }))

    await waitFor(() => expect(requestBody).toEqual({
      operator: null,
      note: null,
      freshnessMinutes: null,
    }))
    expect(await screen.findByText("Production verification passed")).toBeInTheDocument()
    expect(screen.getByText("mpvf_001")).toBeInTheDocument()
    expect(screen.getByText("Normal runtime ownership")).toBeInTheDocument()
    expect(screen.getByText("Public Matrix readiness")).toBeInTheDocument()
    expect(screen.getByText("HTTP 200")).toBeInTheDocument()
    expect(screen.getAllByText("Passed")).toHaveLength(2)
    expect(screen.getByText("2 checks · 0 failed")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Rerun production verification" })).toBeEnabled()
  })

  it("keeps failed or expired verification visible and rerunnable before rollback", async () => {
    const failedState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "production-verification-failed",
      plan: {
        ...prepared.plan!,
        status: "production-verification-failed",
        runtimeRecordsCreated: true,
        publicRoutesCreated: true,
        materialization: { ...prepared.plan!.materialization, status: "private-runtime-ready" },
        cutover: {
          ...prepared.plan!.cutover,
          execution: {
            ...prepared.plan!.cutover.execution,
            executionId: "mpce_001",
            status: "public-awaiting-verification",
            runtimePromotionCompleted: true,
            publicRoutesCreated: true,
          },
        },
        productionVerification: {
          verificationId: "mpvf_failed",
          status: "failed",
          startedAtUtc: "2026-07-20T09:31:00Z",
          completedAtUtc: "2026-07-20T09:32:00Z",
          validUntilUtc: null,
          fresh: false,
          passed: false,
          checkCount: 1,
          failedCheckCount: 1,
          evidenceSha256: "7".repeat(64),
          readinessReportId: null,
          checks: [{
            code: "migration-production.route.live.matrix",
            name: "Live Matrix route",
            success: false,
            detail: "The route no longer targets the promoted Matrix container.",
            url: null,
            statusCode: null,
          }],
          failureCode: "production_verification_failed",
          failureSummary: "One production verification check failed.",
        },
      },
    }
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(failedState)),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)

    expect(await screen.findAllByText("Production verification failed")).toHaveLength(2)
    expect(screen.getByText("One production verification check failed.")).toBeInTheDocument()
    expect(screen.getByText("The route no longer targets the promoted Matrix container.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Rerun production verification" })).toBeEnabled()
  })

  it("shows durable target rollback blockers and withholds execution", async () => {
    const user = userEvent.setup()
    const publicState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "public-awaiting-verification",
      plan: {
        ...prepared.plan!,
        status: "public-awaiting-verification",
        runtimeRecordsCreated: true,
        publicRoutesCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          materializationId: "mpm_001",
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
        cutover: {
          ...prepared.plan!.cutover,
          execution: {
            executionId: "mpce_001",
            status: "public-awaiting-verification",
            startedAtUtc: "2026-07-19T00:06:00Z",
            completedAtUtc: "2026-07-19T00:07:00Z",
            targetPublicAtUtc: "2026-07-19T00:07:00Z",
            matrixRouteId: "11",
            elementRouteId: "12",
            runtimePromotionCompleted: true,
            publicRoutesCreated: true,
            routeCompensationAttempted: false,
            routeCompensationCompleted: false,
            failureCode: null,
            failureSummary: null,
          },
        },
      },
    }
    const blockedState: MigrationProductionAdoptionState = {
      ...publicState,
      plan: {
        ...publicState.plan!,
        rollback: {
          ...publicState.plan!.rollback,
          preview: {
            previewId: "mprv_blocked",
            status: "blocked",
            createdAtUtc: "2026-07-19T00:08:00Z",
            expiresAtUtc: "2026-07-19T00:23:00Z",
            snapshotSha256: "e".repeat(64),
            blockers: ["NPM route 'matrix-davids.deltabox.dev' no longer matches the exact migrated target selected by the completed cutover."],
            routes: [{
              serviceKey: "matrix",
              publicHost: "matrix-davids.deltabox.dev",
              currentRouteId: 99,
              currentForwardScheme: "http",
              currentForwardHost: "unexpected-target",
              currentForwardPort: 8008,
              currentEnabled: true,
              currentSnapshotSha256: "f".repeat(64),
              matchesExpectedTarget: false,
              restoreAction: "restore-pre-cutover-route",
            }],
          },
        },
      },
    }

    let state = publicState
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/rollback/preview", () => {
        state = blockedState
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)
    await user.click(await screen.findByRole("button", { name: "Prepare target rollback preview" }))

    expect(await screen.findByText("Target rollback is blocked")).toBeInTheDocument()
    expect(screen.getByText(/no longer matches the exact migrated target/)).toBeInTheDocument()
    expect(screen.getByText("Route changed")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Execute target rollback" })).not.toBeInTheDocument()
  })

  it("requires step-up, reconstructs completed target rollback, and downloads the source handoff", async () => {
    const user = userEvent.setup()
    let rollbackAttempts = 0
    let rollbackBody: unknown
    let handoffAttempts = 0
    const createObjectUrl = vi.fn(() => "blob:source-handoff")
    const revokeObjectUrl = vi.fn()
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectUrl })
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectUrl })
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined)

    const publicState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "public-awaiting-verification",
      plan: {
        ...prepared.plan!,
        status: "public-awaiting-verification",
        runtimeRecordsCreated: true,
        publicRoutesCreated: true,
        materialization: {
          ...prepared.plan!.materialization,
          materializationId: "mpm_001",
          status: "private-runtime-ready",
          productionDatabaseImported: true,
          matrixContainerStarted: true,
          matrixHealthPassed: true,
          elementContainerStarted: true,
          elementHealthPassed: true,
          runtimeManifestSaved: true,
          databaseOwnershipSaved: true,
          runtimeRecordsCreated: true,
          userInventorySynchronized: true,
        },
        cutover: {
          ...prepared.plan!.cutover,
          execution: {
            executionId: "mpce_001",
            status: "public-awaiting-verification",
            startedAtUtc: "2026-07-19T00:06:00Z",
            completedAtUtc: "2026-07-19T00:07:00Z",
            targetPublicAtUtc: "2026-07-19T00:07:00Z",
            matrixRouteId: "11",
            elementRouteId: "12",
            runtimePromotionCompleted: true,
            publicRoutesCreated: true,
            routeCompensationAttempted: false,
            routeCompensationCompleted: false,
            failureCode: null,
            failureSummary: null,
          },
        },
      },
    }
    const previewReady: MigrationProductionAdoptionState = {
      ...publicState,
      status: "rollback-preview-ready",
      plan: {
        ...publicState.plan!,
        status: "rollback-preview-ready",
        rollback: {
          ...publicState.plan!.rollback,
          preview: {
            previewId: "mprv_001",
            status: "ready",
            createdAtUtc: "2026-07-19T00:08:00Z",
            expiresAtUtc: "2026-07-19T00:23:00Z",
            snapshotSha256: "e".repeat(64),
            blockers: [],
            routes: [
              {
                serviceKey: "matrix",
                publicHost: "matrix-davids.deltabox.dev",
                currentRouteId: 11,
                currentForwardScheme: "http",
                currentForwardHost: "mem-matrix-davids-stack",
                currentForwardPort: 8008,
                currentEnabled: true,
                currentSnapshotSha256: "f".repeat(64),
                matchesExpectedTarget: true,
                restoreAction: "restore-pre-cutover-route",
              },
              {
                serviceKey: "element-web",
                publicHost: "chat-davids.deltabox.dev",
                currentRouteId: 12,
                currentForwardScheme: "http",
                currentForwardHost: "mem-element-davids-stack",
                currentForwardPort: 80,
                currentEnabled: true,
                currentSnapshotSha256: "1".repeat(64),
                matchesExpectedTarget: true,
                restoreAction: "remove-target-route",
              },
            ],
          },
        },
      },
    }
    const completedState: MigrationProductionAdoptionState = {
      ...previewReady,
      status: "target-rolled-back-awaiting-source",
      plan: {
        ...previewReady.plan!,
        status: "target-rolled-back-awaiting-source",
        publicRoutesCreated: false,
        cutover: {
          ...previewReady.plan!.cutover,
          execution: {
            ...previewReady.plan!.cutover.execution,
            status: "target-rolled-back-awaiting-source",
            publicRoutesCreated: false,
          },
        },
        rollback: {
          ...previewReady.plan!.rollback,
          execution: {
            executionId: "mpre_001",
            status: "target-rolled-back-awaiting-source",
            startedAtUtc: "2026-07-19T00:09:00Z",
            completedAtUtc: "2026-07-19T00:10:00Z",
            routesRestored: true,
            runtimeRoutesRemoved: true,
            targetContainersStopped: true,
            targetRouteCompensationAttempted: false,
            targetRouteCompensationCompleted: false,
            sourceHandoffId: "mpsh_001",
            sourceHandoffSha256: "2".repeat(64),
            failureCode: null,
            failureSummary: null,
          },
        },
      },
    }

    let state = publicState
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/rollback/preview", () => {
        state = previewReady
        return HttpResponse.json(state)
      }),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/rollback/execution", async ({ request }) => {
        rollbackAttempts += 1
        rollbackBody = await request.json()
        if (rollbackAttempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent step-up required." },
            { status: 403 },
          )
        }
        state = completedState
        return HttpResponse.json(state)
      }),
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption/rollback/source-handoff", () => {
        handoffAttempts += 1
        if (handoffAttempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent step-up required." },
            { status: 403 },
          )
        }
        return HttpResponse.json(
          { schemaVersion: "mem.migration.source-restoration-handoff.v1", payloadSha256: "2".repeat(64), payload: { handoffId: "mpsh_001" } },
          { headers: { "Content-Disposition": "attachment; filename=mpsh_001.mem-source-restoration-handoff.json" } },
        )
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)
    expect(await screen.findByText("Coordinated pre-acceptance rollback")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Prepare target rollback preview" }))

    expect(await screen.findByText("mprv_001")).toBeInTheDocument()
    expect(screen.getAllByText("Target matched")).toHaveLength(2)
    expect(screen.getByText("Rollback action: restore-pre-cutover-route")).toBeInTheDocument()

    const executeButton = screen.getByRole("button", { name: "Execute target rollback" })
    expect(executeButton).toBeDisabled()
    for (const label of [
      /migration has not been accepted/,
      /restore or remove the Matrix and Element routes/,
      /stop the migrated target Matrix and Element containers/,
      /target databases, manifests, runtime records/,
      /legacy source remains frozen/,
      /source restoration requires the generated/,
      /does not automatically control or mutate the source host/,
    ]) {
      await user.click(screen.getByLabelText(label))
    }

    expect(executeButton).toBeEnabled()
    await user.click(executeButton)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(rollbackAttempts).toBe(2))
    expect(rollbackBody).toEqual({
      operator: null,
      note: null,
      previewId: "mprv_001",
      executeTargetRollback: true,
      acknowledgeMigrationNotAccepted: true,
      acknowledgeRestoresPreCutoverRoutes: true,
      acknowledgeStopsTargetContainers: true,
      acknowledgePreservesTargetData: true,
      acknowledgeSourceRemainsFrozen: true,
      acknowledgeSourceRestorationRequiresHandoff: true,
      acknowledgeNoAutomaticSourceHostMutation: true,
    })
    expect(await screen.findByText("Target rollback completed; source restoration is pending")).toBeInTheDocument()
    expect(screen.getByText("mpsh_001")).toBeInTheDocument()
    expect(screen.getByText("Pre-cutover public routes restored")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Download source-restoration handoff" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(handoffAttempts).toBe(2))
    expect(createObjectUrl).toHaveBeenCalledTimes(1)
    expect(anchorClick).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:source-handoff")
  })


  it("imports source completion after step-up and reconstructs coordinated rollback closure", async () => {
    const user = userEvent.setup()
    let importAttempts = 0
    let importedBody: unknown
    const awaitingState: MigrationProductionAdoptionState = {
      ...prepared,
      status: "target-rolled-back-awaiting-source",
      plan: {
        ...prepared.plan!,
        status: "target-rolled-back-awaiting-source",
        runtimeRecordsCreated: true,
        materialization: { ...prepared.plan!.materialization, status: "private-runtime-ready" },
        rollback: {
          ...prepared.plan!.rollback,
          execution: {
            ...prepared.plan!.rollback.execution,
            executionId: "mpre_001",
            status: "target-rolled-back-awaiting-source",
            routesRestored: true,
            runtimeRoutesRemoved: true,
            targetContainersStopped: true,
            sourceHandoffId: "mpsh_001",
            sourceHandoffSha256: "2".repeat(64),
          },
        },
      },
    }
    const closedState: MigrationProductionAdoptionState = {
      ...awaitingState,
      status: "coordinated-rollback-complete",
      plan: {
        ...awaitingState.plan!,
        status: "coordinated-rollback-complete",
        rollback: {
          ...awaitingState.plan!.rollback,
          execution: { ...awaitingState.plan!.rollback.execution, status: "coordinated-rollback-complete" },
          completion: {
            status: "coordinated-rollback-complete",
            restorationAttemptId: "mm01cb-test",
            completionSha256: "9".repeat(64),
            importedAtUtc: "2026-07-20T08:10:00Z",
            completedAtUtc: "2026-07-20T08:10:00Z",
            sourceRestored: true,
            restartPoliciesRestored: true,
            originalRunningStatesRestored: true,
            matrixVerified: true,
            elementVerified: true,
            targetRollbackAuthorityVerified: true,
            targetRollbackStillIntact: true,
            developmentExternalControlPlane: false,
          },
        },
      },
    }
    const completion = {
      schemaVersion: "mem.migration.source-restoration-completion.v1",
      payloadSha256: "9".repeat(64),
      payload: { restorationAttemptId: "mm01cb-test" },
    }
    let state = awaitingState
    server.use(
      http.get("/api/operator/migrations/sessions/mig_adoption/production-adoption", () => HttpResponse.json(state)),
      http.post("/api/operator/migrations/sessions/mig_adoption/production-adoption/rollback/source-completion", async ({ request }) => {
        importAttempts += 1
        importedBody = await request.json()
        if (importAttempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent step-up required." },
            { status: 403 },
          )
        }
        state = closedState
        return HttpResponse.json(state)
      }),
    )

    renderWithProviders(<MigrationProductionAdoptionWorkspace detail={detail} />)
    const input = await screen.findByLabelText("Choose source-restoration completion file")
    await user.upload(input, new File([JSON.stringify(completion)], "source-restoration-completion.json", { type: "application/json" }))
    await user.click(screen.getByRole("button", { name: "Import and close coordinated rollback" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(importAttempts).toBe(2))
    expect(importedBody).toEqual(completion)
    expect(await screen.findByText("Coordinated rollback is complete")).toBeInTheDocument()
    expect(screen.getByText("mm01cb-test")).toBeInTheDocument()
    expect(screen.getAllByText("Yes").length).toBeGreaterThanOrEqual(2)
  })

})
