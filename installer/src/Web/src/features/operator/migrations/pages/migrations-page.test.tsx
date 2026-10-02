import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import type {
  MigrationSessionInventoryResponse,
  MigrationSessionInventoryRow,
} from "@/features/operator/migrations/api/migration-sessions"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"

import { MigrationsPage } from "./migrations-page"

const endpoint = "/api/operator/migrations/sessions/inventory"

function inventoryRow(
  overrides: Partial<MigrationSessionInventoryRow> & Pick<MigrationSessionInventoryRow, "migrationId" | "displayName">,
): MigrationSessionInventoryRow {
  const { migrationId, displayName, ...rest } = overrides

  return {
    migrationId,
    displayName,
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    sourceProduct: "Message Easy Mode",
    sourceVersion: "0.1.0",
    targetStackSlug: "tester",
    lifecycleStatus: "active",
    archived: false,
    currentStageCode: "review-old-server",
    currentStageState: "ready",
    currentStatusCode: "migration.workspace.source-ready",
    needsAttention: false,
    warningCount: 0,
    errorCount: 0,
    createdAtUtc: "2026-07-28T00:00:00Z",
    updatedAtUtc: "2026-07-28T01:00:00Z",
    stateVersion: 1,
    primaryAction: { kind: "continue", code: "start-conversion" },
    capabilities: {
      canOpen: true,
      canArchive: false,
      canUnarchive: false,
      canCancel: false,
      canDelete: false,
      deleteBlockedReason: null,
    },
    ...rest,
  }
}

const inventoryRows: MigrationSessionInventoryRow[] = [
  inventoryRow({
    migrationId: "mig_completed_01",
    displayName: "Completed MatrixEasyHost migration",
    lifecycleStatus: "completed",
    currentStageCode: "finish-migration",
    currentStageState: "completed",
    currentStatusCode: "migration.workspace.completed",
    updatedAtUtc: "2026-07-28T13:00:00Z",
    primaryAction: { kind: "view", code: "open-baseline-backup" },
    capabilities: {
      canOpen: true,
      canArchive: true,
      canUnarchive: false,
      canCancel: false,
      canDelete: false,
      deleteBlockedReason: "Accepted migrations cannot be deleted.",
    },
  }),
  inventoryRow({
    migrationId: "mig_cancelled_delete_01",
    displayName: "Disposable cancelled migration",
    lifecycleStatus: "cancelled",
    currentStageCode: "create-and-upload-package",
    currentStageState: "closed",
    currentStatusCode: "migration.workspace.cancelled",
    updatedAtUtc: "2026-07-28T14:00:00Z",
    primaryAction: { kind: "view", code: "open-migration" },
    capabilities: {
      canOpen: true,
      canArchive: true,
      canUnarchive: false,
      canCancel: false,
      canDelete: true,
      deleteBlockedReason: null,
    },
  }),
  inventoryRow({
    migrationId: "mig_review_01",
    displayName: "Blocked migration",
    currentStageState: "blocked",
    currentStatusCode: "migration.workspace.source-blocked",
    needsAttention: true,
    warningCount: 2,
    errorCount: 1,
    updatedAtUtc: "2026-07-28T12:00:00Z",
    primaryAction: { kind: "review", code: "review-source-issues" },
  }),
  ...Array.from({ length: 11 }, (_, index) => inventoryRow({
    migrationId: `mig_continue_${String(index + 1).padStart(2, "0")}`,
    displayName: index === 0 ? "David's server migration" : `Ready migration ${index + 1}`,
    targetStackSlug: index % 2 === 0 ? "tester" : "production",
    currentStageCode: index % 2 === 0 ? "review-old-server" : "prepare-and-test",
    currentStageState: "ready",
    currentStatusCode: index % 2 === 0
      ? "migration.workspace.source-ready"
      : "migration.workspace.private-test-ready",
    updatedAtUtc: `2026-07-28T${String(11 - index).padStart(2, "0")}:00:00Z`,
    primaryAction: index % 2 === 0
      ? { kind: "continue", code: "start-conversion" }
      : { kind: "continue", code: "confirm-tested-data" },
    ...(index === 0
      ? {
          capabilities: {
            canOpen: true,
            canArchive: false,
            canUnarchive: false,
            canCancel: true,
            canDelete: false,
            deleteBlockedReason: "Active migrations cannot be deleted.",
          },
        }
      : {}),
  })),
  inventoryRow({
    migrationId: "mig_archived_01",
    displayName: "Archived migration",
    lifecycleStatus: "completed",
    archived: true,
    currentStageCode: "finish-migration",
    currentStageState: "completed",
    currentStatusCode: "migration.workspace.completed",
    updatedAtUtc: "2026-07-01T00:00:00Z",
    primaryAction: { kind: "view", code: "open-baseline-backup" },
    capabilities: {
      canOpen: true,
      canArchive: false,
      canUnarchive: true,
      canCancel: false,
      canDelete: false,
      deleteBlockedReason: "Accepted migrations cannot be deleted.",
    },
  }),
]

function inventoryHandler(rows = inventoryRows) {
  return http.get(endpoint, ({ request }) => {
    const url = new URL(request.url)
    const page = positiveInteger(url.searchParams.get("page"), 1)
    const pageSize = positiveInteger(url.searchParams.get("pageSize"), 10)
    const search = url.searchParams.get("search")?.trim().toLowerCase() ?? ""
    const lifecycle = url.searchParams.get("lifecycle") ?? "all"
    const action = url.searchParams.get("action") ?? "all"
    const stage = url.searchParams.get("stage")
    const targetStack = url.searchParams.get("targetStack")
    const sortBy = url.searchParams.get("sortBy") ?? "updated"
    const sortDirection = url.searchParams.get("sortDirection") === "asc" ? "asc" : "desc"
    const includeArchived = lifecycle === "archived" || url.searchParams.get("includeArchived") === "true"

    const operational = rows.filter((row) => {
      if (!includeArchived && row.archived) return false
      if (search && ![
        row.displayName,
        row.migrationId,
        row.sourceDisplay,
        row.sourceProduct,
        row.sourceVersion,
        row.targetStackSlug,
      ].some((value) => value?.toLowerCase().includes(search))) return false
      if (stage && row.currentStageCode !== stage) return false
      if (targetStack && row.targetStackSlug !== targetStack) return false
      return true
    })

    const summary = {
      totalSessions: operational.length,
      activeCount: operational.filter((row) => row.lifecycleStatus === "active" && !row.archived).length,
      needsActionCount: operational.filter((row) => row.primaryAction.kind === "review").length,
      completedCount: operational.filter((row) => row.lifecycleStatus === "completed" && !row.archived).length,
      closedCount: operational.filter((row) => row.lifecycleStatus === "closed" && !row.archived).length,
      cancelledCount: operational.filter((row) => row.lifecycleStatus === "cancelled" && !row.archived).length,
      archivedCount: operational.filter((row) => row.archived).length,
    }

    const filtered = operational.filter((row) => {
      if (lifecycle === "archived" && !row.archived) return false
      if (lifecycle !== "all" && lifecycle !== "archived" && (row.archived || row.lifecycleStatus !== lifecycle)) return false
      if (action !== "all" && row.primaryAction.kind !== action) return false
      return true
    })

    const sorted = [...filtered].sort((left, right) => {
      const leftValue = sortValue(left, sortBy)
      const rightValue = sortValue(right, sortBy)
      const comparison = leftValue.localeCompare(rightValue)
      return sortDirection === "asc" ? comparison : -comparison
    })
    const totalPages = sorted.length === 0 ? 0 : Math.ceil(sorted.length / pageSize)
    const normalizedPage = totalPages === 0 ? 1 : Math.min(page, totalPages)
    const sessions = sorted.slice((normalizedPage - 1) * pageSize, normalizedPage * pageSize)

    const response: MigrationSessionInventoryResponse = {
      schemaVersion: 1,
      query: {
        search: search || null,
        lifecycle,
        action,
        stage,
        targetStack,
        sortBy,
        sortDirection,
        includeArchived,
      },
      summary,
      totalSessions: sorted.length,
      page: normalizedPage,
      pageSize,
      totalPages,
      hasPreviousPage: normalizedPage > 1,
      hasNextPage: normalizedPage < totalPages,
      targetStacks: [...new Set(operational.flatMap((row) => row.targetStackSlug ? [row.targetStackSlug] : []))].sort(),
      sessions,
      warnings: [],
    }

    return HttpResponse.json(response)
  })
}

function positiveInteger(value: string | null, fallback: number) {
  const parsed = Number.parseInt(value ?? "", 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function sortValue(row: MigrationSessionInventoryRow, sortBy: string) {
  switch (sortBy) {
    case "created": return row.createdAtUtc
    case "name": return row.displayName.toLowerCase()
    case "stage": return row.currentStageCode
    default: return row.updatedAtUtc
  }
}

function LocationProbe() {
  const location = useLocation()
  return <output aria-label="Current migration inventory query">{location.search}</output>
}

function renderPage(initialEntry = "/migrations") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route
          path="/migrations"
          element={<><MigrationsPage /><LocationProbe /></>}
        />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("MigrationsPage", () => {
  it("shows the confirmed cancellation notice without changing server-authored counts", async () => {
    server.use(inventoryHandler())
    renderWithProviders(<MemoryRouter initialEntries={[{ pathname: "/migrations", state: {
      cancelledMigrationName: "Accidental migration",
    } }]}><MigrationsPage /></MemoryRouter>)
    expect(await screen.findByText("Migration cancelled")).toBeInTheDocument()
    expect(screen.getByText(/“Accidental migration” is closed and retained in migration history/)).toBeInTheDocument()
    expect(await screen.findByText("Disposable cancelled migration")).toBeInTheDocument()
  })

  it("renders a readable three-column inventory with one set of server-authored actions", async () => {
    const user = userEvent.setup()
    server.use(inventoryHandler())
    renderPage()

    expect(await screen.findByRole("heading", { name: "Migrations" })).toBeInTheDocument()
    expect(await screen.findByText("David's server migration")).toBeInTheDocument()
    expect(screen.getByText("Completed MatrixEasyHost migration")).toBeInTheDocument()
    expect(screen.getByText("Blocked migration")).toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Continue" }).length).toBeGreaterThan(0)
    expect(screen.getByRole("link", { name: "Review" })).toHaveAttribute("href", "/migrations/mig_review_01")

    const completedRow = screen.getByText("Completed MatrixEasyHost migration").closest("tr")
    expect(completedRow).not.toBeNull()
    expect(within(completedRow!).getByRole("link", { name: "View" })).toHaveAttribute(
      "href",
      "/migrations/mig_completed_01",
    )
    expect(screen.queryByText("mig_continue_01")).not.toBeInTheDocument()

    const actionsHeader = screen.getByRole("columnheader", { name: "Actions" })
    expect(screen.getAllByRole("columnheader").map((heading) => heading.textContent)).toEqual([
      "Migration", "Progress / next action", "Actions",
    ])
    expect(actionsHeader).not.toHaveClass("sticky")
    expect(screen.getByRole("link", { name: "Review" }).closest("td")).toHaveClass("migration-inventory-actions")
    expect(within(completedRow!).getAllByRole("link", { name: "View" })).toHaveLength(1)
    expect(screen.getByRole("table")).toHaveClass("migration-inventory-table")
    expect(screen.getByRole("link", { name: "Start migration" })).toHaveAttribute("href", "/migrations/new")

    await user.click(
      within(completedRow!).getByRole("button", { name: "More migration actions" }),
    )
    expect(await screen.findByRole("menuitem", { name: "Archive migration" })).toBeInTheDocument()
    await user.keyboard("{Escape}")

    const disposableRow = screen.getByText("Disposable cancelled migration").closest("tr")
    expect(disposableRow).not.toBeNull()
    await user.click(
      within(disposableRow!).getByRole("button", { name: "More migration actions" }),
    )
    expect(await screen.findByRole("menuitem", { name: "Delete permanently" })).toBeInTheDocument()
    await user.keyboard("{Escape}")

    const cancellableRow = screen.getByText("David's server migration").closest("tr")
    expect(cancellableRow).not.toBeNull()
    await user.click(
      within(cancellableRow!).getByRole("button", { name: "More migration actions" }),
    )
    expect(await screen.findByRole("menuitem", { name: "Cancel migration" })).toBeInTheDocument()
  })

  it("keeps search, filters, sorting, and pagination in the URL and resets the page after search", async () => {
    const user = userEvent.setup()
    server.use(inventoryHandler())
    renderPage("/migrations?page=2")

    const location = await screen.findByLabelText("Current migration inventory query")
    await waitFor(() => expect(location).toHaveTextContent("page=2"))

    const search = screen.getByRole("textbox", { name: "Search migration sessions" })
    await user.clear(search)
    await user.type(search, "Blocked")
    await waitFor(() => {
      expect(location.textContent).toContain("search=Blocked")
      expect(location.textContent).not.toContain("page=2")
    })
    expect(await screen.findByText("Blocked migration")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: /^Filters/ }))
    await user.selectOptions(screen.getByRole("combobox", { name: "Filter by current migration stage" }), "review-old-server")
    await user.selectOptions(screen.getByRole("combobox", { name: "Filter by target stack" }), "tester")
    await user.selectOptions(screen.getByRole("combobox", { name: "Sort migration sessions" }), "name")
    await user.click(screen.getByRole("button", { name: "Sort descending" }))

    await waitFor(() => {
      expect(location.textContent).toContain("stage=review-old-server")
      expect(location.textContent).toContain("targetStack=tester")
      expect(location.textContent).toContain("sortBy=name")
      expect(location.textContent).toContain("sortDirection=asc")
    })

    await user.click(screen.getByRole("button", { name: "Clear filters" }))
    await waitFor(() => expect(location.textContent).not.toContain("search="))

    await user.click(screen.getByRole("button", { name: "Next" }))
    await waitFor(() => expect(location.textContent).toContain("page=2"))

    await user.selectOptions(screen.getByRole("combobox", { name: "Rows per page" }), "25")
    await waitFor(() => {
      expect(location.textContent).toContain("pageSize=25")
      expect(location.textContent).not.toContain("page=2")
    })
  })

  it("uses compact server-wide counts and quick filters without filtering only the current page", async () => {
    const user = userEvent.setup()
    server.use(inventoryHandler())
    renderPage()

    const location = await screen.findByLabelText("Current migration inventory query")
    const quickFilters = screen.getByRole("group", { name: "Migration lifecycle quick filters" })

    await user.click(within(quickFilters).getByRole("button", { name: "Ready to continue" }))
    await waitFor(() => {
      expect(location.textContent).toContain("lifecycle=active")
      expect(location.textContent).toContain("action=continue")
    })
    expect(screen.queryByText("Blocked migration")).not.toBeInTheDocument()

    await user.click(within(quickFilters).getByRole("button", { name: "Needs attention" }))
    await waitFor(() => expect(location.textContent).toContain("action=review"))
    expect(await screen.findByText("Blocked migration")).toBeInTheDocument()

    const sessionsCard = screen.getByRole("button", {
      name: /^Migration sessions: /,
    })
    expect(sessionsCard).toHaveClass("min-h-14", "px-3", "py-2")
    expect(sessionsCard).not.toHaveTextContent("Sessions matching the current search")

    const completedCard = screen.getByRole("button", {
      name: /^Completed: /,
    })
    await user.click(completedCard)
    await waitFor(() => expect(location.textContent).toContain("lifecycle=completed"))
    expect(await screen.findByText("Completed MatrixEasyHost migration")).toBeInTheDocument()

    await user.click(within(quickFilters).getByRole("button", { name: "Archived" }))
    await waitFor(() => {
      expect(location.textContent).toContain("lifecycle=archived")
      expect(location.textContent).toContain("includeArchived=true")
    })
    expect(await screen.findByText("Archived migration")).toBeInTheDocument()
  })

  it("distinguishes a genuinely empty inventory from an empty filtered view", async () => {
    server.use(inventoryHandler([]))
    const first = renderPage()
    expect(await screen.findByRole("heading", { name: "No migration sessions yet" })).toBeInTheDocument()
    first.unmount()

    server.use(inventoryHandler(inventoryRows))
    renderPage("/migrations?search=missing")
    const filteredEmptyHeading = await screen.findByRole("heading", {
      name: "No migration sessions match this view",
    })
    expect(filteredEmptyHeading).toBeInTheDocument()

    const filteredEmptyPanel = filteredEmptyHeading.parentElement
    expect(filteredEmptyPanel).not.toBeNull()
    expect(within(filteredEmptyPanel!).getByRole("button", { name: "Clear filters" })).toBeInTheDocument()
  })

  it("keeps the inventory shell visible when the API fails", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json({ code: "unavailable" }, { status: 503 })))
    renderPage()

    expect(await screen.findByRole("heading", { name: "Migration sessions could not be loaded" })).toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: "No migration sessions yet" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Start migration" })).toBeInTheDocument()
  })

  it("renders the new inventory controls and actions in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(inventoryHandler([inventoryRows.find((row) => row.migrationId === "mig_continue_01")!]))
    renderPage()

    expect(await screen.findByText("David's server migration")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Fortfahren" })).toBeInTheDocument()
    expect(screen.getByRole("textbox", { name: "Migrationssitzungen durchsuchen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Bereit zum Fortfahren" })).toBeInTheDocument()
  })

  it("does not offer Continue copy for a cancelled history row", async () => {
    const cancelled = inventoryRows.find((row) => row.lifecycleStatus === "cancelled")!
    const before = structuredClone(cancelled)
    server.use(inventoryHandler([cancelled]))
    renderPage()
    const row = (await screen.findByText(cancelled.displayName)).closest("tr")!
    expect(within(row).getByRole("link", { name: "View" })).toHaveAttribute("href", `/migrations/${cancelled.migrationId}`)
    expect(within(row).getByText("Migration cancelled")).toBeInTheDocument()
    expect(within(row).queryByText("Create and upload package")).not.toBeInTheDocument()
    expect(within(row).getByText("View migration details")).toBeInTheDocument()
    expect(within(row).queryByText("Continue")).not.toBeInTheDocument()
    expect(cancelled).toEqual(before)
  })

  it("keeps detailed filters collapsed by default without hiding search or quick filters", async () => {
    server.use(inventoryHandler())
    renderPage()
    await screen.findByText("David's server migration")
    expect(screen.getByRole("button", { name: "Filters" })).toHaveAttribute("aria-expanded", "false")
    expect(screen.queryByRole("combobox", { name: "Filter by current migration stage" })).not.toBeInTheDocument()
    expect(screen.getByRole("textbox", { name: "Search migration sessions" })).toBeInTheDocument()
    expect(screen.getByRole("group", { name: "Migration lifecycle quick filters" })).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole("button", { name: "Filters" }))
    expect(screen.getByRole("combobox", { name: "Filter by current migration stage" })).toBeVisible()
    expect(screen.getByLabelText("Current migration inventory query").textContent).toBe("")
  })

  it("reveals deep-linked filters and preserves their URL and values while the panel is collapsed", async () => {
    server.use(inventoryHandler())
    renderPage("/migrations?stage=review-old-server&targetStack=tester&sortBy=name&includeArchived=true")
    await screen.findByText("David's server migration")
    const user = userEvent.setup()
    const query = screen.getByLabelText("Current migration inventory query").textContent
    expect(screen.getByRole("button", { name: /^Filters/ })).toHaveAttribute("aria-expanded", "true")
    await user.click(screen.getByRole("button", { name: /^Filters/ }))
    expect(screen.queryByRole("combobox", { name: "Sort migration sessions" })).not.toBeInTheDocument()
    expect(screen.getByLabelText("Current migration inventory query").textContent).toBe(query)
    await user.click(screen.getByRole("button", { name: /^Filters/ }))
    expect(screen.getByRole("combobox", { name: "Sort migration sessions" })).toHaveValue("name")
    expect(screen.getByRole("combobox", { name: "Filter by target stack" })).toHaveValue("tester")
    expect(screen.getByRole("checkbox", { name: "Include archived history" })).toBeChecked()
  })

  it("shows unknown counts while loading instead of claiming an empty inventory", async () => {
    server.use(inventoryHandler())
    renderPage()
    expect(screen.getByRole("button", { name: "Migration sessions: —" })).toBeDisabled()
    await screen.findByText("David's server migration")
    expect(screen.getByRole("button", { name: "Migration sessions: 14" })).toBeEnabled()
    // There are 14 matching sessions, although the first server page has only 10.
    expect(screen.getByRole("table").querySelectorAll("tbody tr")).toHaveLength(10)
  })

  it("retains complete long identifiers and a single action set in the German inventory", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const row = inventoryRow({ migrationId: "long-identity", displayName: "Familienmigration-" + "Lang".repeat(40),
      sourceDisplay: "matrix-" + "a".repeat(80) + ".example.test", targetStackSlug: "target-" + "b".repeat(80) })
    server.use(inventoryHandler([row]))
    renderPage()
    const rendered = (await screen.findByText(row.displayName)).closest("tr")!
    expect(within(rendered).getByText(row.sourceDisplay)).toBeInTheDocument()
    expect(within(rendered).getByRole("link", { name: row.targetStackSlug! })).toHaveAttribute("href", `/stacks/${row.targetStackSlug}`)
    expect(within(rendered).getAllByRole("link", { name: "Fortfahren" })).toHaveLength(1)
    expect(screen.getByRole("button", { name: "Filter" })).toHaveAttribute("aria-expanded", "false")
    expect(screen.getByRole("columnheader", { name: "Fortschritt / nächste Aktion" })).toBeInTheDocument()
  })

})
