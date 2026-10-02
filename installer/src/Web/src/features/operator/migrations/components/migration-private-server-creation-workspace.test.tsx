import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { StrictMode, type ReactElement } from "react"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"
import type { MigrationProductionAdoptionState } from "@/features/operator/migrations/api/migration-production-adoption"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import { MigrationPrivateServerCreationWorkspace } from "./migration-private-server-creation-workspace"

const migrationId = "mig_private_server"

const notPrepared: MigrationProductionAdoptionState = {
  source: "control-plane",
  status: "not-prepared",
  migrationId,
  planPrepared: false,
  plan: null,
  detail: "No production adoption plan has been prepared.",
}

const targetReview = {
  migrationId,
  sourceStackSlug: "tester",
  sourceElementPublicHost: "element.example.test",
  targetStackSlug: "tester",
  matrixServerName: "matrix.example.test",
  elementPublicHost: "element.example.test",
  stackNameStatus: "available",
  matrixAddressStatus: "locked-available",
  elementAddressStatus: "available",
  collisionFree: true,
  suggestedTargetStackSlug: null,
  collisions: [],
  detail: "The target identity is available.",
}

const privateReady = {
  source: "control-plane",
  status: "private-runtime-ready",
  migrationId,
  planPrepared: true,
  detail: "Private runtime ready.",
  plan: {
    adoptionPlanId: "mpa_private_server",
    runtimeStackId: "11111111-1111-1111-1111-111111111111",
    targetStackSlug: "tester",
    matrixServerName: "matrix.example.test",
    elementPublicHost: "element.example.test",
    databaseName: "matrix_tester",
    matrixContainerName: "mem-matrix-tester",
    elementContainerName: "mem-element-tester",
    collisions: [],
    blockerSummary: null,
    materialization: {
      materializationId: "mpm_private_server_test",
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
      publicRoutesCreated: false,
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
  },
} as unknown as MigrationProductionAdoptionState

let currentWorkspace = guidedWorkspace({ migrationId, stage: "create-new-server", action: "confirm-tested-data" })
function createdWorkspace() {
  return guidedWorkspace({ migrationId, stage: "make-new-server-live", action: "review-go-live", guided: {
    adoptionPlanId: "mpa_private_server", privateRuntimeReady: true,
    operationRevisions: { ...currentWorkspace.guided.operationRevisions, "create-new-server": "materialization-complete" },
  } })
}
function renderWithProviders(ui: ReactElement) { return renderGuided(ui, () => currentWorkspace) }
function renderStrictWithProviders(ui: ReactElement) { return renderGuided(<StrictMode>{ui}</StrictMode>, () => currentWorkspace) }

beforeEach(() => {
  sessionStorage.clear()
  currentWorkspace = guidedWorkspace({ migrationId, stage: "create-new-server", action: "confirm-tested-data" })
  server.use(
    http.get(
      `/api/operator/migrations/sessions/${migrationId}/production-adoption`,
      () => HttpResponse.json(notPrepared),
    ),
    http.post(
      `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server/review`,
      () => HttpResponse.json(targetReview),
    ),
  )
})

describe("MigrationPrivateServerCreationWorkspace", () => {
  it("settles the automatic target review under React StrictMode", async () => {
    let reviewRequests = 0
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server/review`,
        () => {
          reviewRequests += 1
          return HttpResponse.json(targetReview)
        },
      ),
    )

    renderStrictWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Target identity is available")).toBeInTheDocument()
    expect(screen.queryByText("Checking source defaults and target ownership...")).not.toBeInTheDocument()
    expect(screen.getByDisplayValue("tester")).toBeInTheDocument()
    expect(screen.getByDisplayValue("matrix.example.test")).toBeDisabled()
    expect(screen.getByDisplayValue("element.example.test")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create new server" })).toBeEnabled()
    expect(reviewRequests).toBeGreaterThanOrEqual(1)
  })

  it("uses one confirmation to create the normal server privately", async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server`,
        async ({ request }) => {
          requestBody = await request.json()
          currentWorkspace = createdWorkspace()
          return HttpResponse.json(privateReady)
        },
      ),
    )

    renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Create the new server privately")).toBeInTheDocument()
    expect(await screen.findByDisplayValue("tester")).toBeInTheDocument()
    expect(screen.getByDisplayValue("matrix.example.test")).toBeDisabled()
    expect(screen.getByDisplayValue("element.example.test")).toBeInTheDocument()
    expect(screen.getByText("Target identity is available")).toBeInTheDocument()
    expect(screen.queryByText("Production adoption plan")).not.toBeInTheDocument()
    expect(screen.queryByText("Choose how to continue")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Create new server" }))

    const dialog = screen.getByRole("alertdialog")
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByText(/the verified package and private test become authoritative/i)).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Create new server" }))

    await waitFor(() => expect(requestBody).toEqual({
      targetStackSlug: "tester",
      elementPublicHost: "element.example.test",
      confirmVerifiedSnapshotIsAuthoritative: true,
      confirmLaterSourceWritesAreNotIncluded: true,
      confirmCreatePrivateServer: true,
    }))
  })


  it("shows a spinner in the creation confirmation while the response is pending and submits once", async () => {
    const user = userEvent.setup()
    let release!: () => void
    const pending = new Promise<void>((resolve) => { release = resolve })
    let posts = 0
    let current = notPrepared
    server.use(
      http.get(`/api/operator/migrations/sessions/${migrationId}/production-adoption`, () => HttpResponse.json(current)),
      http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server`, async () => {
        posts += 1
        await pending
        current = privateReady
        currentWorkspace = createdWorkspace()
        return HttpResponse.json(current)
      }),
    )
    renderWithProviders(<MigrationPrivateServerCreationWorkspace migrationId={migrationId} matrixServerName="matrix.example.test" assuranceMode="simplified" onChanged={async () => undefined} />)
    await screen.findByText("Target identity is available")
    await user.click(screen.getByRole("button", { name: "Create new server" }))
    const dialog = screen.getByRole("alertdialog")
    try {
      await user.dblClick(within(dialog).getByRole("button", { name: "Create new server" }))
      const busy = await within(dialog).findByRole("button", { name: "Creating new server..." })
      expect(busy).toBeDisabled()
      expect(busy.querySelector("svg.animate-spin")).not.toBeNull()
      expect(within(dialog).getByRole("status")).toHaveTextContent("Creating and verifying the private server")
      expect(within(dialog).getByRole("button", { name: "Cancel" })).toBeDisabled()
      await waitFor(() => expect(posts).toBe(1))
    } finally {
      await act(async () => release())
    }
    expect(await screen.findByText("New server created privately")).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
  })

  it("rediscovers matching private creation after a lost response without keeping the dialog or replaying creation", async () => {
    const user = userEvent.setup()
    let current = notPrepared
    let posts = 0
    server.use(
      http.get(`/api/operator/migrations/sessions/${migrationId}/production-adoption`, () => HttpResponse.json(current)),
      http.post(`/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server`, () => {
        posts += 1
        current = privateReady
        currentWorkspace = createdWorkspace()
        return HttpResponse.error()
      }),
    )
    const onChanged = vi.fn(async () => { throw new Error("A later read failed") })
    renderWithProviders(<MigrationPrivateServerCreationWorkspace migrationId={migrationId} matrixServerName="matrix.example.test" assuranceMode="simplified" onChanged={onChanged} />)
    await screen.findByText("Target identity is available")
    await user.click(screen.getByRole("button", { name: "Create new server" }))
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Create new server" }))
    expect(await screen.findByText("New server created privately")).toBeInTheDocument()
    expect(onChanged).not.toHaveBeenCalled() // The shared workspace observer owns lost-response recovery.
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(posts).toBe(1)
  })

  it("resumes the exact confirmed request after step-up", async () => {
    const user = userEvent.setup()
    let attempts = 0
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server`,
        async () => {
          attempts += 1
          if (attempts === 1) {
            return HttpResponse.json(
              { status: "step_up_required", detail: "Verify identity." },
              { status: 403 },
            )
          }
          currentWorkspace = createdWorkspace()
          return HttpResponse.json(privateReady)
        },
      ),
    )

    renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    await screen.findByText("Create the new server privately")
    await screen.findByText("Target identity is available")
    await user.click(screen.getByRole("button", { name: "Create new server" }))
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Create new server" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    await waitFor(() => expect(attempts).toBe(2))
  })

  it("shows field-specific collisions and applies an available server-name suggestion", async () => {
    const user = userEvent.setup()
    let reviewCount = 0
    server.use(
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server/review`,
        async ({ request }) => {
          reviewCount += 1
          const body = await request.json() as { targetStackSlug?: string | null }
          if (body.targetStackSlug === "tester-migrated") {
            return HttpResponse.json({
              ...targetReview,
              targetStackSlug: "tester-migrated",
            })
          }

          return HttpResponse.json({
            ...targetReview,
            stackNameStatus: "conflict",
            collisionFree: false,
            suggestedTargetStackSlug: "tester-migrated",
            collisions: [{
              field: "stack-name",
              code: "runtime_stack_collision",
              resourceType: "runtime-stack",
              resourceValue: "tester",
              owner: "Tester restored (tester-restored)",
              detail: "Server name 'tester' is already owned by normal MEM stack 'Tester restored (tester-restored)'.",
            }],
          })
        },
      ),
    )

    renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText(/already owned by normal MEM stack/i)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create new server" })).toBeDisabled()

    await user.click(screen.getByRole("button", { name: "Use and check suggestion" }))

    expect(await screen.findByDisplayValue("tester-migrated")).toBeInTheDocument()
    await waitFor(() => expect(reviewCount).toBeGreaterThanOrEqual(2))
    expect(screen.getByText("Target identity is available")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create new server" })).toBeEnabled()
  })

  it("summarizes the privately created runtime and keeps evidence secondary", async () => {
    currentWorkspace = createdWorkspace()
    server.use(
      http.get(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption`,
        () => HttpResponse.json(privateReady),
      ),
    )

    renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    const privateReadyTitle = await screen.findByText("New server created privately")
    expect(privateReadyTitle).toBeInTheDocument()
    expect(privateReadyTitle.querySelector("svg")).toHaveClass("lucide-server", "shrink-0")
    expect(screen.getByText("Private server is ready")).toBeInTheDocument()
    expect(screen.getByText("None")).toBeInTheDocument()
    expect(await screen.findByText("Technical creation evidence")).toBeInTheDocument()
    expect(screen.queryByText("Create new server")).not.toBeInTheDocument()
  })
  it("keeps in-flight private-server materialization neutral and does not recheck self-owned targets", async () => {
    currentWorkspace = guidedWorkspace({ migrationId, stage: "create-new-server", state: "running", action: null })
    let reviewRequests = 0
    const readyPlan = privateReady.plan
    if (!readyPlan) {
      throw new Error("Expected the private-ready fixture to include an adoption plan.")
    }

    const materializingState = {
      ...privateReady,
      status: "materializing",
      detail: "Private server creation is running.",
      plan: {
        ...readyPlan,
        status: "materializing",
        materialization: {
          ...readyPlan.materialization,
          status: "materializing",
          completedAtUtc: null,
          runtimeManifestSaved: false,
          databaseOwnershipSaved: false,
          runtimeRecordsCreated: false,
        },
      },
    } as unknown as MigrationProductionAdoptionState

    server.use(
      http.get(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption`,
        () => HttpResponse.json(materializingState),
      ),
      http.post(
        `/api/operator/migrations/sessions/${migrationId}/production-adoption/private-server/review`,
        () => {
          reviewRequests += 1
          return HttpResponse.json({
            ...targetReview,
            collisionFree: false,
            stackNameStatus: "conflict",
            collisions: [{
              field: "stack-name",
              code: "runtime_data_path_collision",
              resourceType: "runtime-data-root",
              resourceValue: "tester",
              owner: "tester",
              detail: "One or more normal runtime data directories already exist for server name 'tester'.",
            }],
          })
        },
      ),
    )

    const view = renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    expect(await screen.findByText("Creating new server...")).toBeInTheDocument()
    // Wait for the actual evidence read, not an arbitrary delay outside act.
    await waitFor(() => {
      expect(view.queryClient.getQueryData<MigrationProductionAdoptionState>([
        "migration-guided-evidence", migrationId, "production-adoption",
        currentWorkspace.guided.operationRevisions["create-new-server"],
      ])?.status).toBe("materializing")
      expect(view.queryClient.isFetching()).toBe(0)
    })
    expect(screen.getByText("Creating new server...")).toBeInTheDocument()
    expect(reviewRequests).toBe(0)
    expect(screen.queryByText("Choose another target identity")).not.toBeInTheDocument()
  })

  it("keeps target identity fields stacked until wide layouts have room", async () => {
    renderWithProviders(
      <MigrationPrivateServerCreationWorkspace
        migrationId={migrationId}
        matrixServerName="matrix.example.test"
        assuranceMode="simplified"
        onChanged={async () => undefined}
      />,
    )

    const matrixInput = await screen.findByDisplayValue("matrix.example.test")
    const fieldGrid = matrixInput.closest("div.grid")
    expect(fieldGrid).not.toBeNull()
    expect(fieldGrid).toHaveClass("xl:grid-cols-3")
    expect(fieldGrid).not.toHaveClass("lg:grid-cols-3")
  })

})
