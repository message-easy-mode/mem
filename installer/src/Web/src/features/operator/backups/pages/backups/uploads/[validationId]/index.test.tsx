import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { UploadedZipSourceDetailPage } from "."

const validSource = {
  source: "validated-import", status: "ok", validationId: "validation-1", sourceKind: "uploaded-zip",
  uploadedFileName: "postgres-recovery.zip", recordedAtUtc: "2026-06-29T01:00:00Z", archiveBytes: 1048576,
  archiveState: "retained", validation: { status: "valid", summary: "Validation passed.", zipEntryCount: 6, totalUncompressedBytes: 2048, manifestPresent: true, checksumsPresent: true, passedChecks: 6, failedChecks: 0, warningCount: 0, errors: [] },
  manifest: { manifestVersion: 1, memVersion: "0.1.1-dev", sourceStackSlug: "postgres-stack", sourceStackDisplayName: "Postgres stack", matrixServerName: "matrix.example.test", includedFileCount: 6 },
  retention: { canDelete: true, deleteBlockReason: null, removedAtUtc: null, removedBy: null }, warnings: [], detail: null,
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/backups/uploads/validation-1"]}>
      <Routes>
        <Route path="/backups/uploads/:validationId" element={<UploadedZipSourceDetailPage />} />
        <Route path="/backups/catalog/:catalogEntryId" element={<div>Catalog entry route</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("UploadedZipSourceDetailPage materialisation", () => {
  it("materialises a valid retained ZIP then routes to its catalog entry", async () => {
    server.use(
      http.get("/internal/host-agent/backups/artifacts/validated-imports/validation-1", () => HttpResponse.json(validSource)),
      http.post("/internal/host-agent/backups/catalog/imports/validation-1/materialise", () => HttpResponse.json({
        source: "catalog", status: "ok", validationId: "validation-1", catalogEntryId: "catalog-1", action: "created", payloadState: "available", integrityStatus: "valid", warningCount: 0, payloadBytes: 1048576, materialisedAtUtc: "2026-06-29T01:02:00Z", detail: "Materialised.",
      })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Materialise into Backup Catalog" }))

    expect(await screen.findByText("Catalog entry route")).toBeInTheDocument()
  })

  it("keeps upload provenance separate from Restore Workspace state", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/artifacts/validated-imports/validation-1",
        () => HttpResponse.json(validSource),
      ),
    )

    renderPage()

    expect(await screen.findByText("Source and validation")).toBeInTheDocument()
    expect(screen.queryByText("Restore relationship")).not.toBeInTheDocument()
    expect(screen.queryByText("Restore workspace")).not.toBeInTheDocument()
  })

  it("keeps the upload context and shows a materialisation rejection", async () => {
    server.use(
      http.get("/internal/host-agent/backups/artifacts/validated-imports/validation-1", () => HttpResponse.json(validSource)),
      http.post("/internal/host-agent/backups/catalog/imports/validation-1/materialise", () => HttpResponse.json({ message: "Archive cannot be materialised." }, { status: 409 })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "Materialise into Backup Catalog" }))

    expect(await screen.findByText("Could not materialise into Backup Catalog")).toBeInTheDocument()
    expect(screen.getByText(/Archive cannot be materialised/i)).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "postgres-recovery.zip" })).toBeInTheDocument()
  })

  it("does not offer materialisation for an invalid upload", async () => {
    server.use(
      http.get("/internal/host-agent/backups/artifacts/validated-imports/validation-1", () => HttpResponse.json({ ...validSource, validation: { ...validSource.validation, status: "invalid", errors: ["Checksum mismatch"] } })),
    )
    renderPage()

    expect(await screen.findByText("Materialisation requires a valid archive")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Materialise into Backup Catalog" })).not.toBeInTheDocument()
  })
})

describe("UploadedZipSourceDetailPage archive deletion", () => {
  it("requires confirmation then refreshes the retained ZIP state", async () => {
    let removed = false
    server.use(
      http.get("/internal/host-agent/backups/artifacts/validated-imports/validation-1", () => HttpResponse.json({ ...validSource, archiveState: removed ? "removed" : "retained", retention: { canDelete: !removed, deleteBlockReason: removed ? "Already removed." : null, removedAtUtc: removed ? "2026-06-29T03:00:00Z" : null, removedBy: null } })),
      http.delete("/internal/host-agent/backups/catalog/imports/validation-1/archive", () => { removed = true; return HttpResponse.json({ source: "catalog", status: "ok", validationId: "validation-1", archiveState: "removed", catalogEntryId: "catalog-1", detail: "Removed." }) }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole("button", { name: "Delete uploaded ZIP" }))
    expect(screen.getByRole("heading", { name: "Delete uploaded ZIP?" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: /^Delete uploaded ZIP$/ }))
    expect(await screen.findByText("Uploaded source removed")).toBeInTheDocument()
  })
})
