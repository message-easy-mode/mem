import { HttpResponse, http } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { StoragePage } from "./storage-page"

const stacks = [
  {
    stackId: "stack-alpha",
    slug: "qa-alpha-restored",
    displayName: "QA Alpha Restored",
    category: null,
    logoUrl: null,
    lastVerifiedStatus: "healthy",
    lastVerifiedAtUtc: "2026-09-14T00:00:00Z",
    matrixPublicBaseUrl: "https://matrix-alpha.example",
    elementPublicBaseUrl: "https://chat-alpha.example",
  },
  {
    stackId: "stack-bravo",
    slug: "qa-bravo",
    displayName: null,
    category: null,
    logoUrl: null,
    lastVerifiedStatus: "healthy",
    lastVerifiedAtUtc: "2026-09-14T00:00:00Z",
    matrixPublicBaseUrl: "https://matrix-bravo.example",
    elementPublicBaseUrl: "https://chat-bravo.example",
  },
]

const alphaStorage = {
  source: "host-agent",
  status: "ok",
  runtimeStackId: "stack-alpha",
  slug: "qa-alpha-restored",
  matrix: {
    dataPath: "/srv/mem/qa-alpha/matrix",
    homeserverYamlPath: "/srv/mem/qa-alpha/matrix/homeserver.yaml",
    homeserverYamlBytes: 2252,
    signingKeyPath: "/srv/mem/qa-alpha/matrix/signing.key",
    signingKeyBytes: 59,
    mediaStorePath: "/srv/mem/qa-alpha/matrix/media_store",
    mediaStoreExists: true,
    totalBytes: 1471078,
    totalFiles: 12,
    sections: [
      {
        key: "local_content",
        displayName: "Local uploads",
        path: "/srv/mem/qa-alpha/matrix/media_store/local_content",
        exists: true,
        bytes: 1468006,
        files: 7,
      },
      {
        key: "remote_content",
        displayName: "Remote cache",
        path: "/srv/mem/qa-alpha/matrix/media_store/remote_content",
        exists: true,
        bytes: 2048,
        files: 2,
      },
      {
        key: "thumbnails",
        displayName: "Generated thumbnails",
        path: "/srv/mem/qa-alpha/matrix/media_store/thumbnails",
        exists: true,
        bytes: 1024,
        files: 3,
      },
      {
        key: "url_cache",
        displayName: "URL preview cache",
        path: "/srv/mem/qa-alpha/matrix/media_store/url_cache",
        exists: false,
        bytes: 0,
        files: 0,
      },
    ],
  },
  element: null,
  detail: null,
}

const bravoStorage = {
  source: "host-agent",
  status: "ok",
  runtimeStackId: "stack-bravo",
  slug: "qa-bravo",
  matrix: {
    dataPath: "/srv/mem/qa-bravo/matrix",
    homeserverYamlPath: "/srv/mem/qa-bravo/matrix/homeserver.yaml",
    homeserverYamlBytes: 2119,
    signingKeyPath: "/srv/mem/qa-bravo/matrix/signing.key",
    signingKeyBytes: 59,
    mediaStorePath: "/srv/mem/qa-bravo/matrix/media_store",
    mediaStoreExists: true,
    totalBytes: 0,
    totalFiles: 0,
    sections: [
      {
        key: "local_content",
        displayName: "Local uploads",
        path: "/srv/mem/qa-bravo/matrix/media_store/local_content",
        exists: false,
        bytes: 0,
        files: 0,
      },
      {
        key: "remote_content",
        displayName: "Remote cache",
        path: "/srv/mem/qa-bravo/matrix/media_store/remote_content",
        exists: false,
        bytes: 0,
        files: 0,
      },
      {
        key: "thumbnails",
        displayName: "Generated thumbnails",
        path: "/srv/mem/qa-bravo/matrix/media_store/thumbnails",
        exists: false,
        bytes: 0,
        files: 0,
      },
      {
        key: "url_cache",
        displayName: "URL preview cache",
        path: "/srv/mem/qa-bravo/matrix/media_store/url_cache",
        exists: false,
        bytes: 0,
        files: 0,
      },
    ],
  },
  element: null,
  detail: null,
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/storage"]}>
      <StoragePage />
    </MemoryRouter>,
  )
}

function useStorageHandlers(
  onList?: () => void,
  onStorage?: () => void,
  dashboardDisk: { usedBytes: number; totalBytes: number; scope: string } | null = {
    usedBytes: 75 * 1024 ** 3,
    totalBytes: 100 * 1024 ** 3,
    scope: "mem_data",
  },
) {
  server.use(
    http.get("/api/operator/dashboard/overview", () =>
      HttpResponse.json({
        host: {
          state: "available",
          disk: dashboardDisk,
          storageUnavailableReasonCode: dashboardDisk ? null : "mem_data_filesystem_unavailable",
        },
      }),
    ),
    http.get("/internal/host-agent/runtime-stacks", () => {
      onList?.()
      return HttpResponse.json({
        source: "host-agent",
        status: "ok",
        stacks,
        detail: null,
      })
    }),
    http.get("/internal/host-agent/runtime-stacks/:slug/storage", ({ params }) => {
      onStorage?.()
      return HttpResponse.json(params.slug === "qa-alpha-restored" ? alphaStorage : bravoStorage)
    }),
  )
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("StoragePage", () => {
  it("renders a compact fleet inventory and keeps technical paths in stack detail", async () => {
    useStorageHandlers()

    renderPage()

    expect(await screen.findByRole("heading", { name: "Storage" })).toBeInTheDocument()
    expect(await screen.findByText("Storage overview")).toBeInTheDocument()
    expect(await screen.findByText("Storage inventory")).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText("Managed Matrix media")).toBeInTheDocument()
      expect(screen.getByText("1.4 MiB")).toBeInTheDocument()
      expect(screen.getByText("Media files")).toBeInTheDocument()
      expect(screen.getByText("12", { selector: "div.text-2xl" })).toBeInTheDocument()
      expect(screen.getByText("MEM data storage available")).toBeInTheDocument()
      expect(screen.getByText("25% available")).toBeInTheDocument()
      expect(screen.getByText("25 GiB available of 100 GiB")).toBeInTheDocument()
      expect(screen.getByText("75% used")).toBeInTheDocument()
      expect(screen.getByRole("progressbar", { name: "75% used" })).toHaveAttribute(
        "aria-valuenow",
        "75",
      )
      expect(screen.getAllByText("Local uploads").length).toBeGreaterThanOrEqual(2)
      expect(screen.getByText("1.4 MiB · 7 files", { selector: "div.mt-1" })).toBeInTheDocument()
      expect(screen.getByText("Chat servers")).toBeInTheDocument()
      expect(screen.getByText("2", { selector: "div.text-2xl" })).toBeInTheDocument()
      expect(screen.getByText("Media stores available")).toBeInTheDocument()
      expect(screen.getByText("2 / 2")).toBeInTheDocument()
      expect(screen.getAllByText("Remote cache").length).toBeGreaterThanOrEqual(2)
      expect(screen.getByText("2 KiB · 2 files", { selector: "div.mt-1" })).toBeInTheDocument()
      expect(screen.getByText("Generated thumbnails")).toBeInTheDocument()
      expect(screen.getByText("1 KiB · 3 files")).toBeInTheDocument()
      expect(screen.getByText("URL preview cache")).toBeInTheDocument()
      expect(screen.getByText("0 B · 0 files", { selector: "div.mt-1" })).toBeInTheDocument()
    })

    const table = screen.getByTestId("storage-inventory-table")
    expect(table).toHaveClass("min-w-[1040px]")

    const alphaRow = await screen.findByTestId("storage-row-qa-alpha-restored")
    expect(within(alphaRow).getByText("1.4 MiB · 12 files")).toBeInTheDocument()
    expect(within(alphaRow).getByText("1.4 MiB · 7 files")).toBeInTheDocument()
    expect(within(alphaRow).getByText("2 KiB · 2 files")).toBeInTheDocument()
    expect(within(alphaRow).getByText("available")).toBeInTheDocument()

    const bravoRow = await screen.findByTestId("storage-row-qa-bravo")
    expect(within(bravoRow).getAllByText("not created yet").length).toBeGreaterThanOrEqual(2)

    const alphaActions = screen.getByTestId("storage-actions-qa-alpha-restored")
    expect(alphaActions).toHaveClass("sticky", "right-0", "min-w-40")
    expect(within(alphaActions).getByRole("link", { name: "Open details" })).toHaveAttribute(
      "href",
      "/stacks/qa-alpha-restored/services",
    )

    expect(screen.queryByText("Restore status")).not.toBeInTheDocument()
    expect(screen.queryByText("Coming soon")).not.toBeInTheDocument()
    expect(screen.queryByText("Media store path")).not.toBeInTheDocument()
    expect(screen.queryByText(alphaStorage.matrix.mediaStorePath)).not.toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getAllByRole("button", { name: "Refresh" })).toHaveLength(1)
    })
  })

  it("renders the complete fleet workspace in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    useStorageHandlers()

    renderPage()

    expect(await screen.findByRole("heading", { name: "Speicher" })).toBeInTheDocument()
    expect(await screen.findByText("Speicherübersicht")).toBeInTheDocument()
    expect(await screen.findByText("Verwaltete Matrix-Medien")).toBeInTheDocument()
    expect(await screen.findByText("Mediendateien")).toBeInTheDocument()
    expect(await screen.findByText("MEM-Datenspeicher verfügbar")).toBeInTheDocument()
    expect(await screen.findByText(/25.*%.*verfügbar/)).toBeInTheDocument()
    expect(await screen.findByText(/25 GiB verfügbar von 100 GiB/)).toBeInTheDocument()
    expect(await screen.findByText(/75.*%.*belegt/)).toBeInTheDocument()
    expect(await screen.findByText("Verfügbare Medienspeicher")).toBeInTheDocument()
    expect(await screen.findByText("Erzeugte Vorschaubilder")).toBeInTheDocument()
    expect(await screen.findByText("URL-Vorschau-Cache")).toBeInTheDocument()
    expect(await screen.findByText("Speicherinventar")).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Chatserver" })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Matrix-Medien" })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Lokale Uploads" })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Entfernter Cache" })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Medienspeicher" })).toBeInTheDocument()
    expect(screen.getByRole("columnheader", { name: "Aktionen" })).toBeInTheDocument()

    const alphaRow = await screen.findByTestId("storage-row-qa-alpha-restored")
    expect(within(alphaRow).getByText("1,4 MiB · 7 Dateien")).toBeInTheDocument()
    expect(within(alphaRow).getByRole("link", { name: "Details öffnen" })).toHaveAttribute(
      "href",
      "/stacks/qa-alpha-restored/services",
    )

    const bravoRow = await screen.findByTestId("storage-row-qa-bravo")
    expect(within(bravoRow).getAllByText("noch nicht erstellt").length).toBeGreaterThanOrEqual(2)

    expect(screen.queryByText("Storage overview")).not.toBeInTheDocument()
    expect(screen.queryByText("Open details")).not.toBeInTheDocument()
    expect(screen.queryByText("Coming soon")).not.toBeInTheDocument()
  })

  it("does not present partial storage totals as authoritative when an inspection fails", async () => {
    useStorageHandlers()
    server.use(
      http.get("/internal/host-agent/runtime-stacks", () =>
        HttpResponse.json({
          source: "host-agent",
          status: "ok",
          stacks,
          detail: null,
        }),
      ),
      http.get("/internal/host-agent/runtime-stacks/:slug/storage", ({ params }) => {
        if (params.slug === "qa-bravo") {
          return HttpResponse.json({ detail: "storage inspection failed" }, { status: 500 })
        }

        return HttpResponse.json(alphaStorage)
      }),
    )

    renderPage()

    const bravoRow = await screen.findByTestId("storage-row-qa-bravo")
    await waitFor(() => {
      expect(screen.getAllByText("Unavailable").length).toBeGreaterThanOrEqual(1)
      expect(screen.getByText("25% available")).toBeInTheDocument()
      expect(within(bravoRow).getAllByText("Inspection failed")).toHaveLength(4)
      expect(screen.getByText("2", { selector: "div.text-2xl" })).toBeInTheDocument()
    })
  })

  it("does not invent MEM data filesystem capacity when it is unavailable", async () => {
    useStorageHandlers(undefined, undefined, null)

    renderPage()

    expect(await screen.findByText("Storage overview")).toBeInTheDocument()
    await waitFor(() => {
      const capacity = screen.getByText("MEM data storage available").closest("div.rounded-lg")
      expect(capacity).not.toBeNull()
      expect(within(capacity as HTMLElement).getByText("Unavailable")).toBeInTheDocument()
      expect(within(capacity as HTMLElement).queryByText(/0%/)).not.toBeInTheDocument()
    })
  })

  it("uses the single fleet Refresh action to refresh stacks and mounted storage rows", async () => {
    let listRequests = 0
    let storageRequests = 0
    useStorageHandlers(
      () => {
        listRequests += 1
      },
      () => {
        storageRequests += 1
      },
    )

    const user = userEvent.setup()
    renderPage()

    await screen.findByTestId("storage-row-qa-alpha-restored")
    await waitFor(() => {
      expect(listRequests).toBe(1)
      expect(storageRequests).toBe(2)
    })

    await waitFor(() => expect(screen.getByRole("button", { name: "Refresh" })).toBeEnabled())
    await user.click(screen.getByRole("button", { name: "Refresh" }))

    await waitFor(() => {
      expect(listRequests).toBeGreaterThanOrEqual(2)
      expect(storageRequests).toBeGreaterThanOrEqual(4)
    })
  })
})
