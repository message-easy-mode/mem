import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import type {
  RuntimeStackFederationApplyResult,
  RuntimeStackFederationReview,
  RuntimeStackFederationState,
} from "../api/federation.types"
import { StackFederationPage } from "./stack-federation-page"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onVerified,
  }: {
    open: boolean
    onVerified?: () => void
  }) => open ? (
    <button type="button" onClick={onVerified}>Complete identity verification</button>
  ) : null,
}))

const stackEndpoint = "/internal/host-agent/runtime-stacks/demo-stack"
const federationEndpoint = `${stackEndpoint}/federation`
const reviewEndpoint = `${federationEndpoint}/review`
const applyEndpoint = `${federationEndpoint}/apply`
const reviewHash = `sha256:${"a".repeat(64)}`

const stack = {
  source: "control-plane",
  status: "ready",
  stackId: "7085b97d-3d30-434e-976a-62df0178be16",
  slug: "demo-stack",
  matrix: {
    serviceKey: "matrix",
    instanceId: "11111111-1111-1111-1111-111111111111",
    containerId: "matrix-container",
    containerName: "matrix-demo-stack",
    internalHost: "matrix-demo-stack",
    internalBaseUrl: "http://matrix-demo-stack:8008",
    publicHost: "matrix.example.test",
    publicBaseUrl: "https://matrix.example.test",
    dataPath: null,
    configPath: null,
    publicRouteId: "12",
    npmCertificateId: 2,
    runtimeMetadata: {},
  },
  element: {
    serviceKey: "element-web",
    instanceId: "22222222-2222-2222-2222-222222222222",
    containerId: "element-container",
    containerName: "element-demo-stack",
    internalHost: "element-demo-stack",
    internalBaseUrl: "http://element-demo-stack:80",
    publicHost: "chat.example.test",
    publicBaseUrl: "https://chat.example.test",
    dataPath: null,
    configPath: null,
    publicRouteId: "13",
    npmCertificateId: 2,
    runtimeMetadata: {},
  },
  lastVerifiedAtUtc: "2026-07-23T01:00:00Z",
  detail: null,
}

function state(overrides: Partial<RuntimeStackFederationState> = {}): RuntimeStackFederationState {
  return {
    source: "control-plane",
    status: "ok",
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    mode: "public",
    configurationState: "healthy",
    allowlist: [],
    enforcementKind: "synapse_unrestricted",
    matrixContainerRunning: true,
    matrixDirectHostPortExposed: false,
    ingressMode: "normal",
    serverWellKnownPublished: true,
    federationPathsPubliclyForwarded: true,
    signingKeyPathsPubliclyForwarded: true,
    canonicalRouteEnabled: true,
    canonicalRouteTargetsMatrix: true,
    canonicalCertificatePresent: true,
    alternateMatrixRouteDetected: false,
    stateFingerprint: "sha256:safe",
    latestOperation: null,
    checks: [
      {
        code: "federation.config.supported",
        status: "passed",
        detail: "The active Synapse federation configuration uses a supported representation.",
      },
      {
        code: "federation.direct_host_port.absent",
        status: "passed",
        detail: "No direct Matrix Docker host port is published.",
      },
    ],
    warnings: [],
    problems: [],
    ...overrides,
  }
}

function review(overrides: Partial<RuntimeStackFederationReview> = {}): RuntimeStackFederationReview {
  return {
    source: "control-plane",
    status: "ready",
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    currentMode: "public",
    proposedMode: "restricted",
    currentAllowlist: [],
    canonicalAllowlist: ["matrix.example.org", "partner.example"],
    addedDomains: ["matrix.example.org", "partner.example"],
    removedDomains: [],
    restartRequired: true,
    ingressChangeRequired: false,
    noChange: false,
    warnings: [
      {
        code: "federation_existing_rooms_may_be_affected",
        detail: "Existing federated rooms may be affected.",
      },
    ],
    confirmationText: "Apply Restricted federation and restart Matrix.",
    reviewHash,
    ...overrides,
  }
}

function applyResult(overrides: Partial<RuntimeStackFederationApplyResult> = {}): RuntimeStackFederationApplyResult {
  return {
    source: "control-plane",
    status: "succeeded",
    operationId: "33333333-3333-3333-3333-333333333333",
    previousMode: "public",
    requestedMode: "restricted",
    observedMode: "restricted",
    rollbackAttempted: false,
    rollbackSucceeded: null,
    checks: [],
    errorCode: null,
    detail: "Restricted federation is active.",
    ...overrides,
  }
}

function renderPage(federationState: RuntimeStackFederationState) {
  server.use(
    http.get(stackEndpoint, () => HttpResponse.json(stack)),
    http.get(federationEndpoint, () => HttpResponse.json(federationState)),
  )

  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/federation"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/federation" element={<StackFederationPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

async function prepareRestrictedReview(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText("Manage federation mode")
  await user.click(screen.getByRole("button", { name: /Restricted federation/ }))
  const editor = screen.getByLabelText("Approved homeservers")
  await user.type(editor, "Partner.Example.\nmatrix.example.org")
  await user.click(screen.getByRole("button", { name: "Review change" }))
  return screen.findByRole("alertdialog", { name: "Review federation change" })
}

afterEach(() => window.localStorage.clear())

describe("StackFederationPage FED-CORE-03A", () => {
  it("reviews canonical Public to Restricted changes before applying them", async () => {
    const user = userEvent.setup()
    let reviewBody: unknown
    let applyBody: unknown
    server.use(
      http.post(reviewEndpoint, async ({ request }) => {
        reviewBody = await request.json()
        return HttpResponse.json(review())
      }),
      http.post(applyEndpoint, async ({ request }) => {
        applyBody = await request.json()
        return HttpResponse.json(applyResult())
      }),
    )
    renderPage(state())

    const dialog = await prepareRestrictedReview(user)

    expect(reviewBody).toEqual({
      mode: "restricted",
      allowlist: ["matrix.example.org", "partner.example"],
    })
    expect(within(dialog).getByText("Current mode")).toBeInTheDocument()
    expect(within(dialog).getByText("Proposed mode")).toBeInTheDocument()
    expect(within(dialog).getAllByText("matrix.example.org")).toHaveLength(2)
    expect(within(dialog).getAllByText("partner.example")).toHaveLength(2)
    expect(within(dialog).getByText("Existing federated rooms may be affected")).toBeInTheDocument()
    expect(within(dialog).getAllByText(/does not delete existing users, rooms, messages/i).length).toBeGreaterThan(0)
    expect(within(dialog).getByText("Automatic rollback")).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Federation policy applied")).toBeInTheDocument()
    expect(screen.getByText(/Restricted federation is active/)).toBeInTheDocument()
    expect(applyBody).toMatchObject({
      mode: "restricted",
      allowlist: ["matrix.example.org", "partner.example"],
      reviewHash,
    })
    expect((applyBody as { idempotencyKey: string }).idempotencyKey)
      .toMatch(/^federation-demo-stack-/)
  })

  it("retries the exact confirmed apply request after server-required step-up", async () => {
    const user = userEvent.setup()
    const applyBodies: unknown[] = []
    let attempts = 0
    server.use(
      http.post(reviewEndpoint, () => HttpResponse.json(review())),
      http.post(applyEndpoint, async ({ request }) => {
        applyBodies.push(await request.json())
        attempts += 1
        return attempts === 1
          ? HttpResponse.json(
              { error: "step_up_required", detail: "Recent identity verification is required." },
              { status: 403 },
            )
          : HttpResponse.json(applyResult())
      }),
    )
    renderPage(state())

    const dialog = await prepareRestrictedReview(user)
    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    await user.click(await screen.findByRole("button", { name: "Complete identity verification" }))

    expect(await screen.findByText("Federation policy applied")).toBeInTheDocument()
    expect(applyBodies).toHaveLength(2)
    expect(applyBodies[1]).toEqual(applyBodies[0])
  })

  it("shows candidate rejection as a no-mutation outcome", async () => {
    const user = userEvent.setup()
    server.use(
      http.post(reviewEndpoint, () => HttpResponse.json(review())),
      http.post(applyEndpoint, () => HttpResponse.json(
        applyResult({
          status: "candidate_rejected",
          observedMode: "public",
          errorCode: "federation_config_validation_failed",
          detail: "Candidate rejected.",
        }),
        { status: 422 },
      )),
    )
    renderPage(state())

    const dialog = await prepareRestrictedReview(user)
    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Candidate rejected before mutation")).toBeInTheDocument()
    expect(screen.getByText(/active configuration was not changed/i)).toBeInTheDocument()
  })

  it("shows a verified automatic rollback outcome", async () => {
    const user = userEvent.setup()
    server.use(
      http.post(reviewEndpoint, () => HttpResponse.json(review())),
      http.post(applyEndpoint, () => HttpResponse.json(applyResult({
        status: "rolled_back",
        observedMode: "public",
        rollbackAttempted: true,
        rollbackSucceeded: true,
        errorCode: "federation_restart_failed",
      }))),
    )
    renderPage(state())

    const dialog = await prepareRestrictedReview(user)
    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Apply failed; previous state restored")).toBeInTheDocument()
    expect(screen.getByText(/restored and verified the previous Public federation state/i)).toBeInTheDocument()
  })

  it("shows rollback failure as immediate manual recovery work", async () => {
    const user = userEvent.setup()
    server.use(
      http.post(reviewEndpoint, () => HttpResponse.json(review())),
      http.post(applyEndpoint, () => HttpResponse.json(
        applyResult({
          status: "failed",
          observedMode: "unknown",
          rollbackAttempted: true,
          rollbackSucceeded: false,
          errorCode: "federation_rollback_failed",
        }),
        { status: 500 },
      )),
    )
    renderPage(state())

    const dialog = await prepareRestrictedReview(user)
    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Automatic rollback failed")).toBeInTheDocument()
    expect(screen.getByText(/Stop further policy changes/i)).toBeInTheDocument()
  })

  it("keeps unsupported custom configuration read-only", async () => {
    renderPage(state({
      mode: "unknown",
      configurationState: "custom_unsupported",
      ingressMode: "custom_unsupported",
      enforcementKind: "unknown",
      problems: [
        {
          code: "federation_ingress_custom_unsupported",
          detail: "The canonical Matrix route uses custom advanced configuration.",
        },
      ],
    }))

    expect(await screen.findByText("Custom federation configuration detected")).toBeInTheDocument()
    expect(screen.getByText("MEM will not rewrite a custom or ambiguous federation configuration automatically.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Review change" })).toBeDisabled()
  })

  it("reviews and applies strong Local-only with NPM and client-access consequences", async () => {
    const user = userEvent.setup()
    let reviewBody: unknown
    let applyBody: unknown
    server.use(
      http.post(reviewEndpoint, async ({ request }) => {
        reviewBody = await request.json()
        return HttpResponse.json(review({
          proposedMode: "local_only",
          canonicalAllowlist: [],
          addedDomains: [],
          ingressChangeRequired: true,
          warnings: [
            {
              code: "federation_existing_rooms_may_be_affected",
              detail: "Existing federated rooms may be affected.",
            },
            {
              code: "federation_historical_state_retained",
              detail: "Historical state is retained.",
            },
            {
              code: "federation_local_only_client_access_remains_public",
              detail: "Client access remains public.",
            },
          ],
          confirmationText: "Apply Local-only federation, update NPM ingress, and restart Matrix.",
        }))
      }),
      http.post(applyEndpoint, async ({ request }) => {
        applyBody = await request.json()
        return HttpResponse.json(applyResult({
          requestedMode: "local_only",
          observedMode: "local_only",
          detail: "Local-only federation is active and public federation ingress is blocked.",
        }))
      }),
    )
    renderPage(state())

    await screen.findByText("Manage federation mode")
    await user.click(screen.getByRole("button", { name: /Local-only federation/ }))
    expect(screen.getByText("Strong Local-only enforcement")).toBeInTheDocument()
    expect(screen.getByText(/Matrix client endpoint remains publicly available/i)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Review change" }))

    const dialog = await screen.findByRole("alertdialog", { name: "Review federation change" })
    expect(reviewBody).toEqual({ mode: "local_only", allowlist: [] })
    expect(within(dialog).getByText("Matrix client access remains available")).toBeInTheDocument()
    expect(within(dialog).getByText("Historical Matrix state is retained")).toBeInTheDocument()
    expect(within(dialog).getByText("Will change")).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Federation policy applied")).toBeInTheDocument()
    expect(screen.getByText(/Local-only federation is active/i)).toBeInTheDocument()
    expect(applyBody).toMatchObject({
      mode: "local_only",
      allowlist: [],
      reviewHash,
    })
  })

  it("clears a retained Restricted allowlist when switching to Local-only", async () => {
    const user = userEvent.setup()
    let reviewBody: unknown
    server.use(
      http.post(reviewEndpoint, async ({ request }) => {
        reviewBody = await request.json()
        return HttpResponse.json(review({
          currentMode: "restricted",
          proposedMode: "local_only",
          currentAllowlist: ["matrix-qa-bravo.deltabox.dev"],
          canonicalAllowlist: [],
          addedDomains: [],
          removedDomains: ["matrix-qa-bravo.deltabox.dev"],
          ingressChangeRequired: true,
          confirmationText: "Apply Local-only federation, update NPM ingress, and restart Matrix.",
        }))
      }),
    )
    renderPage(state({
      mode: "restricted",
      allowlist: ["matrix-qa-bravo.deltabox.dev"],
      enforcementKind: "synapse_exact_domain_allowlist",
    }))

    await screen.findByText("Manage federation mode")
    expect(screen.getByLabelText("Approved homeservers")).toHaveValue(
      "matrix-qa-bravo.deltabox.dev",
    )

    await user.click(screen.getByRole("button", { name: /Local-only federation/ }))

    expect(screen.queryByLabelText("Approved homeservers")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Review change" })).toBeEnabled()

    await user.click(screen.getByRole("button", { name: "Review change" }))

    await screen.findByRole("alertdialog", { name: "Review federation change" })
    expect(reviewBody).toEqual({ mode: "local_only", allowlist: [] })
  })

  it("allows a healthy Local-only stack to review a return to Public", async () => {
    const user = userEvent.setup()
    server.use(
      http.post(reviewEndpoint, () => HttpResponse.json(review({
        currentMode: "local_only",
        proposedMode: "public",
        canonicalAllowlist: [],
        ingressChangeRequired: true,
        confirmationText: "Apply Public federation and restart Matrix.",
      }))),
    )
    renderPage(state({
      mode: "local_only",
      ingressMode: "local_only",
      enforcementKind: "synapse_empty_allowlist_and_npm_ingress",
      serverWellKnownPublished: false,
      federationPathsPubliclyForwarded: false,
      signingKeyPathsPubliclyForwarded: false,
    }))

    await screen.findByText("Manage federation mode")
    expect(screen.getByRole("button", { name: /Local-only federation/ })).toHaveAttribute("aria-pressed", "true")
    await user.click(screen.getByRole("button", { name: /Public federation/ }))
    await user.click(screen.getByRole("button", { name: "Review change" }))

    const dialog = await screen.findByRole("alertdialog", { name: "Review federation change" })
    expect(within(dialog).getByText("Public federation")).toBeInTheDocument()
    expect(within(dialog).getByText("Will change")).toBeInTheDocument()
  })

  it("shows the Restricted editor and canonical state in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderPage(state({
      mode: "restricted",
      enforcementKind: "synapse_exact_domain_allowlist",
      allowlist: ["matrix.example.org", "partner.example"],
    }))

    expect(await screen.findByText("Föderationsmodus verwalten")).toBeInTheDocument()
    expect(await screen.findByLabelText("Genehmigte Homeserver")).toHaveValue(
      "matrix.example.org\npartner.example",
    )
    expect(screen.getByRole("button", { name: "Änderung prüfen" })).toBeEnabled()
    expect(screen.getByText("Integrierte Synapse-Richtlinie")).toBeInTheDocument()
  })
})
