import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"
import type {
  RestoreAttemptListItem,
  RestoreAttemptListResponse,
} from "@/features/operator/backups/api/types/backups.types"

import { BackupRestoreSessionsPage } from "."

const startedAtUtc = "2026-07-01T00:00:00Z"
const updatedAtUtc = "2026-07-01T01:00:00Z"

const attempts: RestoreAttemptListItem[] = [
  {
    restoreSessionId: "restore-running",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Demo stack backup",
    sourceDeleted: false,
    catalogEntryId: "catalog-demo",
    sourceStackSlug: "demo-stack",
    sourceBackupId: "backup-demo",
    targetStackSlug: "cool-stack",
    status: "recreating",
    statusLabel: "Creating restored server",
    currentStage: "create-restored-chat-server",
    currentStageLabel: "Creating restored server",
    progressPercent: 70,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: null,
    warningCount: 0,
    errorCount: 0,
    productionRecreateStarted: true,
    publiclyVerified: false,
    nextActionCode: "view-progress",
    nextActionTitle: "Open restore",
    detail: "Review restore progress.",
  },
  {
    restoreSessionId: "restore-completed",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Imported community export",
    sourceDeleted: false,
    catalogEntryId: "catalog-imported",
    sourceStackSlug: "community-stack",
    sourceBackupId: "backup-imported",
    targetStackSlug: "community-restored",
    status: "completed",
    statusLabel: "Completed",
    currentStage: "public-verification",
    currentStageLabel: "Public verification",
    progressPercent: 100,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: updatedAtUtc,
    warningCount: 0,
    errorCount: 0,
    productionRecreateStarted: true,
    publiclyVerified: true,
    nextActionCode: "view-restore",
    nextActionTitle: "Open restore",
    detail: "Completed and publicly verified.",
  },
  {
    restoreSessionId: "restore-needs-attention",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Needs attention backup",
    sourceDeleted: false,
    catalogEntryId: "catalog-attention",
    sourceStackSlug: "attention-stack",
    sourceBackupId: "backup-attention",
    targetStackSlug: null,
    status: "needs-attention",
    statusLabel: "Needs attention",
    currentStage: "needs-attention",
    currentStageLabel: "Needs attention",
    progressPercent: null,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: null,
    warningCount: 1,
    errorCount: 1,
    productionRecreateStarted: false,
    publiclyVerified: false,
    nextActionCode: "review-issue",
    nextActionTitle: "Review issue",
    detail: "Review the reported issue.",
  },
  {
    restoreSessionId: "restore-cancelled",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Cancelled backup",
    sourceDeleted: false,
    catalogEntryId: "catalog-cancelled",
    sourceStackSlug: "cancelled-stack",
    sourceBackupId: "backup-cancelled",
    targetStackSlug: null,
    status: "cancelled",
    statusLabel: "Cancelled",
    currentStage: "cancelled",
    currentStageLabel: "Cancelled",
    progressPercent: null,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: updatedAtUtc,
    warningCount: 0,
    errorCount: 0,
    productionRecreateStarted: false,
    publiclyVerified: false,
    nextActionCode: "view-restore",
    nextActionTitle: "Open restore",
    detail: "Cancelled by the operator.",
  },
  {
    restoreSessionId: "restore-failed",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Failed backup",
    sourceDeleted: false,
    catalogEntryId: "catalog-failed",
    sourceStackSlug: "failed-stack",
    sourceBackupId: "backup-failed",
    targetStackSlug: null,
    status: "abandoned",
    statusLabel: "Failed",
    currentStage: "needs-attention",
    currentStageLabel: "Needs attention",
    progressPercent: null,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: updatedAtUtc,
    warningCount: 0,
    errorCount: 1,
    productionRecreateStarted: false,
    publiclyVerified: false,
    nextActionCode: "review-issue",
    nextActionTitle: "Review issue",
    detail: "The restore did not complete.",
  },
  {
    restoreSessionId: "restore-deleted-source",
    workspaceAvailable: true,
    sourceKind: "backup-catalog",
    sourceLabel: "Deleted backup",
    sourceDeleted: true,
    catalogEntryId: null,
    sourceStackSlug: "removed-stack",
    sourceBackupId: "backup-removed",
    targetStackSlug: "removed-restored",
    status: "completed",
    statusLabel: "Completed",
    currentStage: "public-verification",
    currentStageLabel: "Public verification",
    progressPercent: 100,
    startedAtUtc,
    lastUpdatedAtUtc: updatedAtUtc,
    terminalAtUtc: updatedAtUtc,
    warningCount: 0,
    errorCount: 0,
    productionRecreateStarted: true,
    publiclyVerified: true,
    nextActionCode: "view-restore",
    nextActionTitle: "Open restore",
    detail: "Terminal history remains after catalog deletion.",
  },
]

function buildResponse(overrides: Partial<RestoreAttemptListResponse> = {}): RestoreAttemptListResponse {
  return {
    source: "control-plane",
    status: "ok",
    query: {
      page: 1,
      pageSize: 10,
      search: null,
      status: null,
      targetStack: null,
      sortBy: "updated",
      sortDirection: "desc",
    },
    summary: {
      totalSessions: attempts.length,
      productionRecreateCount: 3,
      publiclyVerifiedCount: 2,
      needsActionCount: 2,
    },
    totalSessions: attempts.length,
    page: 1,
    pageSize: 10,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
    targetStacks: ["community-restored", "cool-stack", "removed-restored"],
    sessions: attempts,
    warnings: [],
    detail: null,
    ...overrides,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location-search">{location.search}</output>
}

function renderPage(initialEntry = "/restores") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route
          path="/restores"
          element={
            <>
              <BackupRestoreSessionsPage />
              <LocationProbe />
            </>
          }
        />
        <Route path="/restores/:restoreSessionId" element={<div>Restore workspace</div>} />
        <Route path="/backups" element={<div>Backup Catalog</div>} />
        <Route path="/backups/catalog/:catalogEntryId" element={<div>Backup Catalog detail</div>} />
        <Route path="/stacks/:stackSlug" element={<div>Stack detail</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function registerRestoreAttemptHandler(response = buildResponse()) {
  server.use(
    http.get("/internal/host-agent/backups/restores", ({ request }) => {
      const url = new URL(request.url)
      const page = Number(url.searchParams.get("page") ?? "1")
      const pageSize = Number(url.searchParams.get("pageSize") ?? "10")

      return HttpResponse.json({
        ...response,
        page,
        pageSize,
        query: {
          ...response.query,
          page,
          pageSize,
          search: url.searchParams.get("search"),
          status: url.searchParams.get("status"),
          targetStack: url.searchParams.get("targetStack"),
          sortBy: url.searchParams.get("sortBy") ?? "updated",
          sortDirection: url.searchParams.get("sortDirection") ?? "desc",
        },
      })
    }),
  )
}

describe("BackupRestoreSessionsPage", () => {
  it("renders the restore summary as one compact surface with server-projected metrics", async () => {
    registerRestoreAttemptHandler()
    renderPage()

    const summary = await screen.findByRole("region", { name: "Restore sessions" })

    expect(summary.querySelectorAll('[data-slot="card"]')).toHaveLength(1)
    expect(within(summary).getByText("Production recreates")).toBeInTheDocument()
    expect(within(summary).getByText("Publicly verified")).toBeInTheDocument()
    expect(within(summary).getByText("Needs action")).toBeInTheDocument()

    // The summary surface is present while the restore query is loading, so wait
    // for the authoritative server-projected counts instead of asserting the
    // initial zero placeholders from the first render.
    await waitFor(() => {
      expect(within(summary).getByText(attempts.length.toString())).toBeInTheDocument()
    })
    expect(within(summary).getByText("3")).toBeInTheDocument()
    expect(within(summary).getAllByText("2")).toHaveLength(2)
  })

  it("uses canonical workspace links and only links a surviving catalog source", async () => {
    registerRestoreAttemptHandler()
    renderPage()

    const sessionLink = await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })
    expect(sessionLink).toHaveAttribute("href", "/restores/restore-running")

    const openRestoreLinks = screen.getAllByRole("link", { name: /Open restore/i })
    expect(openRestoreLinks[0]).toHaveAttribute("href", "/restores/restore-running")

    expect(screen.getByRole("link", { name: "Demo stack backup" })).toHaveAttribute(
      "href",
      "/backups/catalog/catalog-demo",
    )

    const deletedSource = screen.getByText("Deleted backup")
    expect(deletedSource.closest("a")).toBeNull()
    expect(screen.getByRole("link", { name: "restore-deleted-source" })).toHaveAttribute(
      "href",
      "/restores/restore-deleted-source",
    )

    expect(screen.getByRole("link", { name: "Restore from backup" })).toHaveAttribute(
      "href",
      "/backups",
    )
  })

  it("renders lifecycle states and only renders progress where the API provides it", async () => {
    registerRestoreAttemptHandler()
    renderPage()

    await screen.findByRole("img", { name: "Creating restored server state" })
    expect(screen.getAllByRole("img", { name: "Completed state" })).not.toHaveLength(0)
    expect(screen.getByRole("img", { name: "Needs attention state" })).toBeInTheDocument()
    expect(screen.getByRole("img", { name: "Cancelled state" })).toBeInTheDocument()
    expect(screen.getByRole("img", { name: "Failed state" })).toBeInTheDocument()

    expect(screen.getByRole("progressbar", { name: "Restore progress for restore-running" }))
      .toHaveAttribute("aria-valuenow", "70")
    expect(screen.getByRole("progressbar", { name: "Restore progress for restore-completed" }))
      .toHaveAttribute("aria-valuenow", "100")
    expect(screen.queryByRole("progressbar", { name: "Restore progress for restore-cancelled" }))
      .not.toBeInTheDocument()
    expect(screen.queryByRole("progressbar", { name: "Restore progress for restore-needs-attention" }))
      .not.toBeInTheDocument()
  })

  it("keeps query state in the URL for search, lifecycle, target, sort, page, and page size", async () => {
    const user = userEvent.setup()
    registerRestoreAttemptHandler(buildResponse({ totalSessions: 20, totalPages: 2, hasNextPage: true }))
    renderPage()

    await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })

    await user.selectOptions(screen.getByLabelText("Filter restore lifecycle"), "completed")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("status=completed"))

    await user.selectOptions(screen.getByLabelText("Filter by target stack"), "cool-stack")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("target=cool-stack"))

    await user.selectOptions(screen.getByLabelText("Sort restore sessions"), "source")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("sort=source"))

    await user.click(screen.getByRole("button", { name: "Sort descending" }))
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("direction=asc"))

    await user.selectOptions(screen.getByLabelText("Rows per page"), "25")
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("pageSize=25"))
    const secondPage = screen.getByRole("button", { name: "2" })
    await waitFor(() => expect(secondPage).not.toBeDisabled())

    await user.click(secondPage)
    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("page=2"))

    await user.type(screen.getByLabelText("Search restore sessions"), "restore-running")
    await waitFor(
      () => expect(screen.getByTestId("location-search")).toHaveTextContent("q=restore-running"),
      { timeout: 1000 },
    )
  }, 10_000)

  it("keeps core restore columns fixed and removes redundant table actions", async () => {
    registerRestoreAttemptHandler()
    renderPage()

    await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })

    expect(screen.queryByRole("button", { name: "Columns" })).not.toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: /Session ID \/ source/i })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: /Target stack/i })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: /Progress/i })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "cool-stack" })).toHaveAttribute(
      "href",
      "/stacks/cool-stack",
    )
    expect(screen.queryByRole("link", { name: "Stack" })).not.toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: /Open restore/i })[0]).toHaveAttribute(
      "href",
      "/restores/restore-running",
    )
  })

  it("sizes restore toolbar select wrappers at their grid and flex boundaries", async () => {
    registerRestoreAttemptHandler()
    renderPage()

    await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })

    expect(screen.getByLabelText("Filter restore lifecycle").parentElement).toHaveClass("w-full")
    expect(screen.getByLabelText("Filter by target stack").parentElement).toHaveClass("w-full")
    expect(screen.getByLabelText("Sort restore sessions").parentElement).toHaveClass("flex-1")
  })

  it("offers quick lifecycle filters without changing the restore query contract", async () => {
    const user = userEvent.setup()
    registerRestoreAttemptHandler()
    renderPage()

    await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })

    const quickFilters = screen.getByRole("group", { name: "Restore lifecycle quick filters" })
    await user.click(within(quickFilters).getByRole("button", { name: "Completed" }))

    await waitFor(() => expect(screen.getByTestId("location-search")).toHaveTextContent("status=completed"))
    expect(screen.getByRole("button", { name: "Clear" })).toBeInTheDocument()
  })

  it("shows loading, empty, and error states safely", async () => {
    server.use(
      http.get("/internal/host-agent/backups/restores", async () => {
        await new Promise((resolve) => window.setTimeout(resolve, 300))
        return HttpResponse.json(buildResponse({ sessions: [] }))
      }),
    )
    renderPage()

    expect(await screen.findByText("Loading restore sessions...")).toBeInTheDocument()
    expect(await screen.findByText(/No restore sessions match this view/i)).toBeInTheDocument()
  })

  it("shows an API error without replacing the page shell", async () => {
    server.use(
      http.get("/internal/host-agent/backups/restores", () =>
        HttpResponse.json({ message: "Restore inventory unavailable" }, { status: 503 }),
      ),
    )
    renderPage()

    expect(await screen.findByText("Could not load restore sessions")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Restore sessions" })).toBeInTheDocument()
  })

  it("renders the restore-session inventory in German", async () => {
    registerRestoreAttemptHandler()
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    try {
      renderPage()

      expect(await screen.findByRole("heading", { name: "Wiederherstellungssitzungen" })).toBeInTheDocument()
      await screen.findByRole("link", { name: "restore-completed" }, { timeout: 3000 })

      expect(screen.getByLabelText("Wiederherstellungssitzungen durchsuchen")).toBeInTheDocument()
      expect(screen.getByText("Wiederherstellungsaktivität")).toBeInTheDocument()
      expect(screen.getAllByText("Wiederhergestellter Server wird erstellt")).not.toHaveLength(0)
      expect(screen.getAllByText("Öffentliche Überprüfung")).not.toHaveLength(0)
      expect(screen.getAllByText("1 Warnung")).not.toHaveLength(0)
      expect(screen.getAllByText("1 Fehler")).not.toHaveLength(0)
      expect(screen.getByRole("img", { name: "Status Wiederhergestellter Server wird erstellt" })).toBeInTheDocument()
      expect(screen.queryByText("Creating restored server")).not.toBeInTheDocument()
      expect(await screen.findByRole("link", { name: "restore-running" }, { timeout: 3000 })).toHaveAttribute(
        "href",
        "/restores/restore-running",
      )
      expect(screen.getAllByRole("link", { name: /Wiederherstellung öffnen/i })[0]).toHaveAttribute(
        "href",
        "/restores/restore-running",
      )
    } finally {
      window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    }
  })
})
