import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { http, HttpResponse } from "msw"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { BackupUploadValidationCard } from "./backup-upload-validation-card"

const validCatalogIngestionResponse = {
  source: "control-plane",
  status: "valid",
  validationId: "20260630-010203Z-import001",
  catalogEntryId: "bkp_imported_001",
  catalogPayloadState: "available",
  catalogMaterialisationAction: "created",
  uploadedFileName: "portable-export.zip",
  storedZipPath: "/redacted/source.zip",
  zipBytes: 1024,
  zipEntryCount: 8,
  totalUncompressedBytes: 4096,
  manifestPresent: true,
  checksumsPresent: true,
  manifest: {
    manifestVersion: 2,
    exportKind: "mem-stack-export",
    createdAtUtc: "2026-06-30T01:02:03Z",
    createdBy: "test",
    memVersion: "0.1.1-dev",
    stack: {
      stackId: null,
      slug: "import-source",
      displayName: "Import source",
      matrixServerName: "matrix.import-source.test",
      matrixPublicUrl: "https://matrix.import-source.test",
      elementPublicUrl: "https://chat.import-source.test",
    },
    database: { engine: "postgres", dumpFile: "database/synapse.sql", databaseName: null, username: null, present: true },
    matrix: { homeserverConfig: "matrix/homeserver.yaml", signingKey: "matrix/signing.key", mediaStore: "matrix/media_store", mediaBytes: 0, mediaFiles: 0, present: true },
    element: { config: "element/config.json", present: true },
    routes: { matrixHost: "matrix.import-source.test", elementHost: "chat.import-source.test", requiresDns: true },
    coturn: { configured: false, publicHost: null, realm: null, turnUris: [] },
    restorePolicy: { canRestoreToFreshMemServer: true, requiresPostgres: true, requiresDomainMapping: true, requiresSigningKey: true, requiresOldServerStoppedForSameServerName: true },
    includedFiles: ["database/synapse.sql"],
    warnings: [],
  },
  integrity: { checksumLines: 7, checkedFiles: 7, missingFiles: 0, failedFiles: 0, passedFiles: 7 },
  checks: [
    {
      code: "zip.opens",
      severity: "error",
      passed: true,
      message: "ZIP archive opens successfully.",
      detail: null,
    },
    {
      code: "routes.element-host.present",
      severity: "warning",
      passed: true,
      message: "Element public host is present in backup route metadata.",
      detail: "chat.import-source.test",
    },
  ],
  warnings: [],
  errors: [],
  detail: "Materialised 8 archive file(s) into the Backup Catalog.",
}

describe("BackupUploadValidationCard catalog ingestion", () => {
  it("validates and presents the newly ingested Backup Catalog item without creating a restore workspace", async () => {
    server.use(
      http.post(
        "/internal/host-agent/backups/artifacts/validated-imports",
        () => HttpResponse.json(validCatalogIngestionResponse),
      ),
    )

    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter>
        <BackupUploadValidationCard />
      </MemoryRouter>,
    )

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement
    await user.upload(fileInput, new File(["zip-content"], "portable-export.zip", { type: "application/zip" }))
    await user.click(screen.getByRole("button", { name: "Validate ZIP" }))

    expect(await screen.findByText("Backup Catalog item ready")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open Backup Catalog item" }))
      .toHaveAttribute("href", "/backups/catalog/bkp_imported_001")
    expect(screen.queryByRole("link", { name: "Open Restore Workspace" })).not.toBeInTheDocument()
    expect(screen.getByText("TURN restore intent")).toBeInTheDocument()
    expect(screen.getByText("Restore disconnected")).toBeInTheDocument()

    const errorSeverityCheck = screen.getByText("ZIP archive opens successfully.").closest(".rounded-lg")
    expect(errorSeverityCheck).not.toBeNull()
    expect(within(errorSeverityCheck as HTMLElement).getByText("passed")).toBeInTheDocument()
    expect(within(errorSeverityCheck as HTMLElement).queryByText("error")).not.toBeInTheDocument()

    const warningSeverityCheck = screen.getByText("Element public host is present in backup route metadata.").closest(".rounded-lg")
    expect(warningSeverityCheck).not.toBeNull()
    expect(within(warningSeverityCheck as HTMLElement).getByText("passed")).toBeInTheDocument()
    expect(within(warningSeverityCheck as HTMLElement).queryByText("warning")).not.toBeInTheDocument()
  })

  it("keeps failure severity visible when a validation check fails", async () => {
    server.use(
      http.post(
        "/internal/host-agent/backups/artifacts/validated-imports",
        () => HttpResponse.json({
          ...validCatalogIngestionResponse,
          status: "invalid",
          catalogEntryId: null,
          catalogPayloadState: null,
          catalogMaterialisationAction: null,
          checks: [
            {
              code: "manifest.present",
              severity: "error",
              passed: false,
              message: "mem-export-manifest.json is missing.",
              detail: "mem-stack-export/mem-export-manifest.json",
            },
          ],
          errors: ["mem-export-manifest.json is missing."],
          detail: "MEM stack export ZIP validation failed.",
        }),
      ),
    )

    const user = userEvent.setup()
    renderWithProviders(
      <MemoryRouter>
        <BackupUploadValidationCard />
      </MemoryRouter>,
    )

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement
    await user.upload(fileInput, new File(["zip-content"], "portable-export.zip", { type: "application/zip" }))
    await user.click(screen.getByRole("button", { name: "Validate ZIP" }))

    const failedCheckCode = await screen.findByText("manifest.present")
    const failedCheckRow = failedCheckCode.closest(".rounded-lg")
    expect(failedCheckRow).not.toBeNull()
    expect(within(failedCheckRow as HTMLElement).getByText("mem-export-manifest.json is missing.")).toBeInTheDocument()
    expect(within(failedCheckRow as HTMLElement).getByText("error")).toBeInTheDocument()
    expect(within(failedCheckRow as HTMLElement).getByText("failed")).toBeInTheDocument()
  })

  it("renders upload validation guidance in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    try {
      renderWithProviders(
        <MemoryRouter>
          <BackupUploadValidationCard />
        </MemoryRouter>,
      )

      expect(screen.getByText("Export-ZIP-Validierung hochladen")).toBeInTheDocument()
      expect(screen.getByText("Zuerst validieren")).toBeInTheDocument()
      expect(screen.getByRole("button", { name: "ZIP validieren" })).toBeInTheDocument()
    } finally {
      window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
    }
  })
})
