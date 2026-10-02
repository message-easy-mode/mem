import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationPackDownloadAction } from "./documentation-pack-download-action"

const mocks = vi.hoisted(() => ({
  downloadDocumentationPack: vi.fn().mockResolvedValue(undefined),
}))

vi.mock("@/features/operator/docs/documentation-pack-download", () => ({
  downloadDocumentationPack: mocks.downloadDocumentationPack,
}))

afterEach(() => {
  window.localStorage.clear()
  mocks.downloadDocumentationPack.mockClear()
})

describe("DocumentationPackDownloadAction", () => {
  it("downloads only the English documentation pack in English mode", async () => {
    const user = userEvent.setup()

    renderWithProviders(<DocumentationPackDownloadAction />)
    await user.click(screen.getByRole("button", { name: "Download English docs (ZIP)" }))

    await waitFor(() => {
      expect(mocks.downloadDocumentationPack).toHaveBeenCalledWith("en")
    })
  })

  it("downloads only the German documentation pack in German mode", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()

    renderWithProviders(<DocumentationPackDownloadAction />)
    await user.click(
      screen.getByRole("button", { name: "Deutsche Dokumentation herunterladen (ZIP)" }),
    )

    await waitFor(() => {
      expect(mocks.downloadDocumentationPack).toHaveBeenCalledWith("de")
    })
  })
})
