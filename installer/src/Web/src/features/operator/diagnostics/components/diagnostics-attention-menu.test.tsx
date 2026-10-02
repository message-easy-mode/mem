import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { DiagnosticsAttentionResponse } from "@/features/operator/diagnostics/api/diagnostics.types"

import {
  DiagnosticsAttentionMenu,
  diagnosticsAttentionPollingInterval,
} from "./diagnostics-attention-menu"

const emptyResponse: DiagnosticsAttentionResponse = {
  schemaVersion: 1,
  observedAtUtc: new Date().toISOString(),
  state: "ready",
  total: 0,
  highestSeverity: null,
  items: [],
  partial: false,
  warnings: [],
}

function renderMenu() {
  return renderWithProviders(
    <MemoryRouter>
      <DiagnosticsAttentionMenu />
    </MemoryRouter>,
  )
}

function attentionResponse(
  overrides: Partial<DiagnosticsAttentionResponse> = {},
): DiagnosticsAttentionResponse {
  return {
    ...emptyResponse,
    ...overrides,
  }
}

afterEach(() => {
  window.localStorage.clear()
})

describe("DiagnosticsAttentionMenu", () => {
  it("keeps an all-clear result neutral and links to the incident workspace", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(emptyResponse)),
    )

    renderMenu()

    const trigger = await screen.findByRole("button", {
      name: "No diagnostic incidents need attention",
    })
    expect(trigger).toHaveAttribute("data-attention-state", "ready")

    await user.click(trigger)
    expect(screen.getByText("No incidents need attention")).toBeInTheDocument()
    expect(screen.getByRole("menuitem", { name: "View all incidents" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
  })

  it("shows warning attention, exact incident links, a bounded count, and closes with Escape", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(attentionResponse({
          state: "warning",
          total: 2,
          highestSeverity: "warning",
          items: [
            {
              incidentId: "inc_warning_1",
              severity: "warning",
              eventCode: "migration.verification_failed",
              feature: "migration",
              stage: "verification",
              summary: "Production verification needs review.",
              lastSeenAtUtc: new Date(Date.now() - 60_000).toISOString(),
              href: "/diagnostics/logs?incident=inc_warning_1",
            },
          ],
        }))),
    )

    renderMenu()

    const trigger = await screen.findByRole("button", {
      name: "2 diagnostic incidents need attention",
    })
    expect(trigger).toHaveAttribute("data-attention-state", "warning")

    await user.click(trigger)
    const item = screen.getByRole("menuitem", {
      name: /Production verification needs review/i,
    })
    expect(item).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_warning_1",
    )
    expect(screen.getByText("1 additional incident is available in Diagnostics.")).toBeInTheDocument()

    await user.keyboard("{Escape}")
    expect(screen.queryByRole("menu")).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it("never renders more than five recent attention items", async () => {
    const user = userEvent.setup()
    const items = Array.from({ length: 7 }, (_, index) => ({
      incidentId: `inc_bounded_${index}`,
      severity: "warning",
      eventCode: "runtime.warning",
      feature: "runtime",
      stage: null,
      summary: `Bounded incident ${index + 1}`,
      lastSeenAtUtc: new Date(Date.now() - index * 60_000).toISOString(),
      href: `/diagnostics/logs?incident=inc_bounded_${index}`,
    }))
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(attentionResponse({
          state: "warning",
          total: 7,
          highestSeverity: "warning",
          items,
        }))),
    )

    renderMenu()
    await user.click(await screen.findByRole("button", {
      name: "7 diagnostic incidents need attention",
    }))

    expect(screen.getAllByText(/Bounded incident/)).toHaveLength(5)
    expect(screen.getByText("2 additional incidents are available in Diagnostics.")).toBeInTheDocument()
  })

  it("preserves the last successful result when a refresh fails", async () => {
    let fail = false
    server.use(
      http.get("/api/operator/diagnostics/attention", () => {
        if (fail) {
          return HttpResponse.json({ title: "Unavailable" }, { status: 503 })
        }

        return HttpResponse.json(attentionResponse({
          state: "warning",
          total: 1,
          highestSeverity: "warning",
          items: [],
        }))
      }),
    )

    const { queryClient } = renderMenu()
    const trigger = await screen.findByRole("button", {
      name: "1 diagnostic incident needs attention",
    })

    fail = true
    await queryClient.invalidateQueries({ queryKey: ["diagnostics", "attention"] })

    await waitFor(() => {
      expect(trigger).toHaveAttribute("data-attention-stale", "true")
    })
    expect(trigger).toHaveAttribute("data-attention-state", "warning")
  })

  it("uses an urgent state for error or critical incidents", async () => {
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(attentionResponse({
          state: "attention",
          total: 1,
          highestSeverity: "critical",
          items: [],
        }))),
    )

    renderMenu()

    const trigger = await screen.findByRole("button", {
      name: "1 diagnostic incident requires urgent review",
    })
    expect(trigger).toHaveAttribute("data-attention-state", "attention")
    expect(within(trigger).getByText("1")).toHaveClass(
      "bg-destructive",
      "text-white",
      "ring-2",
    )
  })

  it("does not present an unavailable endpoint as all clear", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json({ title: "Unavailable" }, { status: 503 })),
    )

    renderMenu()

    const trigger = await screen.findByRole("button", {
      name: "Diagnostic attention is unavailable",
    })
    expect(trigger).toHaveAttribute("data-attention-state", "unavailable")

    await user.click(trigger)
    expect(screen.getByText("Attention status unavailable")).toBeInTheDocument()
  })

  it("renders an unsafe server href as non-navigation text", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(attentionResponse({
          state: "warning",
          total: 1,
          highestSeverity: "warning",
          items: [
            {
              incidentId: "inc_unsafe",
              severity: "warning",
              eventCode: "runtime.warning",
              feature: "runtime",
              stage: null,
              summary: "Unsafe link was rejected.",
              lastSeenAtUtc: new Date().toISOString(),
              href: "https://untrusted.example.test/incident",
            },
          ],
        }))),
    )

    renderMenu()
    await user.click(await screen.findByRole("button", {
      name: "1 diagnostic incident needs attention",
    }))

    const summary = screen.getByText("Unsafe link was rejected.")
    expect(summary.closest("a")).toBeNull()
  })

  it("renders the attention surface in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json(attentionResponse({
          state: "warning",
          total: 1,
          highestSeverity: "warning",
          items: [],
        }))),
    )

    renderMenu()

    const trigger = await screen.findByRole("button", {
      name: "1 Diagnosevorfall benötigt Aufmerksamkeit",
    })
    await user.click(trigger)
    expect(screen.getByText("Aufmerksamkeit erforderlich")).toBeInTheDocument()
    expect(screen.getByRole("menuitem", { name: "Alle Vorfälle anzeigen" })).toBeInTheDocument()
  })

  it("polls only while the document is visible", () => {
    expect(diagnosticsAttentionPollingInterval(true)).toBe(60_000)
    expect(diagnosticsAttentionPollingInterval(false)).toBe(false)
  })
})
