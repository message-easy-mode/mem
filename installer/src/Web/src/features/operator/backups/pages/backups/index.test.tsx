import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { BackupRestorePage } from "."

const entry = {
  catalogEntryId: "catalog-1",
  originKind: "local-captured",
  displayName: "demo-stack backup",
  sourceStackSlug: "demo-stack",
  sourceBackupId: "backup-1",
  capturedAtUtc: "2026-06-29T00:00:00Z",
  payloadState: "available",
  integrityStatus: "valid",
  warningCount: 0,
  payloadBytes: 1024,
  createdAtUtc: "2026-06-29T00:00:00Z",
  importedAtUtc: null,
  materialisedAtUtc: null,
  payloadRemovedAtUtc: null,
}

function renderPage() {
  return renderWithProviders(<MemoryRouter><BackupRestorePage /></MemoryRouter>)
}

afterEach(() => {
  window.localStorage.clear()
})

describe("BackupRestorePage", () => {
  it("shows a catalog entry in the denser catalog table and its stable detail routes", async () => {
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: 1, entries: [entry] })))
    renderPage()

    expect(screen.getByLabelText("Loading Backup Catalog")).toBeInTheDocument()
    expect(await screen.findByText("backup-1")).toBeInTheDocument()
    expect(screen.getAllByText("demo-stack").length).toBeGreaterThan(0)
    const catalogItemsLabel = screen.getByText("Catalog items")
    expect(catalogItemsLabel).toBeInTheDocument()
    expect(screen.getByText("Integrity status")).toBeInTheDocument()
    expect(screen.queryByText("Source mix")).not.toBeInTheDocument()
    const summaryCard = catalogItemsLabel.closest('[data-slot="card"]')
    expect(summaryCard).toHaveClass("py-0")
    expect(summaryCard?.querySelector('[data-slot="card-content"]')).toHaveClass(
      "min-h-[88px]",
      "gap-2",
      "px-3",
      "py-3",
    )
    expect(screen.getByRole("link", { name: "Details" })).toHaveAttribute("href", "/backups/catalog/catalog-1")
    expect(screen.getByTestId("backup-catalog-table")).toHaveClass("min-w-[1160px]")
    expect(screen.getByTestId("backup-catalog-actions-header")).toHaveClass(
      "sticky",
      "right-0",
      "min-w-40",
    )
    expect(screen.getByTestId("backup-catalog-actions-catalog-1")).toHaveClass(
      "sticky",
      "right-0",
      "min-w-40",
    )
  })


  it("renders the Backup Catalog in German when the operator selected German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: 1, entries: [entry] })))
    renderPage()

    expect(await screen.findByRole("heading", { name: "Sicherungskatalog" })).toBeInTheDocument()
    expect(await screen.findByText("backup-1")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
    expect(screen.getAllByText("Katalogeinträge")).toHaveLength(2)
    expect(screen.getByText("1 gültig")).toBeInTheDocument()
    expect(screen.getAllByText("1,00 KB")).toHaveLength(2)
    expect(screen.getByRole("textbox", { name: "Sicherungskatalog durchsuchen" })).toHaveAttribute("placeholder", "Sicherungs-ID oder Stack-Name suchen…")
    expect(screen.getByRole("combobox", { name: "Nach Herkunft filtern" })).toBeInTheDocument()
    expect(screen.getByText("Verfügbar")).toBeInTheDocument()
  })

  it("shows imported ZIP guidance as advisories without downgrading valid integrity", async () => {
    const importedEntry = {
      ...entry,
      catalogEntryId: "catalog-2",
      sourceBackupId: "imported-1",
      displayName: "imported demo",
      originKind: "imported-zip",
      integrityStatus: "valid",
      warningCount: 0,
      advisoryCount: 4,
      importedAtUtc: "2026-06-30T00:00:00Z",
    }
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: 2, entries: [entry, importedEntry] })))
    renderPage()

    expect(await screen.findByText("imported-1")).toBeInTheDocument()
    expect(screen.getAllByText("4 import advisories")).not.toHaveLength(0)
    expect(screen.getByText("2 valid")).toBeInTheDocument()
  })

  it("filters by search and origin, and removes duplicate quick action affordances", async () => {
    const importedEntry = { ...entry, catalogEntryId: "catalog-2", sourceBackupId: "imported-1", displayName: "imported demo", originKind: "imported-zip", sourceStackSlug: "external-stack", importedAtUtc: "2026-06-30T00:00:00Z" }
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: 2, entries: [entry, importedEntry] })))
    const user = userEvent.setup()
    renderPage()

    await screen.findByText("backup-1")
    expect(screen.queryByRole("link", { name: /inspect demo-stack backup/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /more actions/i })).not.toBeInTheDocument()

    await user.type(screen.getByRole("textbox", { name: "Search catalog" }), "imported")
    expect(await screen.findByText("imported-1")).toBeInTheDocument()
    expect(screen.queryByText("backup-1")).not.toBeInTheDocument()

    await user.clear(screen.getByRole("textbox", { name: "Search catalog" }))
    await user.selectOptions(screen.getByRole("combobox", { name: "Filter by origin" }), "local-captured")
    expect(await screen.findByText("backup-1")).toBeInTheDocument()
    expect(screen.queryByText("imported-1")).not.toBeInTheDocument()
  })

  it("paginates catalog entries and resets to the first page when controls change", async () => {
    const manyEntries = Array.from({ length: 12 }, (_, index) => ({
      ...entry,
      catalogEntryId: `catalog-${index + 1}`,
      sourceBackupId: `backup-${String(index + 1).padStart(2, "0")}`,
      displayName: `backup ${index + 1}`,
      sourceStackSlug: index < 6 ? "demo-stack" : "archive-stack",
      capturedAtUtc: `2026-06-${String(28 - index).padStart(2, "0")}T00:00:00Z`,
      createdAtUtc: `2026-06-${String(28 - index).padStart(2, "0")}T00:00:00Z`,
    }))
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: manyEntries.length, entries: manyEntries })))
    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText("backup-01")).toBeInTheDocument()
    expect(screen.queryByText("backup-12")).not.toBeInTheDocument()
    expect(screen.getByText("Showing 1–10 of 12 matching backups")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Next page" }))
    expect(await screen.findByText("backup-12")).toBeInTheDocument()
    expect(screen.getByText("Showing 11–12 of 12 matching backups")).toBeInTheDocument()

    await user.type(screen.getByRole("textbox", { name: "Search catalog" }), "backup-01")
    expect(await screen.findByText("backup-01")).toBeInTheDocument()
    expect(screen.getByText("Showing 1–1 of 1 matching backup")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Previous page" })).toBeDisabled()
  })

  it("shows an explicit empty catalog state", async () => {
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ totalCount: 0, entries: [] })))
    renderPage()
    expect(await screen.findByText("No backups in the catalog")).toBeInTheDocument()
  })

  it("surfaces a loading error without presenting a false empty state", async () => {
    server.use(http.get("/internal/host-agent/backups/catalog", () => HttpResponse.json({ message: "HostAgent unavailable" }, { status: 503 })))
    renderPage()
    expect(await screen.findByText("Could not load the Backup Catalog")).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByText("No backups in the catalog")).not.toBeInTheDocument())
  })
})
