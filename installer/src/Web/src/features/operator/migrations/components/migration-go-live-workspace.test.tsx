import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import type { ReactElement } from "react"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"
import type {
  MigrationProductionAdoptionState,
} from "@/features/operator/migrations/api/migration-production-adoption"

import { MigrationGoLiveWorkspace } from "./migration-go-live-workspace"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onOpenChange, onVerified }: {
    open: boolean
    onOpenChange: (open: boolean) => void
    onVerified?: () => void
  }) => open ? <div>
    <button onClick={() => { onOpenChange(false); onVerified?.() }}>Complete step-up</button>
    <button onClick={() => onOpenChange(false)}>Cancel step-up</button>
  </div> : null,
}))

const migrationId = "mig_go_live"

function stateWith(
  status: string,
  overrides: {
    previewStatus?: string
    previewId?: string | null
    previewBlockers?: string[]
    cutoverStatus?: string
    publicRoutesCreated?: boolean
    runtimePromotionCompleted?: boolean
    compensationAttempted?: boolean
    compensationCompleted?: boolean
    verificationStatus?: string
    verificationPassed?: boolean
    failedChecks?: number
    failureSummary?: string | null
  } = {},
): MigrationProductionAdoptionState {
  const failedChecks = overrides.failedChecks ?? 0
  const verificationPassed = overrides.verificationPassed ?? false
  return {
    source: "control-plane",
    status,
    migrationId,
    planPrepared: true,
    detail: status,
    plan: {
      adoptionPlanId: "madp_go_live",
      migrationId,
      packageRevisionId: "mpr_preview",
      candidateArtifactId: "mca_preview",
      stagingRunId: "mstg_preview",
      status,
      revisionNumber: 1,
      planSha256: "a".repeat(64),
      createdAtUtc: "2026-07-25T02:00:00Z",
      updatedAtUtc: "2026-07-25T02:01:00Z",
      preparedAtUtc: "2026-07-25T02:00:00Z",
      runtimeStackId: "11111111-1111-1111-1111-111111111111",
      targetStackSlug: "tester",
      targetDisplayName: "Tester",
      matrixInstanceId: "22222222-2222-2222-2222-222222222222",
      elementInstanceId: "33333333-3333-3333-3333-333333333333",
      matrixServerName: "matrix.example.test",
      matrixPublicHost: "matrix.example.test",
      matrixPublicBaseUrl: "https://matrix.example.test",
      elementPublicHost: "element.example.test",
      elementPublicBaseUrl: "https://element.example.test",
      runtimeNetworkName: "mem-gateway",
      matrixContainerName: "mem-matrix-tester",
      elementContainerName: "mem-element-tester",
      matrixImageReference: "synapse:approved",
      matrixImageId: "sha256:synapse",
      elementImageReference: "element:approved",
      elementImageId: "sha256:element",
      databaseEngine: "postgres",
      databaseHost: "mem-postgres",
      databasePort: 5432,
      databaseName: "matrix_tester",
      databaseUsername: "mxu_tester",
      databasePasswordSecretKind: "matrix_postgres_password",
      expectedUsersCount: 3,
      expectedRoomsCount: 2,
      expectedEventsCount: 28,
      routes: [
        {
          serviceKey: "matrix",
          publicHost: "matrix.example.test",
          publicBaseUrl: "https://matrix.example.test",
          forwardScheme: "http",
          forwardHost: "mem-matrix-tester",
          forwardPort: 8008,
          provider: "npm",
          publicMutationDeferred: true,
        },
        {
          serviceKey: "element-web",
          publicHost: "element.example.test",
          publicBaseUrl: "https://element.example.test",
          forwardScheme: "http",
          forwardHost: "mem-element-tester",
          forwardPort: 80,
          provider: "npm",
          publicMutationDeferred: true,
        },
      ],
      collisions: [],
      collisionFree: true,
      runtimeRecordsCreated: true,
      publicRoutesCreated: overrides.publicRoutesCreated ?? false,
      materialization: {
        materializationId: "mpm_ready",
        status: "private-runtime-ready",
        startedAtUtc: "2026-07-25T02:00:00Z",
        completedAtUtc: "2026-07-25T02:01:00Z",
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
      cutover: {
        preview: {
          previewId: overrides.previewId ?? null,
          status: overrides.previewStatus ?? "not-prepared",
          createdAtUtc: overrides.previewId ? "2026-07-25T02:02:00Z" : null,
          expiresAtUtc: null,
          snapshotSha256: overrides.previewId ? "b".repeat(64) : null,
          routes: overrides.previewId ? [
            {
              serviceKey: "matrix",
              publicHost: "matrix.example.test",
              desiredForwardHost: "mem-matrix-tester",
              desiredForwardPort: 8008,
              existingRouteFound: true,
              existingRouteId: 90,
              existingForwardScheme: "http",
              existingForwardHost: "old-matrix",
              existingForwardPort: 8008,
              existingCertificateId: 3,
              existingSslForced: true,
              existingHttp2: true,
              existingEnabled: true,
              existingAdvancedConfigSha256: "c".repeat(64),
              alreadyTargetsProductionRuntime: false,
              action: "update",
              selectedCertificateId: 3,
              selectedCertificateRecordId: "cert-wildcard",
              selectedCertificateName: "*.example.test",
            },
            {
              serviceKey: "element-web",
              publicHost: "element.example.test",
              desiredForwardHost: "mem-element-tester",
              desiredForwardPort: 80,
              existingRouteFound: true,
              existingRouteId: 91,
              existingForwardScheme: "http",
              existingForwardHost: "old-element",
              existingForwardPort: 80,
              existingCertificateId: 3,
              existingSslForced: true,
              existingHttp2: true,
              existingEnabled: true,
              existingAdvancedConfigSha256: "d".repeat(64),
              alreadyTargetsProductionRuntime: false,
              action: "update",
              selectedCertificateId: 3,
              selectedCertificateRecordId: "cert-wildcard",
              selectedCertificateName: "*.example.test",
            },
          ] : [],
          blockers: overrides.previewBlockers ?? [],
        },
        execution: {
          executionId: overrides.cutoverStatus ? "mpce_go_live" : null,
          status: overrides.cutoverStatus ?? "not-started",
          startedAtUtc: overrides.cutoverStatus ? "2026-07-25T02:03:00Z" : null,
          completedAtUtc: overrides.cutoverStatus ? "2026-07-25T02:04:00Z" : null,
          targetPublicAtUtc: overrides.publicRoutesCreated ? "2026-07-25T02:04:00Z" : null,
          matrixRouteId: overrides.publicRoutesCreated ? "97" : null,
          elementRouteId: overrides.publicRoutesCreated ? "98" : null,
          runtimePromotionCompleted: overrides.runtimePromotionCompleted ?? false,
          publicRoutesCreated: overrides.publicRoutesCreated ?? false,
          routeCompensationAttempted: overrides.compensationAttempted ?? false,
          routeCompensationCompleted: overrides.compensationCompleted ?? false,
          failureCode: overrides.cutoverStatus === "failed" ? "migration_production_cutover_npm_failed" : null,
          failureSummary: overrides.cutoverStatus === "failed"
            ? overrides.failureSummary ?? "NPM route publication failed."
            : null,
        },
      },
      productionVerification: {
        verificationId: overrides.verificationStatus ? "mpvf_go_live" : null,
        status: overrides.verificationStatus ?? "not-started",
        startedAtUtc: overrides.verificationStatus ? "2026-07-25T02:04:00Z" : null,
        completedAtUtc: overrides.verificationStatus && overrides.verificationStatus !== "running"
          ? "2026-07-25T02:05:00Z"
          : null,
        validUntilUtc: null,
        fresh: verificationPassed,
        passed: verificationPassed,
        checkCount: overrides.verificationStatus ? 24 : 0,
        failedCheckCount: failedChecks,
        evidenceSha256: overrides.verificationStatus ? "e".repeat(64) : null,
        readinessReportId: overrides.verificationStatus ? "44444444-4444-4444-4444-444444444444" : null,
        checks: overrides.verificationStatus ? [
          {
            code: "migration-production.runtime-stack",
            name: "Runtime Stack identity",
            success: failedChecks === 0,
            detail: failedChecks === 0 ? "Runtime Stack identity passed." : "Runtime Stack identity failed.",
            url: null,
            statusCode: null,
          },
        ] : [],
        failureCode: failedChecks > 0 ? "production_verification_failed" : null,
        failureSummary: failedChecks > 0 ? "One production check failed." : null,
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
      blockerSummary: overrides.previewBlockers?.join("; ") ?? null,
    },
  }
}

let currentState: MigrationProductionAdoptionState

function workspaceFromFixture() {
  const plan = currentState.plan!
  const status = currentState.status
  const passed = status === "production-verification-passed"
  const verificationFailed = status === "production-verification-failed"
  const cutoverFailed = status === "cutover-failed"
  const blocked = plan.cutover.preview.status === "blocked"
  const compensated = plan.cutover.execution.routeCompensationAttempted && plan.cutover.execution.routeCompensationCompleted
  const action = passed ? "finish-migration" : verificationFailed ? "rerun-live-checks" : cutoverFailed
    ? compensated ? "review-go-live-again" : "open-cutover-recovery"
    : plan.cutover.preview.status === "ready" ? "make-server-live" : "review-go-live"
  const workspace = guidedWorkspace({ migrationId, stage: passed ? "finish-migration" : "make-new-server-live",
    state: verificationFailed || cutoverFailed ? "failed" : blocked ? "blocked" : "ready", action,
    guided: { adoptionPlanId: plan.adoptionPlanId, privateRuntimeReady: true,
      cutoverPreviewId: plan.cutover.preview.previewId, cutoverPreviewStatus: plan.cutover.preview.status,
      publicRoutesCreated: plan.cutover.execution.publicRoutesCreated,
      runtimePromotionCompleted: plan.cutover.execution.runtimePromotionCompleted,
    },
  })
  workspace.guided.operationRevisions["review-go-live"] = `${plan.cutover.preview.previewId}:${plan.cutover.preview.status}`
  workspace.guided.operationRevisions["make-server-live"] = `${plan.cutover.execution.executionId}:${status}:${plan.productionVerification.status}`
  workspace.verification = { status: plan.productionVerification.status, hasRun: passed || verificationFailed,
    allPassed: passed ? true : verificationFailed ? false : null, checkCount: plan.productionVerification.checkCount,
    failedCheckCount: plan.productionVerification.failedCheckCount, checkedAtUtc: plan.productionVerification.completedAtUtc }
  if (verificationFailed || cutoverFailed) workspace.stages[4].failureOutcome = {
    failureCode: "test-failure", failedComponent: verificationFailed ? "production-verification" : "public-route-cutover",
    mutationState: "completed", compensationState: compensated ? "completed" : "incomplete",
    observedCurrentState: verificationFailed ? "new-server-public-unverified" : "route-recovery",
    safeToRetry: verificationFailed || compensated, safeStateSummaryCode: "test-outcome", nextAction: workspace.guided.nextAction,
  }
  return workspace
}

function renderWithProviders(ui: ReactElement) { return renderGuided(ui, workspaceFromFixture) }

beforeEach(() => {
  sessionStorage.clear()
  currentState = stateWith("private-runtime-ready")
  server.use(
    http.get(
      `/api/operator/migrations/sessions/${migrationId}/production-adoption`,
      () => HttpResponse.json(currentState),
    ),
    http.post(
      `/api/operator/migrations/sessions/${migrationId}/production-adoption/cutover/preview`,
      () => {
        currentState = stateWith("cutover-preview-ready", {
          previewStatus: "ready",
          previewId: "mpcv_go_live",
        })
        return HttpResponse.json(currentState)
      },
    ),
  )
})

describe("MigrationGoLiveWorkspace", () => {
  it("prepares the route review automatically and uses one confirmed go-live operation", async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`,
        async ({ request }) => {
          requestBody = await request.json()
          currentState = stateWith("production-verification-passed", {
            previewStatus: "ready",
            previewId: "mpcv_go_live",
            cutoverStatus: "public-awaiting-verification",
            publicRoutesCreated: true,
            runtimePromotionCompleted: true,
            verificationStatus: "passed",
            verificationPassed: true,
          })
          return HttpResponse.json(currentState)
        },
      ),
    )

    renderWithProviders(
      <MigrationGoLiveWorkspace
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Public route review")).toBeInTheDocument()
    expect(await screen.findAllByText("Certificate: *.example.test")).toHaveLength(2)
    await user.click(screen.getByRole("button", { name: "Make server live" }))

    const dialog = screen.getByRole("alertdialog")
    expect(within(dialog).getByText(/stop normal use of the old server/i)).toBeInTheDocument()
    expect(within(dialog).getByText(/may remain public/i)).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Make server live" }))

    await waitFor(() => expect(requestBody).toEqual({
      confirmMovePublicTraffic: true,
      confirmStopUsingOldServer: true,
      confirmRunLiveVerification: true,
    }))
    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
    expect(screen.getByText("24 production checks passed.")).toBeInTheDocument()
  })


  it("requires fresh step-up at the public cutover boundary and retries the exact confirmed request", async () => {
    const user = userEvent.setup()
    let attempts = 0
    const bodies: unknown[] = []
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`,
        async ({ request }) => {
          attempts += 1
          bodies.push(await request.json())
          if (attempts === 1) {
            return HttpResponse.json({ error: "step_up_required" }, { status: 403 })
          }
          currentState = stateWith("production-verification-passed", {
            previewStatus: "ready",
            previewId: "mpcv_go_live",
            cutoverStatus: "public-awaiting-verification",
            publicRoutesCreated: true,
            runtimePromotionCompleted: true,
            verificationStatus: "passed",
            verificationPassed: true,
          })
          return HttpResponse.json(currentState)
        },
      ),
    )

    renderWithProviders(
      <MigrationGoLiveWorkspace
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Public route review")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Make server live" }))
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Make server live" }))

    expect(await screen.findByRole("button", { name: "Complete step-up" })).toBeInTheDocument()
    expect(attempts).toBe(1)
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(attempts).toBe(2))
    expect(bodies).toEqual([
      {
        confirmMovePublicTraffic: true,
        confirmStopUsingOldServer: true,
        confirmRunLiveVerification: true,
      },
      {
        confirmMovePublicTraffic: true,
        confirmStopUsingOldServer: true,
        confirmRunLiveVerification: true,
      },
    ])
    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
  })


  it("shows a busy go-live confirmation until accepted evidence arrives and prevents duplicate publication", async () => {
    const user = userEvent.setup()
    let release!: () => void
    const pending = new Promise<void>((resolve) => { release = resolve })
    let posts = 0
    server.use(http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`, async () => {
      posts += 1
      await pending
      currentState = stateWith("production-verification-passed", {
        previewStatus: "ready", previewId: "preview_busy", cutoverStatus: "public-awaiting-verification",
        publicRoutesCreated: true, runtimePromotionCompleted: true, verificationStatus: "passed", verificationPassed: true,
      })
      return HttpResponse.json(currentState)
    }))
    renderWithProviders(<MigrationGoLiveWorkspace migrationId={migrationId} assuranceMode="simplified" onChanged={async () => undefined} />)
    await screen.findByText("Public route review")
    await user.click(screen.getByRole("button", { name: "Make server live" }))
    const dialog = screen.getByRole("alertdialog")
    try {
      await user.dblClick(within(dialog).getByRole("button", { name: "Make server live" }))
      const busy = await within(dialog).findByRole("button", { name: "Making server live..." })
      expect(busy).toBeDisabled()
      expect(busy.querySelector("svg.animate-spin")).not.toBeNull()
      expect(within(dialog).getByRole("status")).toHaveTextContent("Publishing the reviewed routes")
      expect(within(dialog).getByRole("button", { name: "Cancel" })).toBeDisabled()
      await waitFor(() => expect(posts).toBe(1))
    } finally {
      await act(async () => release())
    }
    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
  })

  it("adopts matching go-live evidence after a lost response instead of claiming publication failed", async () => {
    const user = userEvent.setup()
    let posts = 0
    server.use(http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`, () => {
      posts += 1
      currentState = stateWith("production-verification-passed", {
        previewStatus: "ready", previewId: "preview_lost", cutoverStatus: "public-awaiting-verification",
        publicRoutesCreated: true, runtimePromotionCompleted: true, verificationStatus: "passed", verificationPassed: true,
      })
      return HttpResponse.error()
    }))
    renderWithProviders(<MigrationGoLiveWorkspace migrationId={migrationId} assuranceMode="simplified" onChanged={async () => { throw new Error("Follow-up unavailable") }} />)
    await screen.findByText("Public route review")
    await user.click(screen.getByRole("button", { name: "Make server live" }))
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Make server live" }))
    expect(await screen.findByText("New server is live and verified")).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(posts).toBe(1)
  })

  it("settles a blocked route review and retries only after explicit operator action", async () => {
    const user = userEvent.setup()
    let previewCalls = 0
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/cutover/preview`,
        () => {
          previewCalls += 1
          currentState = stateWith("private-runtime-ready", {
            previewStatus: "blocked",
            previewId: `mpcv_blocked_${previewCalls}`,
            previewBlockers: [
              "Active certificate '*.example.test' is not ready in Nginx Proxy Manager.",
            ],
          })
          return HttpResponse.json(currentState)
        },
      ),
    )

    const view = renderWithProviders(
      <MigrationGoLiveWorkspace
        key="initial"
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText(/not ready in Nginx Proxy Manager/i)).toBeInTheDocument()
    await waitFor(() => expect(previewCalls).toBe(1))
    expect(screen.queryByText("Preparing the public route review...")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retry route review" })).toBeEnabled()
    expect(screen.getByRole("button", { name: "Make server live" })).toBeDisabled()

    view.rerender(
      <MigrationGoLiveWorkspace
        key="durable-refresh"
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    await waitFor(() => expect(previewCalls).toBe(1))

    await user.click(screen.getByRole("button", { name: "Retry route review" }))
    await waitFor(() => expect(previewCalls).toBe(2))
  })

  it("keeps a verification failure public and offers a checks-only retry", async () => {
    const user = userEvent.setup()
    currentState = stateWith("production-verification-failed", {
      previewStatus: "ready",
      previewId: "mpcv_go_live",
      cutoverStatus: "public-awaiting-verification",
      publicRoutesCreated: true,
      runtimePromotionCompleted: true,
      verificationStatus: "failed",
      failedChecks: 1,
    })
    let calls = 0
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`,
        () => {
          calls += 1
          return HttpResponse.json(currentState)
        },
      ),
    )

    renderWithProviders(
      <MigrationGoLiveWorkspace
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("The new server is public, but checks need attention")).toBeInTheDocument()
    expect(screen.getByText(/were not automatically rolled back/i)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Run checks again" }))
    await waitFor(() => expect(calls).toBe(1))
  })

  it("offers a safe retry only after verified route compensation", async () => {
    currentState = stateWith("cutover-failed", {
      previewStatus: "failed",
      previewId: "mpcv_failed",
      cutoverStatus: "failed",
      compensationAttempted: true,
      compensationCompleted: true,
      failureSummary: "Previous routes were restored.",
    })

    renderWithProviders(
      <MigrationGoLiveWorkspace
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Go-live did not complete; previous routes were restored")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Try going live again" })).toBeEnabled()
  })

  it("withholds retry when the public route state is unresolved", async () => {
    currentState = stateWith("cutover-failed", {
      previewStatus: "failed",
      previewId: "mpcv_failed",
      cutoverStatus: "failed",
      publicRoutesCreated: true,
      compensationAttempted: true,
      compensationCompleted: false,
      failureSummary: "Route restoration could not be verified.",
    })
    let releaseEvidence!: () => void
    const pendingEvidence = new Promise<void>((resolve) => { releaseEvidence = resolve })
    let mutationRequests = 0
    server.use(
      http.get(`/api/operator/migrations/sessions/${migrationId}/production-adoption`, async () => {
        await pendingEvidence
        return HttpResponse.json(currentState)
      }),
      http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/cutover/preview`, () => {
        mutationRequests += 1
        return HttpResponse.json(currentState)
      }),
      http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/go-live`, () => {
        mutationRequests += 1
        return HttpResponse.json(currentState)
      }),
    )

    renderWithProviders(
      <MigrationGoLiveWorkspace
        migrationId={migrationId}
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    try {
      // The workspace must block retry before optional technical evidence arrives.
      expect(await screen.findByText("Public route state needs recovery")).toBeInTheDocument()
      expect(screen.queryByRole("button", { name: "Try going live again" })).not.toBeInTheDocument()
      expect(screen.queryByText("Technical go-live evidence")).not.toBeInTheDocument()
      expect(mutationRequests).toBe(0)
    } finally {
      await act(async () => { releaseEvidence() })
    }

    expect(await screen.findByText("Technical go-live evidence")).toBeInTheDocument()
    expect(screen.getByText("Public route state needs recovery")).toBeInTheDocument()
    expect(screen.getByText("Route restoration could not be verified.")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Try going live again" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Make server live" })).not.toBeInTheDocument()
    expect(mutationRequests).toBe(0)
  })
})
