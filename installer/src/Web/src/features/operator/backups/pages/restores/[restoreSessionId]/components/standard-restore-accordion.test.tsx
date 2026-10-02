import { http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type {
  RestoreWorkspaceResponse,
  RestoreWorkspaceStandardStage,
} from "@/features/operator/backups/api/types/restore-workspace.types"

import { StandardRestoreAccordion } from "./standard-restore-accordion"

const restoreSessionId = "restore-catalog-1"

function createStage(
  code: string,
  state: string,
  overrides: Partial<RestoreWorkspaceStandardStage> = {},
): RestoreWorkspaceStandardStage {
  return {
    code,
    title: code,
    description: `${code} description`,
    state,
    required: code !== "private-test",
    unlocked: state !== "not-started",
    completedAtUtc: null,
    primaryAction: null,
    secondaryActions: [],
    summary: "",
    blockers: [],
    evidenceSummary: {
      itemCount: 0,
      latestOccurredAtUtc: null,
      latestStatus: null,
    },
    operationSummary: null,
    privateTestEvidence: null,
    ...overrides,
  }
}

function createWorkspace(options?: {
  privateTestState?: string
  privateTestActionEnabled?: boolean
  privateTestBlockers?: string[]
  privateTestEvidence?: RestoreWorkspaceStandardStage["privateTestEvidence"]
  handoverState?: string
}): RestoreWorkspaceResponse {
  const privateTestState = options?.privateTestState ?? "optional"
  const privateTestEvidence = options?.privateTestEvidence ?? null

  return {
    schemaVersion: 3,
    restoreSessionId,
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "ready",
      createdAtUtc: "2026-06-29T05:00:00Z",
      updatedAtUtc: "2026-06-29T05:00:00Z",
      terminalAtUtc: null,
      lastEventAtUtc: null,
      lastErrorCode: null,
      lastErrorSummary: null,
      warningCount: 0,
      errorCount: 0,
      currentOperationId: null,
    },
    source: {
      kind: "backup-catalog",
      stackSlug: "cool-stack",
      backupId: "20260629-023108Z",
      createdAtUtc: "2026-06-29T02:31:08Z",
      sizeBytes: 1024,
      matrixHost: "matrix-cool-stack.deltabox.dev",
      elementHost: "chat-cool-stack.deltabox.dev",
      validationStatus: "available",
      validationSummary: "Catalog payload is ready.",
      catalogEntryId: "bkp-test-001",
      sourceDisplayName: "Test backup",
      sourceOriginKind: "local-captured",
      sourceDeleted: false,
    },
    target: {
      stackSlug: null,
      matrixHost: null,
      elementHost: null,
      availability: "not-selected",
      detail: "No target is reserved.",
      claims: [],
    },
    overallStatus: {
      code: "ready",
      title: "Ready to continue",
      description: "The backup is ready.",
      severity: "information",
      nextAction: null,
    },
    standardStages: [
      createStage("backup-ready", "completed", {
        summary: "Backup ready.",
      }),
      createStage("private-test", privateTestState, {
        primaryAction: {
          code: "run-private-test",
          title: "Run private test",
          description: "Start an isolated restore test.",
          enabled: options?.privateTestActionEnabled ?? true,
          relatedStage: "private-test",
        },
        blockers: options?.privateTestBlockers ?? [],
        summary: privateTestEvidence
          ? "Private test completed. The isolated staging environment was explicitly destroyed; historical evidence remains available."
          : "Optional private test.",
        operationSummary: privateTestEvidence
          ? {
              operationId: "00000000-0000-0000-0000-000000000002",
              operation: "restore.private-test",
              status: "succeeded",
              currentStep: "private-staging-ready",
              requestedAtUtc: "2026-06-29T05:02:09Z",
              startedAtUtc: "2026-06-29T05:02:09Z",
              completedAtUtc: "2026-06-29T05:02:36Z",
            }
          : null,
        privateTestEvidence,
      }),
      createStage("choose-restored-server-details", "ready"),
      createStage("create-restored-chat-server", "not-started"),
      createStage("check-restored-server", "not-started"),
      createStage("complete-and-hand-over", options?.handoverState ?? "not-started"),
    ],
    advancedTools: [],
    verification: {
      status: "not-available",
      hasRun: false,
      allPassed: null,
      checkedAtUtc: null,
      checks: [],
      summary: "Not available.",
    },
    evidence: {
      categories: [],
      latestFailure: null,
      latestSuccess: null,
    },
    logs: {
      totalEvents: 0,
      warningCount: 0,
      errorCount: 0,
      latestEvent: null,
      latestWarningOrError: null,
      supportReportAvailable: false,
      supportBundleAvailable: false,
      warnings: [],
    },
    warnings: [],
  }
}

function renderAccordion(workspace: RestoreWorkspaceResponse) {
  const onWorkspaceChanged = vi.fn(async () => undefined)

  // The accordion can initially open the Standard Recreate step, which
  // requests a preflight. This test file is focused on the private-test
  // contract, so provide a harmless matching response rather than allowing
  // MSW to report an unrelated unhandled request.
  server.use(
    http.post(
      `/internal/host-agent/backups/restores/${restoreSessionId}/standard-recreate/preflight`,
      () =>
        HttpResponse.json({
          source: "control-plane",
          status: "ready",
          checkedAtUtc: "2026-06-29T05:00:00Z",
          restoreSessionId,
          canCreate: true,
          targets: {
            targetStackSlug: "cool-stack-restored",
            matrixHost: "matrix-cool-stack.deltabox.dev",
            elementHost: "chat-cool-stack.deltabox.dev",
          },
          checks: [],
          blockers: [],
          warnings: [],
          detail: "Preflight ready.",
          turn: {
            mode: "restore-disconnected",
            state: "not-connected",
            management: "none",
            platformTurnRequired: false,
            platformTurnReady: null,
            publicHost: null,
            turnUris: [],
            detail: "The backup records no TURN association.",
          },
        }),
    ),
  )

  renderWithProviders(
    <StandardRestoreAccordion
      workspace={workspace}
      onViewEvidence={vi.fn()}
      onViewLogs={vi.fn()}
      onWorkspaceChanged={onWorkspaceChanged}
    />,
  )

  return { onWorkspaceChanged }
}

async function openPrivateTest(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    screen.getByRole("button", { name: /run a private restore test/i }),
  )
}

async function openTargetSelection(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    screen.getByRole("button", { name: /choose and create restored server/i }),
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("StandardRestoreAccordion private test", () => {
  it("renders the standard restore journey in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()

    renderAccordion(createWorkspace())

    expect(screen.getByText("Sicherung bereit")).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: /Privaten Wiederherstellungstest durchführen/i }),
    ).toBeInTheDocument()
    expect(screen.getByText("Diese Sicherung ist einsatzbereit")).toBeInTheDocument()

    await user.click(
      screen.getByRole("button", {
        name: /Wiederhergestellten Server auswählen und erstellen/i,
      }),
    )

    expect(
      await screen.findByLabelText("Ziel-Stack-Slug"),
    ).toBeInTheDocument()
    expect(screen.getByLabelText("Element-Webhost (Client)")).toBeInTheDocument()
    expect(screen.getByText("Preflight-Zusammenfassung")).toBeInTheDocument()
    expect(screen.getByText("Sprach-/Video-TURN")).toBeInTheDocument()
    expect(
      screen.getByText(
        "Diese Sicherung wird ohne TURN-Zuordnung wiederhergestellt, auch wenn Plattform-TURN verfügbar ist.",
      ),
    ).toBeInTheDocument()
  })
  it("renders a known private-test problem in German and retains raw technical detail", async () => {
    const detail = "The current restore state does not allow another private test."
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/private-test`,
        () =>
          HttpResponse.json(
            {
              error: "restore_private_test_not_available",
              detail,
              message: {
                code: "restore.private-test.not-available",
                arguments: { restoreSessionId },
              },
            },
            { status: 400 },
          ),
      ),
    )

    const user = userEvent.setup()
    renderAccordion(createWorkspace())

    await user.click(
      screen.getByRole("button", {
        name: /Privaten Wiederherstellungstest durchführen/i,
      }),
    )
    await user.click(
      screen.getByRole("button", { name: "Privaten Test durchführen" }),
    )

    expect(
      await screen.findByText(
        "Der private Test ist für die Wiederherstellungssitzungs-ID restore-catalog-1 nicht verfügbar.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technische Details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
  })

  it("uses the canonical workspace action for a catalog source", async () => {
    let actionRequests = 0
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/private-test`,
        async ({ request }) => {
          actionRequests += 1
          expect(await request.text()).toBe("")
          return HttpResponse.json({
            source: "control-plane",
            status: "ready",
            restoreSessionId,
            operationId: "00000000-0000-0000-0000-000000000002",
            sourceKind: "backup-catalog",
            catalogEntryId: "catalog-1",
            stagingId: "staging-1",
            privateOnly: true,
            databaseImportSucceeded: true,
            synapseHealthPassed: true,
            requiresExplicitDestroy: true,
            detail: "Private restore test is ready.",
          })
        },
      ),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(createWorkspace())

    await openPrivateTest(user)

    await user.click(screen.getByRole("button", { name: "Run private test" }))

    await waitFor(() => expect(actionRequests).toBe(1))
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))
  })

  it("reconciles a lost private-test transport to authoritative running state instead of claiming rejection", async () => {
    const authoritativeWorkspace = createWorkspace({
      privateTestState: "running",
    })
    authoritativeWorkspace.attempt.status = "testing"
    authoritativeWorkspace.attempt.currentStage = "private-test"
    authoritativeWorkspace.attempt.updatedAtUtc = "2026-06-29T05:02:10Z"
    authoritativeWorkspace.attempt.currentOperationId =
      "00000000-0000-0000-0000-000000000099"
    authoritativeWorkspace.standardStages[1].operationSummary = {
      operationId: "00000000-0000-0000-0000-000000000099",
      operation: "restore.private-test",
      status: "running",
      currentStep: "private-staging",
      requestedAtUtc: "2026-06-29T05:02:09Z",
      startedAtUtc: "2026-06-29T05:02:09Z",
      completedAtUtc: null,
    }

    let workspaceReads = 0
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/private-test`,
        () => HttpResponse.error(),
      ),
      http.get(
        `/internal/host-agent/backups/restores/${restoreSessionId}/workspace`,
        () => {
          workspaceReads += 1
          return HttpResponse.json(authoritativeWorkspace)
        },
      ),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(createWorkspace())

    await openPrivateTest(user)
    await user.click(screen.getByRole("button", { name: "Run private test" }))

    await waitFor(() => expect(workspaceReads).toBeGreaterThan(0))
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))

    expect(
      screen.queryByText("Could not start the private test"),
    ).not.toBeInTheDocument()
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
  })

  it("honours the server-projected action availability", async () => {
    const user = userEvent.setup()
    renderAccordion(
      createWorkspace({
        privateTestActionEnabled: false,
        privateTestBlockers: ["The catalog payload is no longer present."],
      }),
    )

    await openPrivateTest(user)

    expect(screen.getByRole("button", { name: "Run private test" })).toBeDisabled()
    expect(
      screen.getAllByText("The catalog payload is no longer present.").length,
    ).toBeGreaterThan(0)
  })

  it("renders durable private-test evidence after the staging runtime was retired", async () => {
    const user = userEvent.setup()
    renderAccordion(
      createWorkspace({
        privateTestState: "completed",
        privateTestEvidence: {
          sourceKind: "backup-catalog",
          catalogEntryId: "catalog-1",
          stagingId: "staging-1",
          matrixServerName: "matrix-cool-stack.deltabox.dev",
          status: "ready",
          privateOnly: true,
          dockerNetworkInternal: true,
          databaseImportSucceeded: true,
          synapseHealthPassed: true,
          requiresExplicitDestroy: true,
          completedAtUtc: "2026-06-29T05:02:36Z",
          stagingRuntimeStatus: "destroyed",
          stagingRuntimeDestroyed: true,
          destroyAvailable: false,
          destroyedAtUtc: "2026-06-29T05:04:47Z",
        },
      }),
    )

    await openPrivateTest(user)

    expect(
      screen.getByText("Private restore test retired"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("matrix-cool-stack.deltabox.dev"),
    ).toBeInTheDocument()
    expect(screen.getByText("Retired")).toBeInTheDocument()
  })

  it("reconciles a lost retirement response to authoritative destroyed state", async () => {
    const retainedWorkspace = createWorkspace({
      privateTestState: "completed",
      privateTestEvidence: {
        sourceKind: "backup-catalog",
        catalogEntryId: "catalog-1",
        stagingId: "staging-1",
        matrixServerName: "matrix-cool-stack.deltabox.dev",
        status: "ready",
        privateOnly: true,
        dockerNetworkInternal: true,
        databaseImportSucceeded: true,
        synapseHealthPassed: true,
        requiresExplicitDestroy: true,
        completedAtUtc: "2026-06-29T05:02:36Z",
        stagingRuntimeStatus: "retained",
        stagingRuntimeDestroyed: false,
        destroyAvailable: true,
        destroyedAtUtc: null,
      },
    })
    const destroyedWorkspace = createWorkspace({
      privateTestState: "completed",
      privateTestEvidence: {
        sourceKind: "backup-catalog",
        catalogEntryId: "catalog-1",
        stagingId: "staging-1",
        matrixServerName: "matrix-cool-stack.deltabox.dev",
        status: "ready",
        privateOnly: true,
        dockerNetworkInternal: true,
        databaseImportSucceeded: true,
        synapseHealthPassed: true,
        requiresExplicitDestroy: true,
        completedAtUtc: "2026-06-29T05:02:36Z",
        stagingRuntimeStatus: "destroyed",
        stagingRuntimeDestroyed: true,
        destroyAvailable: false,
        destroyedAtUtc: "2026-06-29T05:04:47Z",
      },
    })

    let workspaceReads = 0
    server.use(
      http.post(
        `/internal/host-agent/backups/verification/private-runtime/private-staging/staging-1/destroy`,
        () => HttpResponse.error(),
      ),
      http.get(
        `/internal/host-agent/backups/restores/${restoreSessionId}/workspace`,
        () => {
          workspaceReads += 1
          return HttpResponse.json(destroyedWorkspace)
        },
      ),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(retainedWorkspace)

    await openPrivateTest(user)
    await user.click(
      screen.getByRole("button", { name: "Retire private test" }),
    )

    const confirmButtons = screen.getAllByRole("button", {
      name: "Retire private test",
    })
    await user.click(confirmButtons.at(-1)!)

    await waitFor(() => expect(workspaceReads).toBeGreaterThan(0))
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))

    expect(
      screen.queryByText("Could not retire private test"),
    ).not.toBeInTheDocument()
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
  })

  it("renders an authoritative partial-retirement state as retryable attention", async () => {
    const user = userEvent.setup()
    renderAccordion(
      createWorkspace({
        privateTestState: "completed",
        privateTestEvidence: {
          sourceKind: "backup-catalog",
          catalogEntryId: "catalog-1",
          stagingId: "staging-1",
          matrixServerName: "matrix-cool-stack.deltabox.dev",
          status: "ready",
          privateOnly: true,
          dockerNetworkInternal: true,
          databaseImportSucceeded: true,
          synapseHealthPassed: true,
          requiresExplicitDestroy: true,
          completedAtUtc: "2026-06-29T05:02:36Z",
          stagingRuntimeStatus: "retirement-needs-attention",
          stagingRuntimeDestroyed: false,
          destroyAvailable: true,
          destroyedAtUtc: null,
        },
      }),
    )

    await openPrivateTest(user)

    expect(
      screen.getAllByText("Private-test retirement needs attention").length,
    ).toBeGreaterThan(0)
    expect(
      screen.getByText("Retirement needs attention"),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Retry retirement" }),
    ).toBeInTheDocument()
  })

  it("keeps a cancelled restore read-only and withholds progression actions", async () => {
    const user = userEvent.setup()
    const workspace = createWorkspace()

    workspace.attempt.status = "cancelled"
    workspace.attempt.currentStage = "cancelled"
    workspace.attempt.terminalAtUtc = "2026-06-29T05:03:00Z"
    workspace.overallStatus = {
      code: "cancelled",
      title: "Restore cancelled",
      description: "No further restore actions will run for this attempt.",
      severity: "information",
      nextAction: null,
    }
    workspace.standardStages = workspace.standardStages.map((stage) => ({
      ...stage,
      state: "cancelled",
      primaryAction: stage.primaryAction
        ? { ...stage.primaryAction, enabled: false }
        : null,
    }))

    renderAccordion(workspace)

    expect(
      screen.getByRole("button", { name: "View validation details" }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Continue: private test" }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Skip test: choose server details" }),
    ).not.toBeInTheDocument()

    await openPrivateTest(user)
    expect(
      screen.queryByRole("button", { name: "Run private test" }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Skip for now" }),
    ).not.toBeInTheDocument()

    await openTargetSelection(user)
    expect(screen.queryByLabelText("Target stack slug")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Element web host (client)")).not.toBeInTheDocument()
  })

  it("renders a known handover problem in English and retains raw technical detail", async () => {
    const detail = "A completed public verification is required before handover."
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/complete-handover`,
        ({ request }) => {
          const url = new URL(request.url)
          expect(url.searchParams.get("acknowledgeCompletion")).toBe("true")

          return HttpResponse.json(
            {
              error: "restore_handover_not_available",
              detail,
              message: {
                code: "restore.handover.not-available",
                arguments: { restoreSessionId },
              },
            },
            { status: 400 },
          )
        },
      ),
    )

    const user = userEvent.setup()
    renderAccordion(createWorkspace({ handoverState: "ready" }))

    await user.click(screen.getByRole("button", { name: /Restore complete/i }))
    await user.click(
      screen.getByRole("button", { name: "Complete restore and hand over" }),
    )

    expect(
      await screen.findByText(
        "Handover for Restore Session ID restore-catalog-1 cannot be completed at its current stage.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
    expect(screen.getByText(detail)).toBeInTheDocument()
  })

  it("uses the canonical workspace create action for a catalog source", async () => {
    let actionRequests = 0

    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/standard-recreate`,
        ({ request }) => {
          actionRequests += 1

          const url = new URL(request.url)
          expect(url.searchParams.get("targetStackSlug")).toBe(
            "cool-stack-restored-catalog",
          )
          expect(url.searchParams.get("elementHost")).toBe(
            "chat-cool-stack-restored-catalog.deltabox.dev",
          )
          expect(url.searchParams.get("operatorName")).toBe("Nigel")
          expect(url.searchParams.get("matrixHost")).toBeNull()
          expect(url.searchParams.get("executeProductionRecreate")).toBe("true")
          expect(url.searchParams.get("acknowledgeCreatesRealStack")).toBe("true")
          expect(url.searchParams.get("acknowledgeMutatesProductionPostgres")).toBe(
            "true",
          )
          expect(url.searchParams.get("acknowledgeMutatesNpmRoutes")).toBe("true")
          expect(
            url.searchParams.get("acknowledgeNoAutomaticRollback"),
          ).toBe("true")

          return HttpResponse.json({
            source: "control-plane",
            status: "production_recreate_verified",
            recreateId: "recreate-catalog-1",
            sourceKind: "backup-catalog",
            catalogEntryId: "catalog-1",
            targetStackSlug: "cool-stack-restored-catalog",
            matrixHost: "matrix-cool-stack.deltabox.dev",
            elementHost: "chat-cool-stack-restored-catalog.deltabox.dev",
            detail: "Production Recreate completed.",
            errors: [],
          })
        },
      ),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(createWorkspace())

    await openTargetSelection(user)

    await user.clear(screen.getByLabelText("Target stack slug"))
    await user.type(
      screen.getByLabelText("Target stack slug"),
      "cool-stack-restored-catalog",
    )
    await user.clear(screen.getByLabelText("Element web host (client)"))
    await user.type(
      screen.getByLabelText("Element web host (client)"),
      "chat-cool-stack-restored-catalog.deltabox.dev",
    )
    await user.type(screen.getByLabelText(/operator name/i), "Nigel")

    const createButton = screen.getByRole("button", {
      name: "Continue to create restored chat server",
    })

    await waitFor(() => expect(createButton).toBeEnabled())
    expect(
      screen.queryByText(
        "Creation is unavailable because this workspace does not expose a validation reference.",
      ),
    ).not.toBeInTheDocument()

    await user.click(createButton)

    await waitFor(() => expect(actionRequests).toBe(1))
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))
  })


  it("shows authoritative Standard Recreate progress with elapsed and last-activity context", async () => {
    const workspace = createWorkspace()
    const operationId = "00000000-0000-0000-0000-000000000099"
    workspace.attempt.status = "recreating"
    workspace.attempt.currentStage = "create-restored-chat-server"
    workspace.attempt.currentOperationId = operationId
    workspace.standardStages[2] = createStage(
      "choose-restored-server-details",
      "completed",
    )
    workspace.standardStages[3] = createStage(
      "create-restored-chat-server",
      "running",
      {
        summary: "The restored chat server is being created.",
        operationSummary: {
          operationId,
          operation: "restore.standard-recreate",
          status: "running",
          currentStep: "restore-database",
          requestedAtUtc: new Date(Date.now() - 90_000).toISOString(),
          startedAtUtc: new Date(Date.now() - 85_000).toISOString(),
          completedAtUtc: null,
          lastActivityAtUtc: new Date(Date.now() - 3_000).toISOString(),
          attemptNumber: 1,
        },
      },
    )

    renderAccordion(workspace)

    expect(
      await screen.findByTestId("restore-standard-recreate-progress"),
    ).toBeInTheDocument()
    expect(screen.getByText("Current operation")).toBeInTheDocument()
    expect(
      screen.getByRole("heading", { name: "Restore Synapse database" }),
    ).toBeInTheDocument()
    expect(screen.getByText("Elapsed")).toBeInTheDocument()
    expect(screen.getByText("Last activity")).toBeInTheDocument()
    expect(screen.getByText("Operation progress")).toBeInTheDocument()
    expect(screen.getByText("Reserve target resources")).toBeInTheDocument()
    expect(
      screen.getByText("Verify restored server readiness"),
    ).toBeInTheDocument()
    expect(screen.getByText("MEM is still working")).toBeInTheDocument()
    expect(
      screen.queryByText(/\{(?:count|minutes|seconds)\}/),
    ).not.toBeInTheDocument()
  })


  it("renders numeric seconds during the first minute of Standard Recreate progress", async () => {
    const workspace = createWorkspace()
    const operationId = "00000000-0000-0000-0000-000000000100"
    workspace.attempt.status = "recreating"
    workspace.attempt.currentStage = "create-restored-chat-server"
    workspace.attempt.currentOperationId = operationId
    workspace.standardStages[2] = createStage(
      "choose-restored-server-details",
      "completed",
    )
    workspace.standardStages[3] = createStage(
      "create-restored-chat-server",
      "running",
      {
        summary: "The restored chat server is being created.",
        operationSummary: {
          operationId,
          operation: "restore.standard-recreate",
          status: "running",
          currentStep: "start-matrix",
          requestedAtUtc: new Date(Date.now() - 14_000).toISOString(),
          startedAtUtc: new Date(Date.now() - 12_000).toISOString(),
          completedAtUtc: null,
          lastActivityAtUtc: new Date(Date.now() - 7_000).toISOString(),
          attemptNumber: 1,
        },
      },
    )

    renderAccordion(workspace)

    const progress = await screen.findByTestId("restore-standard-recreate-progress")
    expect(screen.getByText(/^\d+s$/)).toBeInTheDocument()
    expect(screen.getByText(/^\d+s ago$/)).toBeInTheDocument()
    expect(progress).not.toHaveTextContent(/\{\{?\s*(?:count|minutes|seconds)\s*\}\}?/)
  })


  it("treats a lost create transport as uncertain and refreshes authoritative workspace state", async () => {
    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/standard-recreate`,
        () => HttpResponse.error(),
      ),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(createWorkspace())

    await openTargetSelection(user)

    const createButton = screen.getByRole("button", {
      name: "Continue to create restored chat server",
    })
    await waitFor(() => expect(createButton).toBeEnabled())
    await user.click(createButton)

    expect(
      await screen.findByText("Connection ended while starting creation"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        /The operation may already be running on the server/i,
      ),
    ).toBeInTheDocument()
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))
  })

  it("requires recent identity verification then retries only the confirmed standard recreate", async () => {
    const actionSearches: string[] = []
    let stepUpRequests = 0

    server.use(
      http.post(
        `/internal/host-agent/backups/restores/${restoreSessionId}/standard-recreate`,
        ({ request }) => {
          const url = new URL(request.url)
          actionSearches.push(url.search)

          if (actionSearches.length === 1) {
            return HttpResponse.json(
              {
                error: "step_up_required",
                detail: "Fresh identity verification is required before this action.",
              },
              { status: 403 },
            )
          }

          return HttpResponse.json({
            source: "control-plane",
            status: "production_recreate_verified",
            recreateId: "recreate-catalog-step-up-1",
            sourceKind: "backup-catalog",
            catalogEntryId: "catalog-1",
            targetStackSlug: "cool-stack-restored-step-up",
            matrixHost: "matrix-cool-stack.deltabox.dev",
            elementHost: "chat-cool-stack-restored-step-up.deltabox.dev",
            detail: "Production Recreate completed.",
            errors: [],
          })
        },
      ),
      http.post("/api/auth/step-up", async ({ request }) => {
        stepUpRequests += 1
        expect(await request.json()).toEqual({
          password: "Secure!Foundation123",
          code: "123456",
        })

        return HttpResponse.json({
          status: "step_up_authenticated",
          expiresAtUtc: "2026-07-05T12:00:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    const { onWorkspaceChanged } = renderAccordion(createWorkspace())

    await openTargetSelection(user)

    await user.clear(screen.getByLabelText("Target stack slug"))
    await user.type(
      screen.getByLabelText("Target stack slug"),
      "cool-stack-restored-step-up",
    )
    await user.clear(screen.getByLabelText("Element web host (client)"))
    await user.type(
      screen.getByLabelText("Element web host (client)"),
      "chat-cool-stack-restored-step-up.deltabox.dev",
    )
    await user.type(screen.getByLabelText(/operator name/i), "Nigel")

    const createButton = screen.getByRole("button", {
      name: "Continue to create restored chat server",
    })

    await waitFor(() => expect(createButton).toBeEnabled())
    await user.click(createButton)

    expect(
      await screen.findByRole("dialog", { name: "Verify your identity" }),
    ).toBeInTheDocument()
    expect(createButton).toBeDisabled()

    await user.type(
      screen.getByLabelText("Current password"),
      "Secure!Foundation123",
    )
    await user.type(
      screen.getByLabelText("Current authenticator code"),
      "123456",
    )
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => expect(stepUpRequests).toBe(1))
    await waitFor(() => expect(actionSearches).toHaveLength(2))
    expect(actionSearches[1]).toBe(actionSearches[0])
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledTimes(1))
  })

})
