import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  deleteBackupCatalogEntry,
  exportCatalogPortableZip,
  listBackupCatalog,
  materialiseImportedZip,
  prepareCatalogRestoreSession,
} from "./backup-catalog.api"

describe("Backup Catalog API", () => {
  it("loads the safe catalog inventory projection", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog", () =>
        HttpResponse.json({
          totalCount: 1,
          entries: [
            {
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
            },
          ],
        }),
      ),
    )

    await expect(listBackupCatalog()).resolves.toMatchObject({
      totalCount: 1,
      entries: [{ catalogEntryId: "catalog-1", payloadState: "available" }],
    })
  })

  it("uses encoded ids and no request body for materialisation and restore handoff", async () => {
    server.use(
      http.post(
        "/internal/host-agent/backups/catalog/imports/import%2F1/materialise",
        async ({ request }) => {
          expect(await request.text()).toBe("")
          return HttpResponse.json({ catalogEntryId: "catalog-1" })
        },
      ),
      http.post(
        "/internal/host-agent/backups/catalog/catalog%2F1/restore-session",
        async ({ request }) => {
          expect(await request.text()).toBe("")
          return HttpResponse.json({ restoreSessionId: "restore-1" })
        },
      ),
    )

    await expect(materialiseImportedZip("import/1")).resolves.toMatchObject({
      catalogEntryId: "catalog-1",
    })
    await expect(prepareCatalogRestoreSession("catalog/1")).resolves.toMatchObject({
      restoreSessionId: "restore-1",
    })
  })

  it("generates a portable ZIP from encoded catalog identity without a request body", async () => {
    server.use(
      http.post(
        "/internal/host-agent/backups/catalog/catalog%2F1/portable-export",
        async ({ request }) => {
          expect(await request.text()).toBe("")
          return HttpResponse.json({
            source: "control-plane",
            status: "created",
            catalogEntryId: "catalog/1",
            originKind: "local-captured",
            sourceStackSlug: "demo-stack",
            exportId: "catalog-catalog-1",
            downloadName: "mem-stack-catalog-catalog-1.zip",
            downloadPath: "/internal/host-agent/backups/artifacts/portable-exports/catalog-catalog-1/download",
            sizeBytes: 1024,
            warnings: [],
            detail: "Generated.",
          })
        },
      ),
    )

    await expect(exportCatalogPortableZip("catalog/1")).resolves.toMatchObject({
      catalogEntryId: "catalog/1",
      exportId: "catalog-catalog-1",
    })
  })

  it("keeps permanent catalog deletion bodyless so the server derives the actor from the named session", async () => {
    server.use(
      http.delete(
        "/internal/host-agent/backups/catalog/catalog-1",
        async ({ request }) => {
          expect(await request.text()).toBe("")
          return HttpResponse.json({
            source: "control-plane",
            status: "deleted",
            catalogEntryId: "catalog-1",
            originKind: "local-captured",
            deletedBy: "owner.nigel",
            payloadDeleted: true,
            originalArchiveDeleted: false,
            portableExportsDeleted: 1,
            detachedRestoreAttempts: 0,
            detail: "Permanently deleted.",
          })
        },
      ),
    )

    await expect(
      deleteBackupCatalogEntry("catalog-1"),
    ).resolves.toMatchObject({ status: "deleted", deletedBy: "owner.nigel" })
  })
})
