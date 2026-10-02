import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { verifyOperatorStepUp } from "@/features/auth/control-plane-auth"
import { TRACKED_DESTROY_STORAGE_KEY } from "@/features/operator/stacks/lib/destroy-operation-tracking"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import type {
  RuntimeReconciliationActiveStack,
  RuntimeReconciliationReport,
} from "../api/runtime-reconciliation.types"
import { RuntimeReconciliationPage } from "./runtime-reconciliation-page"

vi.mock("@/features/auth/control-plane-auth", () => ({
  verifyOperatorStepUp: vi.fn(),
}))

const verifyOperatorStepUpMock = vi.mocked(verifyOperatorStepUp)
const reportEndpoint = "/internal/host-agent/admin/maintenance/runtime-reconciliation"
const interruptedStackId = "a69b081d-2ec8-473e-a6bd-ba341e5a6087"
const unlistedStackId = "b69b081d-2ec8-473e-a6bd-ba341e5a6088"
const failedCreationStackId = "c69b081d-2ec8-473e-a6bd-ba341e5a6089"
const ambiguousStackId = "d69b081d-2ec8-473e-a6bd-ba341e5a6090"
const operationId = "9411c5eb-6144-491c-af43-574bc1db4eb9"

const activeManifestStack = activeStack({
  stackId: "7085b97d-3d30-434e-976a-62df0178be16",
  slug: "verified-stack",
  hasManifest: true,
  reconciliationState: "manifest_present",
  canRemoveFromReconciliation: false,
  removalActionCode: null,
  reason: "The stack has an active runtime manifest and should be managed from Chat servers.",
  recordedServiceCount: 2,
  recordedRouteCount: 2,
  matchingNpmRouteCount: 2,
})

const interruptedStack = activeStack({
  stackId: interruptedStackId,
  slug: "demo-stack-3",
  hasManifest: false,
  reconciliationState: "interrupted_destroy_candidate",
  canRemoveFromReconciliation: true,
  removalActionCode: "finish_interrupted_removal",
  reason: "SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY",
  recordedServiceCount: 2,
  recordedRouteCount: 2,
  matchingNpmRouteCount: 0,
  missingNpmRouteCount: 2,
})

const unlistedStack = activeStack({
  stackId: unlistedStackId,
  slug: "demo-stack-4",
  hasManifest: false,
  reconciliationState: "unlisted_runtime",
  canRemoveFromReconciliation: true,
  removalActionCode: "remove_unlisted_runtime",
  reason: "SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY",
  recordedServiceCount: 2,
  recordedRouteCount: 2,
  matchingNpmRouteCount: 2,
})

const failedCreationStack = activeStack({
  stackId: failedCreationStackId,
  slug: "failed-stack",
  hasManifest: false,
  reconciliationState: "failed_creation_candidate",
  canRemoveFromReconciliation: true,
  removalActionCode: "cleanup_failed_creation",
  reason: "SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY",
  recordedServiceCount: 2,
  recordedRouteCount: 0,
})

const ambiguousStack = activeStack({
  stackId: ambiguousStackId,
  slug: "ambiguous-stack",
  hasManifest: false,
  reconciliationState: "ownership_ambiguous",
  canRemoveFromReconciliation: false,
  removalActionCode: null,
  reason: "SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY",
  recordedServiceCount: 3,
  recordedRouteCount: 2,
  mismatchedNpmRouteCount: 1,
})

const report: RuntimeReconciliationReport = {
  source: "control-plane",
  status: "needs_attention",
  checkedAtUtc: "2026-08-16T04:48:00Z",
  summary: {
    activeStackCount: 5,
    destroyedStackHistoryCount: 0,
    activeRouteCount: 6,
    npmProxyHostCount: 6,
    memManagedNpmProxyHostCount: 6,
    orphanedNpmProxyHostCount: 1,
    manifestCount: 1,
    activeDatabaseRowsWithoutManifestCount: 4,
    manifestWithoutActiveDatabaseRowCount: 0,
    removableManifestlessStackCount: 3,
  },
  activeStacks: [
    activeManifestStack,
    interruptedStack,
    unlistedStack,
    failedCreationStack,
    ambiguousStack,
  ],
  destroyedStacks: [],
  activeRoutes: [
    route(interruptedStackId, "demo-stack-3", "matrix", "matrix-demo-stack-3.deltabox.dev", "mem-matrix-demo-stack-3", 8008, "3"),
    route(interruptedStackId, "demo-stack-3", "element-web", "chat-demo-stack-3.deltabox.dev", "mem-element-demo-stack-3", 80, "4"),
    route(unlistedStackId, "demo-stack-4", "matrix", "matrix-demo-stack-4.deltabox.dev", "mem-matrix-demo-stack-4", 8008, "5"),
    route(unlistedStackId, "demo-stack-4", "element-web", "chat-demo-stack-4.deltabox.dev", "mem-element-demo-stack-4", 80, "6"),
  ],
  npmProxyHosts: [
    npmHost(5, "matrix-demo-stack-4.deltabox.dev", "mem-matrix-demo-stack-4", 8008, true),
    npmHost(6, "chat-demo-stack-4.deltabox.dev", "mem-element-demo-stack-4", 80, true),
  ],
  orphanedNpmProxyHosts: [
    npmHost(91, "chat-old-stack.deltabox.dev", "mem-element-old-stack", 80, false),
  ],
  warnings: [],
  detail: "SERVER ENGLISH REPORT DETAIL SHOULD NOT BE NORMAL UI COPY",
}

afterEach(() => {
  window.localStorage.clear()
  verifyOperatorStepUpMock.mockReset()
  vi.restoreAllMocks()
})

describe("RuntimeReconciliationPage", () => {
  it("classifies interrupted, failed-created, unlisted, and ambiguous stacks with bounded actions", async () => {
    server.use(reportHandler())

    renderPage()

    expect(await screen.findByRole("heading", { name: "Runtime reconciliation" })).toBeInTheDocument()
    const metricCard = getContainingCard(
      await screen.findByText("Recoverable unlisted stacks"),
    )
    expect(within(metricCard).getByText("3")).toBeInTheDocument()

    const unlistedCard = getContainingCard(
      await screen.findByText("Unlisted and interrupted stacks"),
    )
    const interruptedRow = within(unlistedCard).getByRole("row", { name: /demo-stack-3/ })
    expect(interruptedRow).toHaveTextContent("Interrupted removal")
    expect(interruptedRow).toHaveTextContent("matching NPM: 0")
    expect(within(interruptedRow).getByRole("button", { name: "Finish removal" })).toBeInTheDocument()

    const unlistedRow = within(unlistedCard).getByRole("row", { name: /demo-stack-4/ })
    expect(unlistedRow).toHaveTextContent("Unlisted runtime")
    expect(within(unlistedRow).getByRole("button", { name: "Remove unlisted stack" })).toBeInTheDocument()

    const failedRow = within(unlistedCard).getByRole("row", { name: /failed-stack/ })
    expect(failedRow).toHaveTextContent("Incomplete creation")
    expect(within(failedRow).getByRole("button", { name: "Clean incomplete creation" })).toBeInTheDocument()

    const ambiguousRow = within(unlistedCard).getByRole("row", { name: /ambiguous-stack/ })
    expect(ambiguousRow).toHaveTextContent("Ownership ambiguous")
    expect(within(ambiguousRow).getByText("Review only")).toBeInTheDocument()
    expect(within(ambiguousRow).queryByRole("button")).not.toBeInTheDocument()

    expect(screen.queryByText("SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY")).not.toBeInTheDocument()
    expect(screen.queryByText("SERVER ENGLISH REPORT DETAIL SHOULD NOT BE NORMAL UI COPY")).not.toBeInTheDocument()
  })

  it("accepts exact interrupted-removal confirmation and stores one durable operation", async () => {
    const user = userEvent.setup()
    const submitted: Record<string, unknown>[] = []

    server.use(
      reportHandler(),
      http.post(`/internal/host-agent/runtime-stacks/${interruptedStackId}/destroy`, async ({ request }) => {
        submitted.push((await request.json()) as Record<string, unknown>)
        return HttpResponse.json(acceptedResponse(interruptedStack), { status: 202 })
      }),
    )

    renderPage()

    const unlistedCard = getContainingCard(
      await screen.findByText("Unlisted and interrupted stacks"),
    )
    const row = within(unlistedCard).getByRole("row", { name: /demo-stack-3/ })
    await user.click(within(row).getByRole("button", { name: "Finish removal" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(dialog).toHaveTextContent("Finish removing demo-stack-3?")
    expect(dialog).toHaveTextContent("Database and local files will be retained")
    expect(within(dialog).getByRole("button", { name: "Finish removal" })).toBeDisabled()

    await user.type(
      within(dialog).getByLabelText("Removal confirmation"),
      "REMOVE demo-stack-3",
    )
    await user.click(within(dialog).getByRole("button", { name: "Finish removal" }))

    await waitFor(() => expect(submitted).toHaveLength(1))
    expect(submitted[0]).toMatchObject({
      removeContainers: true,
      removeRoutes: true,
      removeDatabase: false,
      removeFiles: false,
      force: false,
    })
    expect(submitted[0]?.idempotencyKey).toMatch(
      new RegExp(`^reconcile-remove-${interruptedStackId}-`),
    )

    const tracked = JSON.parse(
      window.localStorage.getItem(TRACKED_DESTROY_STORAGE_KEY) ?? "null",
    ) as Record<string, unknown> | null
    expect(tracked).toMatchObject({
      operationId,
      runtimeStackId: interruptedStackId,
      slug: interruptedStack.slug,
    })
  })

  it("retries the same reconciliation removal and idempotency key after step-up", async () => {
    verifyOperatorStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-08-16T06:00:00Z",
    })
    const user = userEvent.setup()
    const submitted: Record<string, unknown>[] = []

    server.use(
      reportHandler(),
      http.post(`/internal/host-agent/runtime-stacks/${unlistedStackId}/destroy`, async ({ request }) => {
        submitted.push((await request.json()) as Record<string, unknown>)
        if (submitted.length === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Fresh identity verification is required." },
            { status: 403, headers: { "Cache-Control": "no-store" } },
          )
        }
        return HttpResponse.json(acceptedResponse(unlistedStack), { status: 202 })
      }),
    )

    renderPage()

    const unlistedCard = getContainingCard(
      await screen.findByText("Unlisted and interrupted stacks"),
    )
    const row = within(unlistedCard).getByRole("row", { name: /demo-stack-4/ })
    await user.click(within(row).getByRole("button", { name: "Remove unlisted stack" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.type(
      within(dialog).getByLabelText("Removal confirmation"),
      "REMOVE demo-stack-4",
    )
    await user.click(within(dialog).getByRole("button", { name: "Remove unlisted stack" }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => expect(submitted).toHaveLength(2))
    expect(verifyOperatorStepUpMock).toHaveBeenCalledWith("Secure!Foundation123", "123456")
    expect(submitted[0]?.idempotencyKey).toEqual(submitted[1]?.idempotencyKey)
  })

  it("localises reconciliation-backed recovery in German without rendering English server reasons", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(reportHandler())

    renderPage()

    expect(await screen.findByRole("heading", { name: "Laufzeit-Abgleich" })).toBeInTheDocument()
    const unlistedCard = getContainingCard(
      await screen.findByText("Nicht gelistete und unterbrochene Stacks"),
    )
    const interruptedRow = within(unlistedCard).getByRole("row", { name: /demo-stack-3/ })
    expect(interruptedRow).toHaveTextContent("Unterbrochene Entfernung")
    expect(interruptedRow).toHaveTextContent("Mindestens eine gespeicherte NPM-Route fehlt bereits")
    expect(within(interruptedRow).getByRole("button", { name: "Entfernung abschließen" })).toBeInTheDocument()

    const ambiguousRow = within(unlistedCard).getByRole("row", { name: /ambiguous-stack/ })
    expect(ambiguousRow).toHaveTextContent("Besitzzuordnung uneindeutig")
    expect(within(ambiguousRow).getByText("Nur prüfen")).toBeInTheDocument()
    expect(screen.queryByText("SERVER ENGLISH SHOULD NOT BE NORMAL UI COPY")).not.toBeInTheDocument()
  })

  it("preserves selected orphaned-NPM cleanup behind exact confirmation", async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null

    server.use(
      reportHandler(),
      http.post(`${reportEndpoint}/npm-proxy-hosts/delete`, async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({
          source: "control-plane",
          status: "completed",
          checkedAtUtc: "2026-08-16T04:49:00Z",
          requestedProxyHostIds: [91],
          deletedCount: 1,
          alreadyMissingCount: 0,
          skippedCount: 0,
          failedCount: 0,
          results: [],
          detail: "Selected orphan cleanup completed.",
        })
      }),
    )

    renderPage()

    await screen.findByText("chat-old-stack.deltabox.dev")
    await user.click(
      screen.getByRole("checkbox", {
        name: "Select chat-old-stack.deltabox.dev for orphan cleanup",
      }),
    )
    await user.click(screen.getByRole("button", { name: "Delete selected orphaned hosts" }))
    await user.type(screen.getByPlaceholderText("DELETE ORPHANED NPM HOSTS"), "DELETE ORPHANED NPM HOSTS")
    await user.click(screen.getByRole("button", { name: "Delete selected hosts" }))

    await waitFor(() => {
      expect(requestBody).toEqual({
        proxyHostIds: [91],
        confirmationText: "DELETE ORPHANED NPM HOSTS",
      })
    })
    expect(await screen.findByText("Orphan cleanup completed")).toBeInTheDocument()
  })
})

function getContainingCard(element: HTMLElement): HTMLElement {
  const card = element.closest<HTMLElement>('[data-slot="card"]')
  if (!card) {
    throw new Error("Expected the test element to be rendered inside a card.")
  }

  return card
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/diagnostics/runtime-reconciliation"]}>
      <RuntimeReconciliationPage />
    </MemoryRouter>,
  )
}

function reportHandler(value: RuntimeReconciliationReport = report) {
  return http.get(reportEndpoint, () => HttpResponse.json(value))
}

function acceptedResponse(stack: RuntimeReconciliationActiveStack) {
  return {
    operationId,
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    status: "accepted",
    pollUrl: `/internal/host-agent/operations/${operationId}`,
    reusedExistingOperation: false,
  }
}

function activeStack(
  overrides: Partial<RuntimeReconciliationActiveStack>,
): RuntimeReconciliationActiveStack {
  return {
    stackId: "stack-id",
    slug: "stack",
    status: "public_routes_created",
    lastVerifiedStatus: "public_routes_created",
    lastVerifiedAtUtc: "2026-08-16T04:30:00Z",
    matrixPublicBaseUrl: "https://matrix-stack.deltabox.dev",
    elementPublicBaseUrl: "https://chat-stack.deltabox.dev",
    hasManifest: false,
    recordedServiceCount: 0,
    recordedRouteCount: 0,
    matchingNpmRouteCount: 0,
    missingNpmRouteCount: 0,
    mismatchedNpmRouteCount: 0,
    reconciliationState: "review_required",
    canRemoveFromReconciliation: false,
    removalActionCode: null,
    reason: null,
    ...overrides,
  }
}

function route(
  runtimeStackId: string,
  stackSlug: string,
  serviceKey: string,
  publicHost: string,
  forwardHost: string,
  forwardPort: number,
  providerRouteId: string,
) {
  return {
    routeId: `${runtimeStackId}-${serviceKey}`,
    runtimeStackId,
    stackSlug,
    serviceKey,
    provider: "npm",
    publicHost,
    forwardHost,
    forwardPort,
    npmCertificateId: 1,
    providerRouteId,
    status: "created",
  }
}

function npmHost(
  proxyHostId: number,
  domain: string,
  forwardHost: string,
  forwardPort: number,
  matchesActiveRoute: boolean,
) {
  return {
    proxyHostId,
    domainNames: [domain],
    forwardHost,
    forwardPort,
    certificateId: 1,
    enabled: true,
    nginxOnline: true,
    looksLikeMemStackRoute: true,
    matchesActiveRoute,
    matchedActiveRouteHosts: matchesActiveRoute ? [domain] : [],
    reason: null,
  }
}
