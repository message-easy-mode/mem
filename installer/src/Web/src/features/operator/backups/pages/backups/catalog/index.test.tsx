import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { backupCatalogKeys, useBackupCatalog } from "@/features/operator/backups/hooks/use-backup-catalog"

import { BackupCatalogEntryRouteBoundary } from "."

const detail = {
  catalogEntryId: "catalog-1",
  originKind: "imported-zip",
  displayName: "Postgres recovery export",
  sourceStackSlug: "postgres-stack",
  sourceBackupId: "backup-1",
  validationId: "validation-1",
  manifestVersion: 1,
  memVersion: "0.1.1-dev",
  matrixServerName: "matrix.example.test",
  matrixHost: "matrix.example.test",
  elementHost: "chat.example.test",
  capturedAtUtc: "2026-06-29T00:00:00Z",
  payloadState: "available",
  integrityStatus: "valid",
  integritySummary: "Payload checks passed.",
  warningCount: 0,
  payloadBytes: 1048576,
  createdAtUtc: "2026-06-29T00:00:00Z",
  importedAtUtc: "2026-06-29T01:00:00Z",
  materialisedAtUtc: "2026-06-29T01:02:00Z",
  payloadRemovedAtUtc: null,
  payloadRemovedBy: null,
}

function BackupCatalogListProbe() {
  const catalogQuery = useBackupCatalog()

  return (
    <div>
      <div>Backup Catalog list</div>
      <div data-testid="backup-catalog-count">{catalogQuery.data?.totalCount ?? "loading"}</div>
    </div>
  )
}

function renderPage({ withCatalogListProbe = false }: { withCatalogListProbe?: boolean } = {}) {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/backups/catalog/catalog-1"]}>
      <Routes>
        <Route path="/backups/catalog/:catalogEntryId" element={<BackupCatalogEntryRouteBoundary />} />
        <Route path="/restores/:restoreSessionId" element={<div>Canonical restore workspace route</div>} />
        <Route
          path="/backups"
          element={withCatalogListProbe ? <BackupCatalogListProbe /> : <div>Backup Catalog list</div>}
        />
      </Routes>
    </MemoryRouter>,
  )
}

describe("BackupCatalogEntryRouteBoundary", () => {
  it("shows identity, provenance, lifecycle, and active restore navigation", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: true,
        activeRestoreSessionId: "restore-1", canDelete: false, deleteBlockReason: "An active restore is using this backup.", originalArchive: null,
      })),
    )
    renderPage()

    expect(screen.getByLabelText("Loading catalog entry")).toBeInTheDocument()
    expect(await screen.findByRole("heading", { name: "Postgres recovery export" })).toBeInTheDocument()
    expect(screen.getAllByText("Imported ZIP")).not.toHaveLength(0)
    expect(screen.getByRole("heading", { name: "Restore readiness" })).toBeInTheDocument()
    expect(await screen.findByText("Active")).toBeInTheDocument()
    expect(screen.getByText("Payload checks passed.")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open active restore workspace" })).toHaveAttribute("href", "/restores/restore-1")
  })


  it("separates non-blocking imported ZIP advisories from valid payload integrity", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({
        ...detail,
        integrityStatus: "valid",
        integritySummary: "Structural, manifest, and checksum validation passed. 4 non-blocking import advisories retained for operator review.",
        warningCount: 0,
        advisoryCount: 4,
        advisories: [
          { category: "security", title: "Matrix signing identity included", message: "Store and transfer the archive securely." },
          { category: "operational-safety", title: "Original Matrix server must be offline", message: "Do not run two public homeservers with the same identity." },
          { category: "configuration", title: "TURN metadata is incomplete", message: "Verify calling separately if TURN is required." },
          { category: "provenance", title: "Regenerated portable export", message: "The export was regenerated from the Backup Catalog payload." },
        ],
      })),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
    )
    renderPage()

    expect((await screen.findAllByText("Import advisories")).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText("Matrix signing identity included")).toBeInTheDocument()
    expect(screen.getByText("Original Matrix server must be offline")).toBeInTheDocument()
    expect(screen.getByText("TURN metadata is incomplete")).toBeInTheDocument()
    expect(screen.getByText("Regenerated portable export")).toBeInTheDocument()
    expect(await screen.findByText("4 import advisories")).toBeInTheDocument()
  })

  it("generates a portable ZIP directly from the catalog payload", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/portable-export", () => HttpResponse.json({
        source: "control-plane", status: "created", catalogEntryId: "catalog-1", originKind: "imported-zip",
        sourceStackSlug: "postgres-stack", exportId: "catalog-catalog-1", downloadName: "mem-stack-catalog-catalog-1.zip",
        downloadPath: "/internal/host-agent/backups/artifacts/portable-exports/catalog-catalog-1/download",
        sizeBytes: 2048, warnings: ["Store it securely."], detail: "Generated from the managed catalog payload.",
      })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Export portable ZIP" }))

    expect(await screen.findByText("portable ZIP ready")).toBeInTheDocument()
    expect(screen.getByText("mem-stack-catalog-catalog-1.zip")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Download ZIP" })).toBeInTheDocument()
    expect(screen.getByText(/original uploaded ZIP was not required/i)).toBeInTheDocument()
  })

  it("shows a clear detail-load failure for a genuine server error", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({ message: "Catalog database unavailable" }, { status: 500 })),
    )
    renderPage()
    expect(await screen.findByText("Could not load catalog entry")).toBeInTheDocument()
    expect(screen.getByText(/Catalog database unavailable/i)).toBeInTheDocument()
  })

  it("renders a calm not-found state for an old or deleted catalog URL without loading lifecycle state", async () => {
    let lifecycleReads = 0

    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({
        error: "backup_catalog_entry_not_found",
        detail: "No Backup Catalog entry exists with the supplied catalog entry id.",
      }, { status: 404 })),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => {
        lifecycleReads += 1
        return HttpResponse.json({ message: "Not found" }, { status: 404 })
      }),
    )

    renderPage()

    expect(await screen.findByText("Backup no longer exists")).toBeInTheDocument()
    expect(screen.getByText("This backup is no longer in the Backup Catalog. It may have been deleted.")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to Backup Catalog" })).toHaveAttribute("href", "/backups")
    expect(screen.queryByText("Could not load catalog entry")).not.toBeInTheDocument()
    expect(screen.queryByText(/GET \/internal\/host-agent\/backups\/catalog/)).not.toBeInTheDocument()
    expect(lifecycleReads).toBe(0)
  })

  it("renders the deleted catalog not-found state in German", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({
        error: "backup_catalog_entry_not_found",
        detail: "No Backup Catalog entry exists with the supplied catalog entry id.",
      }, { status: 404 })),
    )
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    try {
      renderPage()

      expect(await screen.findByText("Sicherung ist nicht mehr vorhanden")).toBeInTheDocument()
      expect(screen.getByText("Diese Sicherung ist nicht mehr im Sicherungskatalog vorhanden. Sie wurde möglicherweise gelöscht.")).toBeInTheDocument()
    } finally {
      window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    }
  })

  it("keeps entry details visible when lifecycle status cannot be loaded", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({ message: "Lifecycle unavailable" }, { status: 503 })),
    )
    renderPage()
    expect(await screen.findByRole("heading", { name: "Postgres recovery export" })).toBeInTheDocument()
    expect(await screen.findByText("Could not load lifecycle status")).toBeInTheDocument()
  })
  it("creates a restore workspace then routes into the canonical session", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        source: "catalog", status: "ok", catalogEntryId: "catalog-1", restoreSessionId: "restore-created-1",
        restoreAttemptCreated: true, restoreAttemptResumed: false, sourceKind: "imported-zip", payloadState: "available",
        integrityStatus: "valid", warningCount: 0, detail: "Created restore workspace.",
      })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText("Canonical restore workspace route")).toBeInTheDocument()
  })

  it("renders a localised entry-not-found problem and keeps raw detail closed", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "backup_catalog_entry_not_found",
        detail: "Backup Catalog entry 'catalog-1' was not found.",
        message: {
          code: "backup-catalog.entry.not-found",
          arguments: { catalogEntryId: "catalog-1" },
        },
      }, { status: 404 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText(
      "The catalog entry with Catalog Entry ID catalog-1 was not found.",
    )).toBeInTheDocument()
    expect(screen.getByText("Backup Catalog entry 'catalog-1' was not found.")).toBeInTheDocument()
    expect(screen.getByText("Technical details").closest("details")).not.toHaveAttribute("open")
    expect(screen.getByRole("heading", { name: "Postgres recovery export" })).toBeInTheDocument()
  })

  it("renders an entry-unavailable problem with the literal payload state", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "backup_catalog_entry_not_available",
        detail: "Backup Catalog entry 'catalog-1' cannot begin a restore while payload state is 'removed'.",
        message: {
          code: "backup-catalog.entry.unavailable",
          arguments: { catalogEntryId: "catalog-1", payloadState: "removed" },
        },
      }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText(
      "The catalog entry with Catalog Entry ID catalog-1 cannot start a restore while payload state is removed.",
    )).toBeInTheDocument()
    expect(screen.getByText(/payload state is 'removed'/i)).toBeInTheDocument()
  })

  it("keeps an incomplete entry-unavailable descriptor on the raw-detail fallback", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "backup_catalog_entry_not_available",
        detail: "The catalog payload state was not supplied.",
        message: {
          code: "backup-catalog.entry.unavailable",
          arguments: { catalogEntryId: "catalog-1" },
        },
      }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText("The catalog payload state was not supplied.")).toBeInTheDocument()
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("renders an invalid restore-request problem in German", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "invalid_backup_catalog_restore_request",
        detail: "The catalog restore request was rejected.",
        message: {
          code: "backup-catalog.restore-request.invalid",
          arguments: { catalogEntryId: "catalog-1" },
        },
      }, { status: 400 })),
    )
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    try {
      const user = userEvent.setup()
      renderPage()

      await user.click(await screen.findByRole("button", { name: "Diese Sicherung wiederherstellen" }))

      expect(await screen.findByText(
        "Die Wiederherstellungsanfrage für den Katalogeintrag catalog-1 ist ungültig.",
      )).toBeInTheDocument()
      expect(screen.getByText("Technische Details").closest("details")).not.toHaveAttribute("open")
      expect(screen.getByText("The catalog restore request was rejected.")).toBeInTheDocument()
    } finally {
      window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    }
  })

  it("keeps an unknown structured problem on the raw-detail fallback", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "future_backup_catalog_problem",
        detail: "A future Backup Catalog condition needs operator attention.",
        message: {
          code: "backup-catalog.future-condition",
          arguments: { catalogEntryId: "catalog-1" },
        },
      }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText(
      "A future Backup Catalog condition needs operator attention.",
    )).toBeInTheDocument()
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("keeps an absent structured descriptor on the raw-detail fallback", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.post("/internal/host-agent/backups/catalog/catalog-1/restore-session", () => HttpResponse.json({
        error: "legacy_backup_catalog_restore_problem",
        detail: "Payload cannot be restored.",
      }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Restore this backup" }))

    expect(await screen.findByText("Payload cannot be restored.")).toBeInTheDocument()
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Postgres recovery export" })).toBeInTheDocument()
  })

  it("renders Backup Catalog detail actions in German", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
    )
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    try {
      renderPage()

      expect(await screen.findByRole("heading", { name: "Postgres recovery export" })).toBeInTheDocument()
      expect(screen.getByRole("heading", { name: "Wiederherstellungsbereitschaft" })).toBeInTheDocument()
      expect(screen.getByRole("button", { name: "Diese Sicherung wiederherstellen" })).toBeInTheDocument()
      expect(screen.getAllByText("Importierte ZIP-Datei")).not.toHaveLength(0)
    } finally {
      window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    }
  })

})

describe("BackupCatalogEntryRouteBoundary permanent deletion", () => {
  it("does not refetch the deleted detail resource and refreshes the catalog after navigation", async () => {
    let deleted = false
    let detailReadsAfterDelete = 0
    let lifecycleReadsAfterDelete = 0
    let catalogReads = 0

    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => {
        if (deleted) {
          detailReadsAfterDelete += 1
          return HttpResponse.json({
            error: "backup_catalog_entry_not_found",
            detail: "No Backup Catalog entry exists with the supplied catalog entry id.",
          }, { status: 404 })
        }

        return HttpResponse.json(detail)
      }),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => {
        if (deleted) {
          lifecycleReadsAfterDelete += 1
          return HttpResponse.json({
            error: "backup_catalog_entry_not_found",
            detail: "No Backup Catalog entry exists with the supplied catalog entry id.",
          }, { status: 404 })
        }

        return HttpResponse.json({
          catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
          activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
        })
      }),
      http.get("/internal/host-agent/backups/catalog", () => {
        catalogReads += 1
        return HttpResponse.json({
          totalCount: deleted ? 0 : 1,
          entries: deleted ? [] : [{
            catalogEntryId: detail.catalogEntryId,
            originKind: detail.originKind,
            displayName: detail.displayName,
            sourceStackSlug: detail.sourceStackSlug,
            sourceBackupId: detail.sourceBackupId,
            capturedAtUtc: detail.capturedAtUtc,
            payloadState: detail.payloadState,
            integrityStatus: detail.integrityStatus,
            warningCount: detail.warningCount,
            payloadBytes: detail.payloadBytes,
            createdAtUtc: detail.createdAtUtc,
            importedAtUtc: detail.importedAtUtc,
            materialisedAtUtc: detail.materialisedAtUtc,
            payloadRemovedAtUtc: detail.payloadRemovedAtUtc,
          }],
        })
      }),
      http.delete("/internal/host-agent/backups/catalog/catalog-1", async ({ request }) => {
        expect(await request.text()).toBe("")
        deleted = true
        return HttpResponse.json({
          source: "control-plane", status: "deleted", catalogEntryId: "catalog-1", originKind: "imported-zip",
          deletedBy: "owner.nigel", payloadDeleted: true, originalArchiveDeleted: true, portableExportsDeleted: 1,
          detachedRestoreAttempts: 0, detail: "Permanently deleted.",
        })
      }),
    )
    const user = userEvent.setup()
    const { queryClient } = renderPage({ withCatalogListProbe: true })
    queryClient.setQueryData(backupCatalogKeys.list(), {
      totalCount: 1,
      entries: [{
        catalogEntryId: detail.catalogEntryId,
        originKind: detail.originKind,
        displayName: detail.displayName,
        sourceStackSlug: detail.sourceStackSlug,
        sourceBackupId: detail.sourceBackupId,
        capturedAtUtc: detail.capturedAtUtc,
        payloadState: detail.payloadState,
        integrityStatus: detail.integrityStatus,
        warningCount: detail.warningCount,
        payloadBytes: detail.payloadBytes,
        createdAtUtc: detail.createdAtUtc,
        importedAtUtc: detail.importedAtUtc,
        materialisedAtUtc: detail.materialisedAtUtc,
        payloadRemovedAtUtc: detail.payloadRemovedAtUtc,
      }],
    })

    await user.click(await screen.findByRole("button", { name: "Delete backup permanently" }))
    expect(screen.getByRole("heading", { name: "Delete backup permanently?" })).toBeInTheDocument()
    expect(screen.getByText("This cannot be undone")).toBeInTheDocument()
    expect(screen.getByText("What stays intact")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: /^Delete backup permanently$/ }))

    expect(await screen.findByText("Backup Catalog list")).toBeInTheDocument()
    await waitFor(() => expect(catalogReads).toBeGreaterThan(0))
    await waitFor(() => expect(screen.getByTestId("backup-catalog-count")).toHaveTextContent("0"))
    expect(detailReadsAfterDelete).toBe(0)
    expect(lifecycleReadsAfterDelete).toBe(0)
    expect(screen.queryByText("Could not load catalog entry")).not.toBeInTheDocument()
  })

  it("requires recent identity verification then resumes only the confirmed permanent deletion", async () => {
    let deleteAttempts = 0

    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
      http.delete("/internal/host-agent/backups/catalog/catalog-1", () => {
        deleteAttempts += 1

        return deleteAttempts === 1
          ? HttpResponse.json(
            {
              error: "step_up_required",
              detail: "Fresh identity verification is required before this action.",
            },
            { status: 403 },
          )
          : HttpResponse.json({
            source: "control-plane", status: "deleted", catalogEntryId: "catalog-1", originKind: "imported-zip",
            deletedBy: "owner.nigel", payloadDeleted: true, originalArchiveDeleted: true, portableExportsDeleted: 1,
            detachedRestoreAttempts: 0, detail: "Permanently deleted.",
          })
      }),
      http.post("/api/auth/step-up", async ({ request }) => {
        expect(await request.json()).toEqual({
          password: "Secure!Foundation123",
          code: "123456",
        })

        return HttpResponse.json({
          status: "step_up_authenticated",
          expiresAtUtc: "2026-07-05T22:10:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Delete backup permanently" }))
    await user.click(screen.getByRole("button", { name: /^Delete backup permanently$/ }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()

    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    expect(await screen.findByText("Backup Catalog list")).toBeInTheDocument()
    expect(deleteAttempts).toBe(2)
  })

  it("offers a permanent catalog-record purge for a legacy removed entry", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({ ...detail, payloadState: "removed", payloadRemovedAtUtc: "2026-06-29T02:00:00Z" })),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({
        catalogEntryId: "catalog-1", payloadState: "removed", payloadPresent: false, hasActiveRestore: false,
        activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null,
      })),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole("button", { name: "Permanently delete catalog record" }))
    expect(screen.getByRole("heading", { name: "Permanently delete catalog record?" })).toBeInTheDocument()
    expect(screen.getByText(/recoverable payload was already removed/i)).toBeInTheDocument()
  })

  it("keeps the permanent deletion confirmation open and shows an active-restore conflict", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json(detail)),
      http.get("/internal/host-agent/backups/catalog/catalog-1/lifecycle", () => HttpResponse.json({ catalogEntryId: "catalog-1", payloadState: "available", payloadPresent: true, hasActiveRestore: false, activeRestoreSessionId: null, canDelete: true, deleteBlockReason: null, originalArchive: null })),
      http.delete("/internal/host-agent/backups/catalog/catalog-1", () => HttpResponse.json({ message: "An active restore now references this backup." }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole("button", { name: "Delete backup permanently" }))
    await user.click(screen.getByRole("button", { name: /^Delete backup permanently$/ }))
    expect(await screen.findByText("Could not delete backup permanently")).toBeInTheDocument()
    expect(screen.getByText(/active restore now references/i)).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Delete backup permanently?" })).toBeInTheDocument()
  })
})
