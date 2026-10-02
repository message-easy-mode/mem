import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import type { RuntimeStackSummaryResponse } from "../api/stacks.types"

import { StacksListPage } from "./stacks-list-page"

const stacksEndpoint = "/internal/host-agent/runtime-stacks"
const operationId = "9411c5eb-6144-491c-af43-574bc1db4eb9"
const trackingStorageKey = "mem.stack-destroy.pending-operation"

const stack = {
  stackId: "7085b97d-3d30-434e-976a-62df0178be16",
  slug: "cool-stack-restored",
  displayName: "Cool Family Chat",
  category: "Family",
  logoUrl: "/internal/host-agent/runtime-stacks/cool-stack-restored/logo?v=logo-revision",
  lastVerifiedStatus: "Public Routes Verified",
  lastVerifiedAtUtc: "2026-07-02T14:14:00Z",
  verificationFreshness: "current",
  matrixPublicBaseUrl: "https://matrix-cool-stack.deltabox.dev",
  elementPublicBaseUrl: "https://chat-cool-stack.deltabox.dev",
} satisfies RuntimeStackSummaryResponse

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location-search">{location.search}</output>
}

function renderPage(initialEntry = "/stacks") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <StacksListPage />
      <LocationProbe />
    </MemoryRouter>,
  )
}

function listHandler(stacks: RuntimeStackSummaryResponse[] = [stack]) {
  return http.get(stacksEndpoint, () =>
    HttpResponse.json({
      source: "host-agent",
      status: "ok",
      stacks,
      detail: null,
      categories: [{
        category: "Family",
        count: stacks.filter((item) => item.category?.toLowerCase() === "family").length,
      }],
    }),
  )
}

function acceptedResponse() {
  return {
    operationId,
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    status: "accepted",
    pollUrl: `/internal/host-agent/operations/${operationId}`,
    reusedExistingOperation: false,
  }
}

function operationResponse({
  status = "running",
  currentStep = "remove-matrix-route",
  terminal = false,
  succeeded = false,
  lastError = null,
}: {
  status?: string
  currentStep?: string
  terminal?: boolean
  succeeded?: boolean
  lastError?: string | null
} = {}) {
  return {
    operationId,
    runtimeStackId: stack.stackId,
    status,
    currentStep,
    requestedAtUtc: "2026-08-16T02:00:00Z",
    startedAtUtc: "2026-08-16T02:00:00Z",
    completedAtUtc: terminal ? "2026-08-16T02:00:03Z" : null,
    lastError,
    terminal,
    succeeded,
  }
}

function trackedDestroy() {
  return {
    operationId,
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    request: {
      removeContainers: true,
      removeRoutes: true,
      removeDatabase: false,
      removeFiles: false,
      force: false,
      idempotencyKey: `destroy-stack-${stack.stackId}-stable-test-key`,
    },
  }
}

afterEach(() => {
  window.localStorage.clear()
})

describe("StacksListPage", () => {
  it("renders a compact operator inventory in German without exposing internal stack ids", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(listHandler())

    renderPage()

    expect(await screen.findByRole("heading", { name: "Chatserver" })).toBeInTheDocument()
    await screen.findByText("Cool Family Chat")
    expect(screen.getByText(stack.slug)).toBeInTheDocument()
    expect(screen.getByText("Family")).toBeInTheDocument()
    expect(document.querySelector(`img[src="${stack.logoUrl}"]`)).toBeInTheDocument()
    expect(screen.getByText("Matrix- und Element-Stacks über die lokale MEM-Steuerungsoberfläche erstellen und verwalten.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Stack erstellen" })).toHaveAttribute("href", "/stacks/new")
    expect(screen.getByText("1 Chatserver")).toBeInTheDocument()
    expect(screen.getByText("1 fehlerfrei")).toBeInTheDocument()
    expect(screen.getByText("Fehlerfrei", { selector: "span" })).toBeInTheDocument()
    expect(screen.getByText("Öffentliche Routen verifiziert")).toBeInTheDocument()
    const matrixHost = screen.getByText("matrix-cool-stack.deltabox.dev")
    const elementHost = screen.getByText("chat-cool-stack.deltabox.dev")
    expect(matrixHost).toBeInTheDocument()
    expect(elementHost).toBeInTheDocument()
    expect(matrixHost.closest("div.min-w-0")).toHaveClass("md:col-start-1", "md:row-start-2")
    expect(elementHost.closest("div.min-w-0")).toHaveClass("md:col-start-2", "md:row-start-2")
    expect(screen.queryByText(stack.stackId)).not.toBeInTheDocument()

    const matrixHostLink = screen.getByRole("link", {
      name: `Matrix-Host: matrix-cool-stack.deltabox.dev`,
    })
    expect(matrixHostLink).toHaveAttribute("href", stack.matrixPublicBaseUrl)
    expect(matrixHostLink).toHaveAttribute("target", "_blank")
    expect(matrixHostLink).toHaveAttribute("rel", "noopener noreferrer")

    const elementHostLink = screen.getByRole("link", {
      name: `Element-Host: chat-cool-stack.deltabox.dev`,
    })
    expect(elementHostLink).toHaveAttribute("href", stack.elementPublicBaseUrl)
    expect(elementHostLink).toHaveAttribute("target", "_blank")
    expect(elementHostLink).toHaveAttribute("rel", "noopener noreferrer")

    const manageLink = screen.getByRole("link", { name: "Verwalten" })
    expect(manageLink).toHaveAttribute("href", "/stacks/cool-stack-restored")
    expect(manageLink.parentElement).toHaveClass("md:col-start-2", "md:row-start-1")
    expect(screen.queryByRole("link", { name: "Diagnose" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Element öffnen" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Löschen" })).not.toBeInTheDocument()
  })

  it("shows stale successful verification as operator attention rather than current health", async () => {
    server.use(
      listHandler([
        {
          ...stack,
          health: "needs_attention",
          verificationFreshness: "stale",
          lastVerifiedStatus: "Public Routes Verified",
          lastVerifiedAtUtc: "2026-08-01T12:00:00Z",
        },
      ]),
    )

    renderPage()

    expect(await screen.findByText("1 chat server")).toBeInTheDocument()
    expect(screen.getByText("0 healthy")).toBeInTheDocument()
    expect(screen.getByText("1 needs attention")).toBeInTheDocument()
    expect(screen.getByText("Needs attention", { selector: "span" })).toBeInTheDocument()
    expect(screen.getByText("Verification is stale — run Doctor to refresh it.")).toBeInTheDocument()
    expect(screen.queryByText("Public routes verified")).not.toBeInTheDocument()
  })

  it("classifies recorded list status conservatively for operator summary", async () => {
    server.use(
      listHandler([
        stack,
        {
          ...stack,
          stackId: "11111111-1111-1111-1111-111111111111",
          slug: "starting-stack",
          lastVerifiedStatus: "Started",
        },
        {
          ...stack,
          stackId: "22222222-2222-2222-2222-222222222222",
          slug: "failed-stack",
          lastVerifiedStatus: "Failed",
        },
        {
          ...stack,
          stackId: "33333333-3333-3333-3333-333333333333",
          slug: "offline-stack",
          lastVerifiedStatus: "Offline",
        },
      ]),
    )

    renderPage()

    expect(await screen.findByText("4 chat servers")).toBeInTheDocument()
    expect(screen.getByText("1 healthy")).toBeInTheDocument()
    expect(screen.getByText("1 needs attention")).toBeInTheDocument()
    expect(screen.getByText("1 offline")).toBeInTheDocument()
    expect(screen.getByText("1 other")).toBeInTheDocument()
    expect(screen.getByText("Setting up", { selector: "span" })).toBeInTheDocument()
  })


  it("keeps inventory navigation in the URL and renders server-wide totals and paging", async () => {
    const requestedUrls: URL[] = []
    server.use(
      http.get(stacksEndpoint, ({ request }) => {
        const url = new URL(request.url)
        requestedUrls.push(url)
        const page = url.searchParams.get("page") === "2" ? 2 : 1
        const pageSize = Number(url.searchParams.get("pageSize") ?? "10")
        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stacks: page === 2 ? [stack, { ...stack, stackId: "11111111-1111-1111-1111-111111111111", slug: "second-stack" }] : [stack],
          detail: null,
          summary: { totalStacks: 12, healthy: 11, needsAttention: 1, offline: 0, settingUp: 0, unknown: 0 },
          categories: [
            { category: "Family", count: 7 },
            { category: "School", count: 5 },
          ],
          totalMatchingStacks: 12,
          page,
          pageSize,
          totalPages: Math.ceil(12 / pageSize),
          hasPreviousPage: page > 1,
          hasNextPage: page < Math.ceil(12 / pageSize),
          isPaged: true,
        })
      }),
    )

    const user = userEvent.setup()
    renderPage("/stacks?page=2&status=needs_attention&category=Family&sort=last-checked-desc")

    expect(await screen.findByText("12 chat servers")).toBeInTheDocument()
    expect(screen.getByText("11 healthy")).toBeInTheDocument()
    expect(screen.getByLabelText("Filter chat servers by status")).toHaveValue("needs_attention")
    expect(screen.getByLabelText("Filter chat servers by category")).toHaveValue("Family")
    expect(screen.getByRole("option", { name: "Family (7)" })).toBeInTheDocument()
    expect(screen.getByRole("option", { name: "School (5)" })).toBeInTheDocument()
    expect(screen.getByLabelText("Sort chat servers")).toHaveValue("last-checked-desc")
    expect(screen.getByText("Showing 11–12 of 12 chat servers")).toBeInTheDocument()

    await waitFor(() => {
      const latest = requestedUrls.at(-1)
      expect(latest?.searchParams.get("page")).toBe("2")
      expect(latest?.searchParams.get("pageSize")).toBe("10")
      expect(latest?.searchParams.get("status")).toBe("needs_attention")
      expect(latest?.searchParams.get("category")).toBe("Family")
      expect(latest?.searchParams.get("sortBy")).toBe("lastChecked")
      expect(latest?.searchParams.get("sortDirection")).toBe("desc")
    })

    await user.click(screen.getByRole("button", { name: "Previous" }))
    await waitFor(() => expect(screen.getByTestId("location-search")).not.toHaveTextContent("page=2"))

    const search = screen.getByLabelText("Search chat servers")
    await user.type(search, "school")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("search=school"))

    const categoryFilter = screen.getByLabelText("Filter chat servers by category")
    await waitFor(() => expect(categoryFilter).toBeEnabled())
    await user.selectOptions(categoryFilter, "School")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("category=School"))

    const rowsPerPage = screen.getByLabelText("Rows per page")
    await waitFor(() => expect(rowsPerPage).toBeEnabled())
    await user.selectOptions(rowsPerPage, "25")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("pageSize=25"))

    await user.click(screen.getByRole("button", { name: "Clear filters" }))
    await waitFor(() => {
      const params = new URLSearchParams(screen.getByTestId("location-search").textContent?.slice(1) ?? "")
      expect(params.get("search")).toBeNull()
      expect(params.get("status")).toBeNull()
      expect(params.get("category")).toBeNull()
      expect(params.get("sort")).toBeNull()
      expect(params.get("pageSize")).toBe("25")
    })
  })

  it("distinguishes filtered-empty results from an actually empty inventory", async () => {
    server.use(
      http.get(stacksEndpoint, () => HttpResponse.json({
        source: "control-plane",
        status: "ok",
        stacks: [],
        detail: "No runtime stacks match the current inventory filters.",
        summary: { totalStacks: 3, healthy: 2, needsAttention: 1, offline: 0, settingUp: 0, unknown: 0 },
        categories: [{ category: "Family", count: 2 }],
        totalMatchingStacks: 0,
        page: 1,
        pageSize: 10,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
        isPaged: true,
      })),
    )

    renderPage("/stacks?search=does-not-exist")

    expect(await screen.findByText("No chat servers match these filters")).toBeInTheDocument()
    expect(screen.queryByText("No chat servers yet")).not.toBeInTheDocument()
    expect(screen.getByText("3 chat servers")).toBeInTheDocument()
  })

  it("restores durable destroy tracking after refresh without resubmitting", async () => {
    window.localStorage.setItem(trackingStorageKey, JSON.stringify(trackedDestroy()))
    let destroyPosts = 0

    server.use(
      listHandler(),
      http.post(`${stacksEndpoint}/${stack.slug}/destroy`, () => {
        destroyPosts += 1
        return HttpResponse.json(acceptedResponse(), { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${operationId}`, () =>
        HttpResponse.json(operationResponse({ currentStep: "remove-element-container" })),
      ),
    )

    renderPage()

    const dialog = await screen.findByRole("alertdialog")
    await waitFor(() => {
      expect(dialog).toHaveTextContent("Removing Element container")
    })
    expect(dialog).toHaveTextContent(operationId)
    expect(destroyPosts).toBe(0)
  })

  it("shows terminal destroy failure without offering an unsafe automatic retry", async () => {
    window.localStorage.setItem(trackingStorageKey, JSON.stringify(trackedDestroy()))

    server.use(
      listHandler(),
      http.get(`/internal/host-agent/operations/${operationId}`, () =>
        HttpResponse.json(operationResponse({
          status: "failed",
          currentStep: "remove-element-container",
          terminal: true,
          succeeded: false,
          lastError: "container removal failed",
        })),
      ),
    )

    renderPage()

    expect(await screen.findByText("Chat server removal failed")).toBeInTheDocument()
    expect(screen.getByText("Failed at: Removing Element container")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
    expect(screen.queryByRole("button", { name: /retry/i })).not.toBeInTheDocument()
  })


})
