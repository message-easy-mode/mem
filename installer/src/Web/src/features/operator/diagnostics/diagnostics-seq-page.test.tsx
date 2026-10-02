import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"
import type {
  DiagnosticsSeqBootstrapOperationResponse,
  DiagnosticsSeqBootstrapOverviewResponse,
  DiagnosticsSeqBootstrapReviewResponse,
  DiagnosticsSeqOverviewResponse,
  DiagnosticsSeqSetupReviewResponse,
} from "./api/diagnostics.types"
import { DiagnosticsSeqPage } from "./diagnostics-seq-page"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onOpenChange,
    onVerified,
  }: {
    open: boolean
    onOpenChange: (open: boolean) => void
    onVerified?: () => void
  }) => open ? (
    <div>
      <button type="button" onClick={() => { onOpenChange(false); onVerified?.() }}>Complete step-up</button>
      <button type="button" onClick={() => onOpenChange(false)}>Cancel step-up</button>
    </div>
  ) : null,
}))

const ownerSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const operatorSession = {
  ...ownerSession,
  displayName: "operator",
  roles: ["operator"],
}

const auditorSession = {
  ...ownerSession,
  displayName: "auditor",
  roles: ["auditor"],
}

const optionalOverview: DiagnosticsSeqOverviewResponse = {
  schemaVersion: 5,
  observedAtUtc: "2026-08-03T04:00:00Z",
  configured: false,
  delivery: {
    enabled: false,
    desiredEnabled: false,
    configurationState: "disabled",
    secretState: "not-required",
    requiresApiRestartToChange: true,
    restartRequired: false,
    preferenceUpdatedAtUtc: null,
    activationState: "disabled",
    activationVerifiedAtUtc: null,
    lastActivationVerificationId: null,
    restart: {
      kind: "container",
      guidanceCode: "restart_control_plane_container",
      command: "sudo docker restart mem-control-plane",
      commandAvailable: true,
    },
  },
  runtime: {
    managementEnabled: false,
    present: false,
    managed: false,
    state: "not-configured",
    running: false,
    usesApprovedRuntime: false,
    expectedVersion: "2026.1.17044",
    dataRetentionState: "preserved-on-remove",
    publishesPublicIngress: false,
    warningCode: null,
  },
  health: {
    status: "optional-disabled",
    reachable: false,
    lastCheckedAtUtc: null,
    lastSucceededAtUtc: null,
    warningCode: null,
  },
  ui: {
    configured: false,
    available: false,
    url: null,
  },
  capabilities: {
    canReviewSetup: false,
    canOpenUi: false,
    canDeploy: false,
    canStart: false,
    canStop: false,
    canRestart: false,
    canRemove: false,
    canEnableDelivery: false,
    canDisableDelivery: false,
    canCheckHealth: false,
    canVerifyDelivery: false,
    requiresRecentStepUp: true,
  },
  warnings: [],
}

const bootstrapOverview: DiagnosticsSeqBootstrapOverviewResponse = {
  schemaVersion: 1,
  observedAtUtc: "2026-08-05T00:00:00Z",
  state: "not-installed",
  setupAvailable: true,
  approvedVersion: "2026.1.17044",
  imageState: "preparable",
  storageState: "ready-to-create",
  runtimeOwnershipState: "absent",
  eulaAccepted: false,
  administratorSecretState: "absent",
  ingestionCredentialState: "absent",
  uiAuthorityState: "not-configured",
  canStartSetup: true,
  warnings: [],
}

const bootstrapReview: DiagnosticsSeqBootstrapReviewResponse = {
  schemaVersion: 1,
  reviewId: "seq_bootstrap_review_123",
  observedAtUtc: "2026-08-05T00:00:00Z",
  expiresAtUtc: "2026-08-05T00:10:00Z",
  ready: true,
  image: {
    approvedReference: "datalust/seq:2026.1.17044",
    expectedVersion: "2026.1.17044",
    local: false,
    immutableIdentityAvailable: false,
    willPullDuringSetup: true,
    warningCode: null,
  },
  storage: {
    serverOwned: true,
    state: "ready-to-create",
    displayName: "MEM Seq data directory",
    warningCode: null,
  },
  runtime: {
    ownershipState: "absent",
    selectedHostPort: 15341,
    publishesPublicIngress: false,
  },
  security: {
    eulaAcceptedInReview: true,
    administratorPasswordRequired: true,
    authenticatedIngestionRequired: true,
    currentAdministratorPasswordRequired: false,
  },
  privateUiAuthorityConfigured: false,
  actionCodes: [
    "prepare_approved_image",
    "prepare_server_owned_storage",
    "hash_administrator_password_securely",
    "create_mem_managed_container",
    "attach_mem_gateway_network",
    "do_not_create_public_ingress",
    "preserve_seq_data_directory",
    "verify_docker_and_seq_health",
    "provision_mem_ingestion_credential",
    "verify_mem_ingestion",
    "prepare_event_delivery_after_restart",
  ],
  warnings: [],
  enableEventDelivery: true,
}

const bootstrapSucceeded: DiagnosticsSeqBootstrapOperationResponse = {
  schemaVersion: 1,
  operationId: "11111111-1111-1111-1111-111111111111",
  status: "succeeded",
  currentStep: "completed",
  requestedAtUtc: "2026-08-05T00:00:00Z",
  startedAtUtc: "2026-08-05T00:00:01Z",
  completedAtUtc: "2026-08-05T00:00:05Z",
  checks: [
    "approved_image",
    "storage",
    "password_hash",
    "administrator_secret",
    "bootstrap_state",
    "docker_runtime",
    "seq_health",
    "ingestion_connection",
    "event_delivery",
  ].map((code) => ({ code, status: "passed", warningCode: null })),
  warningCode: null,
  warnings: [],
}

const ownerOverview: DiagnosticsSeqOverviewResponse = {
  ...optionalOverview,
  configured: true,
  delivery: {
    enabled: true,
    desiredEnabled: true,
    configurationState: "configured",
    secretState: "available",
    requiresApiRestartToChange: true,
    restartRequired: false,
    preferenceUpdatedAtUtc: null,
    activationState: "verified",
    activationVerifiedAtUtc: "2026-08-03T03:59:30Z",
    lastActivationVerificationId: "seq-active-existing",
    restart: {
      kind: "container",
      guidanceCode: "restart_control_plane_container",
      command: "sudo docker restart mem-control-plane",
      commandAvailable: true,
    },
  },
  runtime: {
    ...optionalOverview.runtime,
    managementEnabled: true,
    present: true,
    managed: true,
    state: "running",
    running: true,
    usesApprovedRuntime: true,
  },
  health: {
    status: "ready",
    reachable: true,
    lastCheckedAtUtc: "2026-08-03T03:59:00Z",
    lastSucceededAtUtc: "2026-08-03T03:59:00Z",
    warningCode: null,
  },
  ui: {
    configured: true,
    available: true,
    url: "https://seq.internal.example/",
  },
  capabilities: {
    canReviewSetup: true,
    canOpenUi: true,
    canDeploy: false,
    canStart: false,
    canStop: true,
    canRestart: true,
    canRemove: false,
    canEnableDelivery: false,
    canDisableDelivery: true,
    canCheckHealth: true,
    canVerifyDelivery: false,
    canConnect: false,
  },
  connection: {
    credentialState: "available",
    verificationState: "verified",
    verifiedAtUtc: "2026-08-03T03:58:00Z",
    apiKeyId: "api-key-existing",
    lastVerificationId: "seq-connect-existing",
    lastEventId: "event-existing",
  },
}

const foreignControlPlaneOverview: DiagnosticsSeqOverviewResponse = {
  ...ownerOverview,
  runtime: {
    ...ownerOverview.runtime,
    managed: false,
    state: "control-plane-mismatch",
    usesApprovedRuntime: false,
    warningCode: "seq_control_plane_ownership_mismatch",
  },
  capabilities: {
    ...ownerOverview.capabilities,
    canDeploy: false,
    canStart: false,
    canStop: false,
    canRestart: false,
    canRemove: false,
    canCheckHealth: false,
  },
  warnings: ["seq_control_plane_ownership_mismatch"],
}

const foreignControlPlaneBootstrap: DiagnosticsSeqBootstrapOverviewResponse = {
  ...bootstrapOverview,
  state: "blocked",
  setupAvailable: false,
  runtimeOwnershipState: "control-plane-mismatch",
  canStartSetup: false,
  warnings: ["seq_control_plane_ownership_mismatch"],
}

const readyReview: DiagnosticsSeqSetupReviewResponse = {
  schemaVersion: 1,
  reviewId: "seq_review_123",
  observedAtUtc: "2026-08-03T04:00:00Z",
  expiresAtUtc: "2026-08-03T04:10:00Z",
  readyForDeployment: true,
  readyForDelivery: true,
  image: {
    approvedReference: "datalust/seq:2026.1.17044",
    expectedVersion: "2026.1.17044",
    local: true,
    immutableIdentityAvailable: true,
    warningCode: null,
  },
  storage: {
    serverOwned: true,
    state: "ready-to-create",
    displayName: "MEM Seq data directory",
    warningCode: null,
  },
  secrets: {
    administratorPasswordHashAvailable: true,
    ingestionApiKeyRequired: true,
    ingestionApiKeyAvailable: true,
  },
  uiAuthority: {
    configured: true,
  },
  eulaAccepted: true,
  publishesPublicIngress: false,
  runtimeOwnershipState: "managed",
  actionCodes: [
    "create_mem_managed_container",
    "attach_mem_gateway_network",
    "preserve_seq_data_directory",
    "do_not_create_public_ingress",
    "leave_event_delivery_unchanged",
  ],
  warnings: [],
}

function operationResult(
  operation: string,
  overview: DiagnosticsSeqOverviewResponse,
) {
  return {
    schemaVersion: 1,
    operationId: "11111111-1111-1111-1111-111111111111",
    operation,
    status: "succeeded",
    startedAtUtc: "2026-08-04T01:00:00Z",
    completedAtUtc: "2026-08-04T01:00:01Z",
    dataRetained: true,
    overview,
    warnings: [],
  }
}

function renderPage(session = ownerSession) {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/diagnostics/seq"]}>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <DiagnosticsSeqPage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("DiagnosticsSeqPage", () => {
  beforeEach(() => {
    server.use(
      http.get("/api/operator/diagnostics/seq/bootstrap", () =>
        HttpResponse.json(bootstrapOverview)),
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json({
        schemaVersion: 1,
        observedAtUtc: "2026-08-04T04:00:00Z",
        available: true,
        managed: true,
        runtimeState: "ready",
        ownershipState: "managed",
        version: "2.39.5",
        approvedVersion: "2.39.5",
        environmentConfigured: true,
        exactResourceLinksSupported: true,
        links: {
          home: "https://portainer.internal.example/",
          environment: "https://portainer.internal.example/#!/endpoints/1/docker/dashboard",
          containers: "https://portainer.internal.example/#!/endpoints/1/docker/containers",
        },
        capabilities: {
          canOpenHome: true,
          canOpenEnvironment: true,
          canOpenContainers: true,
          canOpenExactResource: true,
        },
        warnings: [],
      })),
    )
  })
  it("distinguishes a Seq runtime owned by another Control Plane from container identity drift", async () => {
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(foreignControlPlaneOverview)),
      http.get("/api/operator/diagnostics/seq/bootstrap", () => HttpResponse.json(foreignControlPlaneBootstrap)),
    )

    renderPage()

    const seqOverview = await screen.findByRole("region", { name: "Seq overview" })
    expect(within(seqOverview).getAllByText("Different Control Plane")).toHaveLength(2)
    expect(within(seqOverview).queryByText("Unknown")).not.toBeInTheDocument()
    expect(screen.getAllByText(/different MEM Control Plane instance/i).length).toBeGreaterThan(0)
    expect(screen.queryByRole("button", { name: "Stop Seq" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Restart Seq" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Remove runtime" })).not.toBeInTheDocument()
  })

  it.skip("guides a Platform Owner through reviewed setup, step-up, durable progress, and verified success", async () => {
    const user = userEvent.setup()
    const reviewBodies: unknown[] = []
    const executeBodies: unknown[] = []
    let executeCalls = 0
    let operationCalls = 0
    let bootstrapCalls = 0
    let bootstrapCompleted = false
    const password = "Correct-Horse-Battery-42"

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(bootstrapCompleted
        ? {
            ...ownerOverview,
            delivery: {
              ...ownerOverview.delivery,
              enabled: false,
              desiredEnabled: true,
              restartRequired: true,
              activationState: "restart-required",
            },
          }
        : optionalOverview)),
      http.get("/api/operator/diagnostics/seq/bootstrap", () => {
        bootstrapCalls += 1
        return HttpResponse.json(bootstrapCompleted
          ? {
              ...bootstrapOverview,
              state: "running",
              setupAvailable: false,
              runtimeOwnershipState: "managed",
              canStartSetup: false,
            }
          : bootstrapOverview)
      }),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", async ({ request }) => {
        reviewBodies.push(await request.json())
        return HttpResponse.json(bootstrapReview)
      }),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", async ({ request }) => {
        executeCalls += 1
        executeBodies.push(await request.json())
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: bootstrapSucceeded.operationId,
          status: "queued",
          acceptedAtUtc: "2026-08-05T00:00:00Z",
        }, { status: 202 })
      }),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        () => {
          operationCalls += 1
          if (operationCalls === 1) {
            return HttpResponse.json({
              ...bootstrapSucceeded,
              status: "running",
              currentStep: "verifying-health",
              completedAtUtc: null,
              checks: bootstrapSucceeded.checks.map((check) => ({
                ...check,
                status: check.code === "seq_health" ? "running" : "passed",
              })),
            })
          }

          bootstrapCompleted = true
          return HttpResponse.json(bootstrapSucceeded)
        },
      ),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", {
      name: /accept the Seq End User License Agreement/i,
    }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), password)
    await user.type(screen.getByLabelText("Confirm administrator password"), password)
    expect(screen.getByRole("checkbox", { name: /Send MEM structured events to Seq after the next API restart/i })).toBeChecked()
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))

    expect(await screen.findByRole("heading", { name: "Review the exact deployment" })).toBeInTheDocument()
    expect(reviewBodies).toEqual([{ acceptEula: true, privateUiUrl: null, enableEventDelivery: true }])
    expect(JSON.stringify(reviewBodies)).not.toContain(password)
    expect(screen.getAllByText("2026.1.17044").length).toBeGreaterThan(0)
    expect(screen.getByText("Pull the approved exact image during setup")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Set up and connect Seq" }))
    expect(executeCalls).toBe(0)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByRole("heading", { name: "Seq is ready for MEM" }, { timeout: 4_000 })).toBeInTheDocument()
    expect(executeCalls).toBe(1)
    expect(executeBodies).toEqual([
      {
        reviewId: "seq_bootstrap_review_123",
        administratorPassword: password,
        administratorPasswordConfirmation: password,
        connectionAdministratorPassword: "",
      },
    ])
    expect(Object.keys(executeBodies[0] as Record<string, unknown>).sort()).toEqual([
      "administratorPassword",
      "administratorPasswordConfirmation",
      "connectionAdministratorPassword",
      "reviewId",
    ])
    expect(screen.queryByDisplayValue(password)).not.toBeInTheDocument()
    expect(screen.getByText(bootstrapSucceeded.operationId)).toBeInTheDocument()
    await waitFor(() => expect(bootstrapCalls).toBeGreaterThan(1))
    expect(screen.queryByText("Guided setup is currently blocked")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Finish" }))
    const setupCompleteHeading = await screen.findByRole("heading", { name: "Seq setup complete" })
    const setupCompleteSection = setupCompleteHeading.closest("section")
    expect(setupCompleteSection).not.toBeNull()
    expect(within(setupCompleteSection as HTMLElement).getByText("MEM is connected; API restart required")).toBeInTheDocument()
    expect(within(setupCompleteSection as HTMLElement).getByText(
      "Restart the server-identified MEM Control Plane container, then return here. MEM will verify the effective state from the new API process.",
    )).toBeInTheDocument()
    expect(within(setupCompleteSection as HTMLElement).getByText("sudo docker restart mem-control-plane")).toBeInTheDocument()
    expect(within(setupCompleteSection as HTMLElement).getByRole("button", { name: "Copy restart command" })).toBeInTheDocument()
  })

  it("lets the owner connect MEM during setup while explicitly leaving event delivery disabled", async () => {
    const user = userEvent.setup()
    let reviewBody: unknown = null
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", async ({ request }) => {
        reviewBody = await request.json()
        return HttpResponse.json({
          ...bootstrapReview,
          enableEventDelivery: false,
          actionCodes: bootstrapReview.actionCodes
            .filter((code) => code !== "prepare_event_delivery_after_restart")
            .concat("leave_event_delivery_disabled"),
        })
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), "Correct-Horse-Battery-42")
    await user.type(screen.getByLabelText("Confirm administrator password"), "Correct-Horse-Battery-42")
    await user.click(screen.getByRole("checkbox", { name: /Send MEM structured events to Seq after the next API restart/i }))
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))

    expect(reviewBody).toEqual({ acceptEula: true, privateUiUrl: null, enableEventDelivery: false })
    expect(await screen.findByText("Leave MEM event delivery disabled after setup.")).toBeInTheDocument()
  })

  it("preserves an existing administrator hash while using the current password only for MEM connection", async () => {
    const user = userEvent.setup()
    let executeBody: unknown = null
    const existingAuthorityReview = {
      ...bootstrapReview,
      security: {
        ...bootstrapReview.security,
        administratorPasswordRequired: false,
        currentAdministratorPasswordRequired: true,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.get("/api/operator/diagnostics/seq/bootstrap", () => HttpResponse.json({
        ...bootstrapOverview,
        administratorSecretState: "available",
      })),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () =>
        HttpResponse.json(existingAuthorityReview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", async ({ request }) => {
        executeBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: bootstrapSucceeded.operationId,
          status: "queued",
          acceptedAtUtc: "2026-08-05T00:00:00Z",
        }, { status: 202 })
      }),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        () => HttpResponse.json(bootstrapSucceeded),
      ),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))

    expect(screen.queryByLabelText("Initial Seq administrator password")).not.toBeInTheDocument()
    expect(screen.getByText("Existing administrator authority will be preserved")).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current Seq administrator password"), "Current-Seq-Administrator-42")
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    expect(await screen.findByText("Reuse existing configured authority")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Set up and connect Seq" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByRole("heading", { name: "Seq is ready for MEM" })).toBeInTheDocument()
    expect(executeBody).toEqual({
      reviewId: "seq_bootstrap_review_123",
      administratorPassword: "",
      administratorPasswordConfirmation: "",
      connectionAdministratorPassword: "Current-Seq-Administrator-42",
    })
  })

  it("executes reviewed Seq setup without the step-up dialog when security policy disables it", async () => {
    const user = userEvent.setup()
    let executeBody: unknown = null
    const policyDisabledOverview: DiagnosticsSeqOverviewResponse = {
      ...optionalOverview,
      capabilities: {
        ...optionalOverview.capabilities,
        requiresRecentStepUp: false,
      },
    }
    const existingAuthorityReview = {
      ...bootstrapReview,
      security: {
        ...bootstrapReview.security,
        administratorPasswordRequired: false,
        currentAdministratorPasswordRequired: true,
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(policyDisabledOverview)),
      http.get("/api/operator/diagnostics/seq/bootstrap", () => HttpResponse.json({
        ...bootstrapOverview,
        administratorSecretState: "available",
      })),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () =>
        HttpResponse.json(existingAuthorityReview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", async ({ request }) => {
        executeBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: bootstrapSucceeded.operationId,
          status: "queued",
          acceptedAtUtc: "2026-08-05T00:00:00Z",
        }, { status: 202 })
      }),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        () => HttpResponse.json(bootstrapSucceeded),
      ),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", {
      name: /accept the Seq End User License Agreement/i,
    }))
    await user.type(screen.getByLabelText("Current Seq administrator password"), "Current-Seq-Administrator-42")
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    await user.click(await screen.findByRole("button", { name: "Set up and connect Seq" }))

    expect(screen.queryByRole("button", { name: "Complete step-up" })).not.toBeInTheDocument()
    expect(await screen.findByRole("heading", { name: "Seq is ready for MEM" })).toBeInTheDocument()
    expect(executeBody).toEqual({
      reviewId: "seq_bootstrap_review_123",
      administratorPassword: "",
      administratorPasswordConfirmation: "",
      connectionAdministratorPassword: "Current-Seq-Administrator-42",
    })
  })

  it("clears the one-time administrator password when step-up is cancelled", async () => {
    const user = userEvent.setup()
    const password = "Correct-Horse-Battery-42"
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () => HttpResponse.json(bootstrapReview)),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), password)
    await user.type(screen.getByLabelText("Confirm administrator password"), password)
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    await user.click(await screen.findByRole("button", { name: "Set up and connect Seq" }))
    await user.click(screen.getByRole("button", { name: "Cancel step-up" }))
    await user.click(screen.getByRole("button", { name: "Back" }))

    expect(screen.getByLabelText("Initial Seq administrator password")).toHaveValue("")
    expect(screen.getByLabelText("Confirm administrator password")).toHaveValue("")
  })

  it("does not send a review until EULA and matching strong passwords are present", async () => {
    const user = userEvent.setup()
    let reviewCalls = 0
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () => {
        reviewCalls += 1
        return HttpResponse.json(bootstrapReview)
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    const reviewButton = screen.getByRole("button", { name: "Review exact actions" })
    expect(reviewButton).toBeDisabled()

    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), "a-short-password")
    await user.type(screen.getByLabelText("Confirm administrator password"), "different-password")
    expect(screen.getByText("The password confirmation does not match.")).toBeInTheDocument()
    expect(reviewButton).toBeDisabled()
    expect(reviewCalls).toBe(0)
  })

  it("preserves a healthy runtime and reports connection setup as needing attention", async () => {
    const user = userEvent.setup()
    const attentionOperation: DiagnosticsSeqBootstrapOperationResponse = {
      ...bootstrapSucceeded,
      status: "attention",
      currentStep: "needs-attention",
      warningCode: "seq_connection_verification_failed",
      checks: bootstrapSucceeded.checks.map((check) => ({
        ...check,
        status: check.code === "ingestion_connection"
          ? "failed"
          : check.code === "event_delivery"
            ? "not-started"
            : "passed",
        warningCode: check.code === "ingestion_connection"
          ? "seq_connection_verification_failed"
          : null,
      })),
      warnings: ["seq_connection_verification_failed"],
    }
    let operationObserved = false
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(operationObserved
        ? {
            ...optionalOverview,
            runtime: { ...optionalOverview.runtime, present: true, managed: true, running: true, state: "running", usesApprovedRuntime: true },
            health: { ...optionalOverview.health, status: "ready", reachable: true },
          }
        : optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () => HttpResponse.json(bootstrapReview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", () => HttpResponse.json({
        schemaVersion: 1,
        operationId: attentionOperation.operationId,
        status: "queued",
        acceptedAtUtc: "2026-08-05T00:00:00Z",
      }, { status: 202 })),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        () => {
          operationObserved = true
          return HttpResponse.json(attentionOperation)
        },
      ),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), "Correct-Horse-Battery-42")
    await user.type(screen.getByLabelText("Confirm administrator password"), "Correct-Horse-Battery-42")
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    await user.click(await screen.findByRole("button", { name: "Set up and connect Seq" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByRole("heading", { name: "Seq is running; setup needs attention" })).toBeInTheDocument()
    expect(screen.getByText(/runtime passed health verification and is being preserved/i)).toBeInTheDocument()
    expect(screen.getByText(/could not provision or verify its dedicated ingestion credential/i)).toBeInTheDocument()
    expect(screen.getByText("Event delivery preference")).toBeInTheDocument()
  })

  it("shows the exact failed bootstrap stage while keeping native Diagnostics available", async () => {
    const user = userEvent.setup()
    const failedOperation: DiagnosticsSeqBootstrapOperationResponse = {
      ...bootstrapSucceeded,
      status: "failed",
      currentStep: "failed",
      warningCode: "diagnostics.seq_health_failed",
      checks: bootstrapSucceeded.checks.map((check) => ({
        ...check,
        status: check.code === "seq_health" ? "failed" : "passed",
        warningCode: check.code === "seq_health" ? "diagnostics.seq_health_failed" : null,
      })),
      warnings: ["diagnostics.seq_health_failed"],
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () => HttpResponse.json(bootstrapReview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", () => HttpResponse.json({
        schemaVersion: 1,
        operationId: failedOperation.operationId,
        status: "queued",
        acceptedAtUtc: "2026-08-05T00:00:00Z",
      }, { status: 202 })),
      http.get(
        "/api/operator/diagnostics/seq/bootstrap/operations/11111111-1111-1111-1111-111111111111",
        () => HttpResponse.json(failedOperation),
      ),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), "Correct-Horse-Battery-42")
    await user.type(screen.getByLabelText("Confirm administrator password"), "Correct-Horse-Battery-42")
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    await user.click(await screen.findByRole("button", { name: "Set up and connect Seq" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByRole("heading", { name: "Seq setup did not complete" })).toBeInTheDocument()
    expect(screen.getByText("The runtime did not pass Seq health verification.")).toBeInTheDocument()
    const setupRegion = screen.getByRole("region", { name: "Set up Seq" })
    expect(within(setupRegion).getByText(/MEM-native Diagnostics continue/i)).toBeInTheDocument()
  })

  it("clears one-time passwords and returns to the security step after execution is rejected", async () => {
    const user = userEvent.setup()
    const password = "Correct-Horse-Battery-42"
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/review", () => HttpResponse.json(bootstrapReview)),
      http.post("/api/operator/diagnostics/seq/bootstrap/execute", () =>
        HttpResponse.json({ code: "seq_operation_in_progress" }, { status: 409 })),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Set up Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue" }))
    await user.click(screen.getByRole("checkbox", { name: /accept the Seq End User License Agreement/i }))
    await user.type(screen.getByLabelText("Initial Seq administrator password"), password)
    await user.type(screen.getByLabelText("Confirm administrator password"), password)
    await user.click(screen.getByRole("button", { name: "Review exact actions" }))
    await user.click(await screen.findByRole("button", { name: "Set up and connect Seq" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByRole("heading", { name: "Security and access" })).toBeInTheDocument()
    expect(screen.getByLabelText("Initial Seq administrator password")).toHaveValue("")
    expect(screen.getByLabelText("Confirm administrator password")).toHaveValue("")
    expect(screen.getByText("Another Seq operation is already running.")).toBeInTheDocument()
  })

  it("keeps an unconfigured Seq installation neutral and explains the product boundary", async () => {
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(optionalOverview)),
    )

    renderPage(operatorSession)

    expect(await screen.findByRole("heading", { name: "Seq overview" })).toBeInTheDocument()
    expect(document.querySelector('img[src="/brands/seq-mark.svg"]')).toBeInTheDocument()
    const breadcrumbs = screen.getByRole("navigation", { name: "Breadcrumb" })
    expect(breadcrumbs).toHaveTextContent("Diagnostics")
    expect(breadcrumbs).toHaveTextContent("Advanced logging with Seq")
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute("href", "/diagnostics")
    expect(screen.getAllByText("Optional and disabled").length).toBeGreaterThan(0)
    expect(screen.getByText("How Seq fits with MEM Diagnostics")).toBeInTheDocument()
    const setupRegion = screen.getByRole("region", { name: "Set up Seq" })
    expect(within(setupRegion).queryByRole("button", { name: "Set up Seq" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Review setup" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Seq documentation overview" })).toHaveAttribute(
      "href",
      "https://datalust.co/docs",
    )
    expect(screen.queryByRole("button", { name: /deploy/i })).not.toBeInTheDocument()
  })

  it("projects completed setup for an existing MEM-managed runtime without offering another deployment review", async () => {
    let reviewCalls = 0
    const restartedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      health: {
        ...ownerOverview.health,
        status: "optional-disabled",
        reachable: false,
        lastCheckedAtUtc: null,
        lastSucceededAtUtc: null,
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(restartedOverview)),
      http.get("/api/operator/diagnostics/seq/bootstrap", () => HttpResponse.json({
        ...bootstrapOverview,
        state: "running",
        setupAvailable: false,
        runtimeOwnershipState: "managed",
        eulaAccepted: true,
        administratorSecretState: "available",
        uiAuthorityState: "configured",
        canStartSetup: false,
      })),
      http.post("/api/operator/diagnostics/seq/setup/review", () => {
        reviewCalls += 1
        return HttpResponse.json(readyReview)
      }),
    )

    renderPage()

    expect(await screen.findByRole("link", { name: "Open Seq" })).toHaveAttribute(
      "href",
      "https://seq.internal.example/",
    )
    expect(screen.getByRole("link", { name: "Open Seq" })).toHaveAttribute("data-variant", "default")
    expect(document.querySelector("[data-seq-overview-grid]")).toHaveClass("md:grid-cols-2")
    expect(document.querySelector("[data-seq-overview-grid]")).toHaveClass("2xl:grid-cols-4")
    expect(document.querySelector("[data-seq-overview-grid]")).not.toHaveClass("lg:grid-cols-4")
    expect(await screen.findByRole("heading", { name: "Seq setup complete" })).toBeInTheDocument()
    expect(screen.getByText("MEM event delivery is active")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Review setup" })).not.toBeInTheDocument()
    expect(screen.queryByText("Ready for a reviewed deployment")).not.toBeInTheDocument()
    expect(screen.queryByText("Future deployment effects")).not.toBeInTheDocument()
    expect(reviewCalls).toBe(0)
  })

  it("shows missing prerequisites truthfully and never pulls or mutates from the browser", async () => {
    const user = userEvent.setup()
    const warnings = [
      "seq_approved_image_missing",
      "diagnostics.seq_admin_password_hash_unavailable",
    ]
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json({
        ...ownerOverview,
        runtime: { ...ownerOverview.runtime, state: "absent", present: false, managed: false },
      })),
      http.post("/api/operator/diagnostics/seq/setup/review", () => HttpResponse.json({
        ...readyReview,
        readyForDeployment: false,
        image: {
          ...readyReview.image,
          local: false,
          immutableIdentityAvailable: false,
          warningCode: "seq_approved_image_missing",
        },
        secrets: {
          ...readyReview.secrets,
          administratorPasswordHashAvailable: false,
        },
        warnings,
      })),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Review setup" }))

    expect(await screen.findByText("Setup is not ready")).toBeInTheDocument()
    expect(screen.getByText(/approved Seq image is not available locally/i)).toBeInTheDocument()
    expect(screen.getAllByText(/administrator password-hash secret is unavailable/i).length).toBeGreaterThan(0)
    expect(screen.queryByRole("button", { name: /pull|install|deploy/i })).not.toBeInTheDocument()
  })

  it("opens the exact server-approved UI authority for the active Seq context", async () => {
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json({
        ...ownerOverview,
        ui: {
          configured: true,
          available: true,
          url: "http://127.0.0.1:16341/",
          configurable: true,
        },
      })),
    )

    renderPage()

    const openSeq = await screen.findByRole("link", { name: "Open Seq" })
    expect(openSeq).toHaveAttribute("href", "http://127.0.0.1:16341/")
    expect(openSeq).not.toHaveAttribute("href", "http://127.0.0.1:15341/")
    expect(openSeq).toHaveAttribute("target", "_blank")
  })

  it("configures a private Seq UI authority after deployment and opens it in a new tab", async () => {
    const user = userEvent.setup()
    let configured = false
    let requestBody: unknown = null
    const disconnectedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        secretState: "not-required",
      },
      ui: {
        configured: false,
        available: false,
        url: null,
        configurable: true,
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canOpenUi: false,
        canEnableDelivery: false,
        canDisableDelivery: false,
        canConnect: true,
      },
      connection: {
        credentialState: "absent",
        verificationState: "not-verified",
        verifiedAtUtc: null,
        apiKeyId: null,
        lastVerificationId: null,
        lastEventId: null,
      },
      warnings: ["seq_ui_authority_not_configured"],
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(configured
        ? {
            ...disconnectedOverview,
            ui: {
              configured: true,
              available: true,
              url: "http://127.0.0.1:15341/",
              configurable: true,
            },
            capabilities: {
              ...disconnectedOverview.capabilities,
              canOpenUi: true,
            },
            warnings: [],
          }
        : disconnectedOverview)),
      http.put("/api/operator/diagnostics/seq/ui-authority", async ({ request }) => {
        requestBody = await request.json()
        configured = true
        return HttpResponse.json({
          schemaVersion: 1,
          configured: true,
          url: "http://127.0.0.1:15341/",
          updatedAtUtc: "2026-08-05T05:00:00Z",
        })
      }),
    )

    renderPage()

    expect(await screen.findByText("Seq is running, but MEM is not connected yet")).toBeInTheDocument()
    expect(screen.getByText(/No MEM events will appear in Seq/i)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Open Seq" })).toBeDisabled()
    await user.click(screen.getByRole("button", { name: "Configure Seq access" }))
    const input = screen.getByRole("textbox", { name: "Private Seq UI URL" })
    await user.type(input, "http://127.0.0.1:15341")
    await user.click(screen.getByRole("button", { name: "Save access" }))

    expect(requestBody).toEqual({ url: "http://127.0.0.1:15341" })
    expect(await screen.findByRole("link", { name: "Open Seq" })).toHaveAttribute(
      "href",
      "http://127.0.0.1:15341/",
    )
    expect(screen.getByRole("link", { name: "Open Seq" })).toHaveAttribute("target", "_blank")
  })

  it("provisions and verifies the dedicated Seq ingestion credential after step-up", async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null
    const disconnectedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        secretState: "not-required",
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canConnect: true,
        canEnableDelivery: false,
        canDisableDelivery: false,
      },
      connection: {
        credentialState: "absent",
        verificationState: "not-verified",
        verifiedAtUtc: null,
        apiKeyId: null,
        lastVerificationId: null,
        lastEventId: null,
      },
    }
    const connectedOverview: DiagnosticsSeqOverviewResponse = {
      ...disconnectedOverview,
      capabilities: {
        ...disconnectedOverview.capabilities,
        canConnect: false,
        canEnableDelivery: true,
      },
      connection: {
        credentialState: "available",
        verificationState: "verified",
        verifiedAtUtc: "2026-08-05T06:00:00Z",
        apiKeyId: "api-key-mem",
        lastVerificationId: "seq-connect-verification-1",
        lastEventId: "event-verification-1",
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(disconnectedOverview)),
      http.post("/api/operator/diagnostics/seq/connect", async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "11111111-1111-1111-1111-111111111111",
          status: "succeeded",
          credentialState: "available",
          verificationState: "verified",
          apiKeyId: "api-key-mem",
          verificationId: "seq-connect-verification-1",
          eventId: "event-verification-1",
          verifiedAtUtc: "2026-08-05T06:00:00Z",
          reusedCredential: false,
          overview: connectedOverview,
        })
      }),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Connect MEM to Seq" })).toBeInTheDocument()
    expect(screen.queryByLabelText("Current Seq administrator password")).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Verify identity to continue" }))
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))
    await user.type(
      await screen.findByLabelText("Current Seq administrator password"),
      "one-time-seq-password",
    )
    await user.click(screen.getByRole("button", { name: "Provision and verify" }))
    expect(screen.queryByLabelText("Current Seq administrator password")).not.toBeInTheDocument()

    expect(requestBody).toEqual({ administratorPassword: "one-time-seq-password" })
    expect(await screen.findByText("Dedicated ingestion verified")).toBeInTheDocument()
    expect(screen.getByText("seq-connect-verification-1")).toBeInTheDocument()
    expect(screen.getByText("event-verification-1")).toBeInTheDocument()
    expect(screen.getByText(/ongoing MEM event delivery remains disabled/i)).toBeInTheDocument()
  })

  it("shows the Seq password form immediately when high-risk step-up is disabled", async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null
    const disconnectedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        secretState: "not-required",
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canConnect: true,
        canEnableDelivery: false,
        canDisableDelivery: false,
        requiresRecentStepUp: false,
      },
      connection: {
        credentialState: "absent",
        verificationState: "not-verified",
        verifiedAtUtc: null,
        apiKeyId: null,
        lastVerificationId: null,
        lastEventId: null,
      },
    }
    const connectedOverview: DiagnosticsSeqOverviewResponse = {
      ...disconnectedOverview,
      capabilities: {
        ...disconnectedOverview.capabilities,
        canConnect: false,
        canEnableDelivery: true,
      },
      connection: {
        credentialState: "available",
        verificationState: "verified",
        verifiedAtUtc: "2026-08-05T06:00:00Z",
        apiKeyId: "api-key-mem",
        lastVerificationId: "seq-connect-verification-policy-off",
        lastEventId: "event-verification-policy-off",
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(disconnectedOverview)),
      http.post("/api/operator/diagnostics/seq/connect", async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "22222222-2222-2222-2222-222222222222",
          status: "succeeded",
          credentialState: "available",
          verificationState: "verified",
          apiKeyId: "api-key-mem",
          verificationId: "seq-connect-verification-policy-off",
          eventId: "event-verification-policy-off",
          verifiedAtUtc: "2026-08-05T06:00:00Z",
          reusedCredential: false,
          overview: connectedOverview,
        })
      }),
    )

    renderPage()

    expect(await screen.findByLabelText("Current Seq administrator password")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Verify identity to continue" })).not.toBeInTheDocument()
    expect(screen.getByText(/Additional identity verification is disabled/i)).toBeInTheDocument()
    await user.type(
      screen.getByLabelText("Current Seq administrator password"),
      "one-time-seq-password",
    )
    await user.click(screen.getByRole("button", { name: "Provision and verify" }))

    expect(requestBody).toEqual({ administratorPassword: "one-time-seq-password" })
    expect(await screen.findByText("Dedicated ingestion verified")).toBeInTheDocument()
  })

  it("reports Seq administration compatibility failures without blaming the password", async () => {
    const user = userEvent.setup()
    const disconnectedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        secretState: "not-required",
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canConnect: true,
        canEnableDelivery: false,
        canDisableDelivery: false,
        requiresRecentStepUp: false,
      },
      connection: {
        credentialState: "absent",
        verificationState: "not-verified",
        verifiedAtUtc: null,
        apiKeyId: null,
        lastVerificationId: null,
        lastEventId: null,
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(disconnectedOverview)),
      http.post("/api/operator/diagnostics/seq/connect", () => HttpResponse.json({
        code: "seq_administration_api_incompatible",
        detail: "raw server detail must not become the localized operator message",
      }, { status: 503 })),
    )

    renderPage()
    await user.type(
      await screen.findByLabelText("Current Seq administrator password"),
      "one-time-seq-password",
    )
    await user.click(screen.getByRole("button", { name: "Provision and verify" }))

    expect(await screen.findByText(/approved Seq administration API contract was not available/i)).toBeInTheDocument()
    expect(screen.queryByText(/supplied administrator password/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/raw server detail/i)).not.toBeInTheDocument()
  })

  it("rejects unspecified bind addresses in the Seq access editor", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json({
        ...ownerOverview,
        ui: { configured: false, available: false, url: null, configurable: true },
        capabilities: { ...ownerOverview.capabilities, canOpenUi: false },
      })),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Configure Seq access" }))
    await user.type(
      screen.getByRole("textbox", { name: "Private Seq UI URL" }),
      "http://0.0.0.0:15341",
    )

    expect(screen.getByText(/unspecified bind address/i)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Save access" })).toBeDisabled()
  })

  it("rejects an unsafe browser-facing Seq URL", async () => {
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json({
        ...ownerOverview,
        ui: {
          configured: true,
          available: true,
          url: "https://operator:secret@seq.internal.example/",
          configurable: true,
        },
      })),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Seq overview" })).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open Seq" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Open Seq" })).toBeDisabled()
  })

  it("does not request operator-only Seq details for an Auditor", async () => {
    let requested = false
    server.use(
      http.get("/api/operator/diagnostics/seq", () => {
        requested = true
        return HttpResponse.json(optionalOverview)
      }),
    )

    renderPage(auditorSession)

    expect(await screen.findByText("Seq details are restricted")).toBeInTheDocument()
    expect(requested).toBe(false)
  })

  it("unlocks reviewed deployment and retries only that action after step-up", async () => {
    const user = userEvent.setup()
    let deployCalls = 0
    const absentOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: { ...ownerOverview.delivery, enabled: false, desiredEnabled: false },
      runtime: {
        ...ownerOverview.runtime,
        present: false,
        managed: false,
        state: "absent",
        running: false,
        usesApprovedRuntime: false,
      },
      health: { ...ownerOverview.health, status: "optional-disabled", reachable: false },
      capabilities: {
        ...ownerOverview.capabilities,
        canDeploy: true,
        canStop: false,
        canRestart: false,
        canDisableDelivery: false,
        canEnableDelivery: false,
        canCheckHealth: false,
      },
    }
    const deployedOverview: DiagnosticsSeqOverviewResponse = {
      ...absentOverview,
      runtime: {
        ...absentOverview.runtime,
        present: true,
        managed: true,
        state: "running",
        running: true,
        usesApprovedRuntime: true,
      },
      health: { ...absentOverview.health, status: "ready", reachable: true },
      capabilities: {
        ...absentOverview.capabilities,
        canDeploy: false,
        canStop: true,
        canRestart: true,
        canEnableDelivery: true,
        canCheckHealth: true,
      },
    }

    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(absentOverview)),
      http.post("/api/operator/diagnostics/seq/setup/review", () => HttpResponse.json(readyReview)),
      http.post("/api/operator/diagnostics/seq/runtime/deploy", () => {
        deployCalls += 1
        return deployCalls === 1
          ? HttpResponse.json({ code: "step_up_required" }, { status: 403 })
          : HttpResponse.json(operationResult("seq.deploy", deployedOverview))
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Review setup" }))
    expect(await screen.findByRole("button", { name: "Deploy Seq" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Deploy Seq" }))
    await user.click(screen.getByRole("button", { name: "Continue to verification" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Seq runtime deployed and verified.")).toBeInTheDocument()
    expect(deployCalls).toBe(2)
    expect(screen.getByText("11111111-1111-1111-1111-111111111111")).toBeInTheDocument()
  })

  it("starts a stopped managed runtime and reports only the server-confirmed result", async () => {
    const user = userEvent.setup()
    const stoppedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      runtime: { ...ownerOverview.runtime, state: "stopped", running: false },
      health: { ...ownerOverview.health, status: "stopped-intentionally", reachable: false },
      capabilities: {
        ...ownerOverview.capabilities,
        canStart: true,
        canStop: false,
        canRestart: false,
        canCheckHealth: false,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(stoppedOverview)),
      http.post("/api/operator/diagnostics/seq/runtime/start", () =>
        HttpResponse.json(operationResult("seq.start", ownerOverview))),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Start Seq" }))
    expect(await screen.findByText("Seq runtime started and verified.")).toBeInTheDocument()
    expect(screen.getAllByText("Running").length).toBeGreaterThan(0)
  })

  it("requires explicit confirmation before stopping the managed runtime", async () => {
    const user = userEvent.setup()
    let stopCalls = 0
    const stoppedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      runtime: { ...ownerOverview.runtime, state: "stopped", running: false },
      health: { ...ownerOverview.health, status: "stopped-intentionally", reachable: false },
      capabilities: {
        ...ownerOverview.capabilities,
        canStart: true,
        canStop: false,
        canRestart: false,
        canCheckHealth: false,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(ownerOverview)),
      http.post("/api/operator/diagnostics/seq/runtime/stop", () => {
        stopCalls += 1
        return HttpResponse.json(operationResult("seq.stop", stoppedOverview))
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Stop Seq" }))
    const dialog = screen.getByRole("alertdialog")
    expect(within(dialog).getByText("Stop Seq?")).toBeInTheDocument()
    expect(stopCalls).toBe(0)
    await user.click(within(dialog).getByRole("button", { name: "Stop Seq" }))

    expect(await screen.findByText("Seq runtime stopped intentionally.")).toBeInTheDocument()
    expect(stopCalls).toBe(1)
  })

  it("requires explicit confirmation and server health proof before reporting restart success", async () => {
    const user = userEvent.setup()
    let restartCalls = 0
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(ownerOverview)),
      http.post("/api/operator/diagnostics/seq/runtime/restart", () => {
        restartCalls += 1
        return HttpResponse.json(operationResult("seq.restart", ownerOverview))
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Restart Seq" }))
    const dialog = screen.getByRole("alertdialog")
    expect(within(dialog).getByText("Restart Seq?")).toBeInTheDocument()
    expect(restartCalls).toBe(0)
    await user.click(within(dialog).getByRole("button", { name: "Restart Seq" }))

    expect(await screen.findByText("Seq runtime restarted and verified.")).toBeInTheDocument()
    expect(restartCalls).toBe(1)
  })

  it("stages delivery disablement truthfully and keeps the current process distinct", async () => {
    const user = userEvent.setup()
    const pendingOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        desiredEnabled: false,
        configurationState: "disable-pending-restart",
        restartRequired: true,
        preferenceUpdatedAtUtc: "2026-08-04T01:00:00Z",
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canDisableDelivery: false,
        canEnableDelivery: true,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(ownerOverview)),
      http.post("/api/operator/diagnostics/seq/delivery", () =>
        HttpResponse.json(operationResult("seq.delivery.disable", pendingOverview))),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Disable event delivery" }))
    await user.click(screen.getByRole("button", { name: "Continue to verification" }))

    expect(await screen.findByText("API restart required")).toBeInTheDocument()
    expect(screen.getAllByText(/current process may continue sending events/i).length).toBeGreaterThan(0)
    expect(screen.getByText("Seq delivery preference disabled; API restart may be required.")).toBeInTheDocument()
  })

  it("stages delivery enablement, shows the exact restart command, and does not claim immediate activation", async () => {
    const user = userEvent.setup()
    const disabledOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        activationState: "disabled",
        activationVerifiedAtUtc: null,
        lastActivationVerificationId: null,
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canEnableDelivery: true,
        canDisableDelivery: false,
        canVerifyDelivery: false,
      },
    }
    const pendingOverview: DiagnosticsSeqOverviewResponse = {
      ...disabledOverview,
      delivery: {
        ...disabledOverview.delivery,
        desiredEnabled: true,
        configurationState: "enable-pending-restart",
        restartRequired: true,
        preferenceUpdatedAtUtc: "2026-08-05T09:00:00Z",
        activationState: "restart-pending",
      },
      capabilities: {
        ...disabledOverview.capabilities,
        canEnableDelivery: false,
        canDisableDelivery: true,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(disabledOverview)),
      http.post("/api/operator/diagnostics/seq/delivery", () =>
        HttpResponse.json(operationResult("seq.delivery.enable", pendingOverview))),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Enable event delivery" }))
    await user.click(screen.getByRole("button", { name: "Continue to verification" }))

    const restartTitle = await screen.findByText("API restart required")
    const restartAlert = restartTitle.closest('[role="alert"]')
    expect(restartAlert).not.toBeNull()
    expect(restartAlert).toHaveAttribute("data-seq-restart-attention")
    expect(restartAlert).toHaveClass("border-amber-500/40")
    expect(restartAlert).toHaveClass("bg-amber-500/10")
    expect(
      within(restartAlert as HTMLElement).getByText(/current process is not sending events yet/i),
    ).toBeInTheDocument()
    expect(screen.getByText("Restart pending")).toBeInTheDocument()
    expect(within(restartAlert as HTMLElement).getByText("sudo docker restart mem-control-plane")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Emit verification event" })).not.toBeInTheDocument()
  })

  it("uses local-process restart guidance without inventing a Docker command", async () => {
    const localPending: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        desiredEnabled: true,
        configurationState: "enable-pending-restart",
        restartRequired: true,
        activationState: "restart-pending",
        restart: {
          kind: "developer-process",
          guidanceCode: "restart_local_api_process",
          command: null,
          commandAvailable: false,
        },
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(localPending)),
    )

    renderPage()

    const restartTitle = await screen.findByText("API restart required")
    const restartAlert = restartTitle.closest('[role="alert"]')
    expect(restartAlert).not.toBeNull()
    expect(within(restartAlert as HTMLElement).getByText(/Stop and restart the local MEM API process/)).toBeInTheDocument()
    expect(screen.queryByText(/sudo docker restart/)).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Copy restart command" })).not.toBeInTheDocument()
  })

  it("emits a current-process verification event only after restart and shows a copyable Seq query", async () => {
    const user = userEvent.setup()
    let verificationCalls = 0
    const restartedOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        activationState: "verification-required",
        activationVerifiedAtUtc: null,
        lastActivationVerificationId: null,
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canVerifyDelivery: true,
      },
    }
    const verifiedOverview: DiagnosticsSeqOverviewResponse = {
      ...restartedOverview,
      delivery: {
        ...restartedOverview.delivery,
        activationState: "verified",
        activationVerifiedAtUtc: "2026-08-05T09:02:00Z",
        lastActivationVerificationId: "seq-active-live-proof",
      },
      capabilities: {
        ...restartedOverview.capabilities,
        canVerifyDelivery: false,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(restartedOverview)),
      http.post("/api/operator/diagnostics/seq/delivery/verify", () => {
        verificationCalls += 1
        return HttpResponse.json({
          schemaVersion: 1,
          operationId: "33333333-3333-3333-3333-333333333333",
          status: "succeeded",
          verificationId: "seq-active-live-proof",
          emittedAtUtc: "2026-08-05T09:02:00Z",
          overview: verifiedOverview,
        })
      }),
    )

    renderPage()
    expect(await screen.findByText("Verification required")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Emit verification event" }))

    expect((await screen.findAllByText("Current API process verified")).length).toBeGreaterThan(0)
    expect(screen.getByText("VerificationId = 'seq-active-live-proof'")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Copy verification query" })).toBeInTheDocument()
    expect(verificationCalls).toBe(1)
  })


  it("reports an unattached startup sink as unavailable without offering verification", async () => {
    const unavailableOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: true,
        desiredEnabled: true,
        configurationState: "unavailable",
        restartRequired: false,
        activationState: "unavailable",
        activationVerifiedAtUtc: null,
        lastActivationVerificationId: null,
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canVerifyDelivery: false,
      },
      warnings: ["diagnostics.seq_sink_configuration_failed"],
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(unavailableOverview)),
    )

    renderPage()

    expect((await screen.findAllByText(/could not attach the Seq logging sink during startup/i)).length)
      .toBeGreaterThan(0)
    expect(screen.queryByRole("button", { name: "Emit verification event" })).not.toBeInTheDocument()
  })

  it("reports startup prerequisite failure as unavailable rather than endless restart pending", async () => {
    const unavailableOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: true,
        configurationState: "enable-pending-restart",
        restartRequired: true,
        activationState: "unavailable",
        activationVerifiedAtUtc: null,
        lastActivationVerificationId: null,
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canEnableDelivery: false,
        canDisableDelivery: true,
        canVerifyDelivery: false,
      },
      warnings: ["seq_delivery_startup_prerequisites_unavailable"],
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(unavailableOverview)),
    )

    renderPage()

    expect((await screen.findAllByText("Unavailable")).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/could not activate the staged Seq delivery preference/i).length).toBeGreaterThan(0)
    expect(screen.queryByText("API restart required")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Emit verification event" })).not.toBeInTheDocument()
  })

  it("keeps runtime removal blocked while current or desired delivery is enabled", async () => {
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(ownerOverview)),
    )

    renderPage()
    expect(await screen.findByText(/Disable event delivery and restart the API/i)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Remove runtime" })).not.toBeInTheDocument()
  })

  it("removes only after step-up and preserves the server-confirmed data-retention result", async () => {
    const user = userEvent.setup()
    let removeCalls = 0
    const removableOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      delivery: {
        ...ownerOverview.delivery,
        enabled: false,
        desiredEnabled: false,
        configurationState: "disabled",
        secretState: "not-required",
      },
      capabilities: {
        ...ownerOverview.capabilities,
        canRemove: true,
        canDisableDelivery: false,
        canEnableDelivery: true,
      },
    }
    const removedOverview: DiagnosticsSeqOverviewResponse = {
      ...removableOverview,
      runtime: {
        ...removableOverview.runtime,
        present: false,
        managed: false,
        state: "absent",
        running: false,
        usesApprovedRuntime: false,
      },
      health: {
        ...removableOverview.health,
        status: "runtime-absent",
        reachable: false,
      },
      ui: {
        ...removableOverview.ui,
        configured: true,
        available: false,
        url: null,
        configurable: false,
      },
      capabilities: {
        ...removableOverview.capabilities,
        canOpenUi: false,
        canRemove: false,
        canStop: false,
        canRestart: false,
        canCheckHealth: false,
        canDeploy: true,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(removableOverview)),
      http.post("/api/operator/diagnostics/seq/runtime/remove", () => {
        removeCalls += 1
        return removeCalls === 1
          ? HttpResponse.json({ code: "step_up_required" }, { status: 403 })
          : HttpResponse.json(operationResult("seq.remove", removedOverview))
      }),
    )

    renderPage()
    await user.click(await screen.findByRole("button", { name: "Remove runtime" }))
    await user.click(screen.getByRole("button", { name: "Continue to verification" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Seq runtime removed; data retained.")).toBeInTheDocument()
    expect(removeCalls).toBe(2)
    expect(screen.getAllByText("Not deployed").length).toBeGreaterThan(0)
    expect(screen.queryByRole("link", { name: "Open Seq" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Open Seq" })).not.toBeInTheDocument()
    expect(screen.queryByText("Dedicated ingestion verified")).not.toBeInTheDocument()
    expect(screen.getByText(
      "The private Seq UI authority is retained, but no healthy MEM-managed Seq runtime is currently available to open.",
    )).toBeInTheDocument()
  })

  it("lets an Operator run only the bounded health check", async () => {
    const user = userEvent.setup()
    const operatorOverview: DiagnosticsSeqOverviewResponse = {
      ...ownerOverview,
      ui: { ...ownerOverview.ui, available: false, url: null },
      capabilities: {
        ...optionalOverview.capabilities,
        canCheckHealth: true,
      },
    }
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(operatorOverview)),
      http.post("/api/operator/diagnostics/seq/health-check", () =>
        HttpResponse.json(operationResult("seq.health-check", operatorOverview))),
    )

    renderPage(operatorSession)
    await user.click(await screen.findByRole("button", { name: "Check health" }))
    expect(await screen.findByText("Seq health check passed.")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Stop Seq" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Disable event delivery" })).not.toBeInTheDocument()
  })

  it("renders the overview, setup boundary, starter searches, and help in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get("/api/operator/diagnostics/seq", () => HttpResponse.json(ownerOverview)),
    )

    renderPage()

    expect(await screen.findByText("Wie Seq zur MEM-Diagnose passt")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Laufzeitsteuerung" })).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Ereigniszustellung" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Seq stoppen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Ereigniszustellung deaktivieren" })).toBeInTheDocument()
    expect(screen.getByText("Erste Schritte mit Seq-Suchen")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Übersicht der Seq-Dokumentation" })).toBeInTheDocument()
  })
})
