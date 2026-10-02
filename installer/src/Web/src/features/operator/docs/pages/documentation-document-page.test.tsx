import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { fireEvent, screen, waitFor, within } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { useI18n } from "@/app/i18n/i18n-context"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationDocumentPage } from "./documentation-document-page"
import { DocumentationHomePage } from "./documentation-home-page"

function LanguageSwitchProbe() {
  const { language, setLanguage } = useI18n()
  const location = useLocation()

  return (
    <div>
      <span data-testid="active-language">{language}</span>
      <span data-testid="active-route">{location.pathname}</span>
      <button type="button" onClick={() => setLanguage(language === "en" ? "de" : "en")}>
        Switch language
      </button>
    </div>
  )
}

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe("DocumentationDocumentPage", () => {
  it.skip("renders a reviewed installation document with source metadata and local-document links", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/installation/advanced-install"]}>
        <Routes>
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole("heading", { name: "Advanced bootstrap options", level: 1 })).toBeInTheDocument()
    expect(screen.getAllByText("Documentation")).not.toHaveLength(0)
    expect(screen.getByText("Review required")).toBeInTheDocument()
    expect(screen.getByRole("complementary", { name: "Headings in this document" })).toHaveClass(
      "sticky",
      "top-[14rem]",
      "max-h-[calc(100vh-15rem)]",
      "overflow-y-auto",
    )
    expect(screen.getByRole("button", { name: "Download English docs (ZIP)" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Back to top" })).toBeInTheDocument()
    expect(
      screen.getByText(
        "Built-in English documentation for MEM (Message Easy Mode). Source content pack v0.2.0 is available offline.",
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText("Some documentation sections are still being replaced for MEM 0.2.0"),
    ).toBeInTheDocument()
    expect(screen.getAllByText("docs/installation/advanced-install.md")).not.toHaveLength(0)
    expect(screen.getByText("87 documents")).toBeInTheDocument()
    expect(screen.queryByText("CLI (Deutsch)")).not.toBeInTheDocument()
    expect(screen.queryByText("Mit Geräteanmeldung anmelden")).not.toBeInTheDocument()

    for (const link of screen.getAllByRole("link", { name: "Install MEM 0.2.0" })) {
      expect(link).toHaveAttribute("href", "/docs/installation")
    }

    for (const link of screen.getAllByRole("link", { name: "Unsupported advanced paths" })) {
      expect(link).toHaveAttribute("href", "#unsupported-advanced-paths")
    }
  }, 15_000)

  it("keeps Setup-scoped document navigation inside Setup and returns to the originating step", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    const { container } = renderWithProviders(
      <MemoryRouter
        initialEntries={[
          "/setup/docs/installation/advanced-install?returnTo=%2Fsetup%2Freview",
        ]}
      >
        <Routes>
          <Route path="/setup/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole("link", { name: "Back to Setup" })).toHaveAttribute(
      "href",
      "/setup/review",
    )
    for (const link of screen.getAllByRole("link", { name: "Install MEM 0.2.0" })) {
      expect(link).toHaveAttribute(
        "href",
        "/setup/docs/installation?returnTo=%2Fsetup%2Freview",
      )
    }
    expect(screen.getAllByRole("link", { name: "Documentation" })[0]).toHaveAttribute(
      "href",
      "/setup/docs?returnTo=%2Fsetup%2Freview",
    )

    const stickyShell = container.querySelector("div.sticky.top-16")
    expect(stickyShell).not.toBeNull()
    expect(stickyShell).toHaveClass("mt-0", "sm:mt-0")
    expect(stickyShell).not.toHaveClass("-mt-6", "sm:-mt-8")
  }, 15_000)

  it("offers language-scoped previous, next, and related reading links", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    const { container } = renderWithProviders(
      <MemoryRouter initialEntries={["/docs/cli/device-login"]}>
        <Routes>
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    const stickyShell = container.querySelector("div.sticky.top-16")
    expect(stickyShell).not.toBeNull()
    expect(stickyShell).toHaveClass("-mt-6", "sm:-mt-8")

    const continueReading = screen.getByRole("navigation", { name: "Continue reading" })
    expect(
      within(continueReading).getByRole("link", {
        name: "Previous document: Configure profiles and servers",
      }),
    ).toHaveAttribute("href", "/docs/cli/profiles")
    expect(
      within(continueReading).getByRole("link", {
        name: "Next document: Inspect account status and sign out",
      }),
    ).toHaveAttribute("href", "/docs/cli/account-and-logout")

    const related = screen.getByRole("region", { name: "Related documentation" })
    expect(within(related).getAllByRole("link")).toHaveLength(3)
    expect(within(related).queryByText("Mit Geräteanmeldung anmelden")).not.toBeInTheDocument()
  })

  it.skip("uses the canonical route for German content and closes the compact menu after legacy-link navigation", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const scrollTo = vi.spyOn(window, "scrollTo").mockImplementation(() => undefined)

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/cli/device-login"]}>
        <Routes>
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      screen.getByRole("heading", { name: "Mit Geräteanmeldung anmelden", level: 1 }),
    ).toBeInTheDocument()
    expect(screen.getByText("75 Dokumente")).toBeInTheDocument()
    expect(screen.queryByText("Overview")).not.toBeInTheDocument()
    expect(screen.queryByText("Advanced Install")).not.toBeInTheDocument()

    const contentsSummary = screen
      .getAllByText("Inhalt")
      .find((element) => element.closest("summary"))
      ?.closest("summary")
    expect(contentsSummary).not.toBeNull()

    const contentsDetails = contentsSummary!.closest("details")
    expect(contentsDetails).not.toBeNull()
    expect(contentsDetails).not.toHaveAttribute("open")

    fireEvent.click(contentsSummary!)

    expect(contentsDetails).toHaveAttribute("open")

    const compactContents = within(contentsDetails!)
    expect(
      compactContents.getByRole("link", { name: "Mit Geräteanmeldung anmelden", current: "page" }),
    ).toBeInTheDocument()

    fireEvent.click(compactContents.getByRole("link", { name: "Kontostatus prüfen und abmelden" }))

    expect(contentsDetails).not.toHaveAttribute("open")
    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Kontostatus prüfen und abmelden", level: 1 })).toBeInTheDocument()
      expect(scrollTo).toHaveBeenCalledWith({ top: 0, left: 0, behavior: "auto" })
    })

    scrollTo.mockRestore()
  })

  it("keeps the canonical page selected when the UI language changes", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")
    vi.spyOn(window, "scrollTo").mockImplementation(() => undefined)

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/cli/device-login"]}>
        <LanguageSwitchProbe />
        <Routes>
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole("heading", { name: "Sign in with device login", level: 1 })).toBeInTheDocument()
    expect(screen.getByTestId("active-route")).toHaveTextContent("/docs/cli/device-login")

    fireEvent.click(screen.getByRole("button", { name: "Switch language" }))

    await waitFor(() => {
      expect(screen.getByTestId("active-language")).toHaveTextContent("de")
      expect(
        screen.getByRole("heading", { name: "Mit Geräteanmeldung anmelden", level: 1 }),
      ).toBeInTheDocument()
      expect(screen.getByTestId("active-route")).toHaveTextContent("/docs/cli/device-login")
    })

  })

  it("redirects a missing translation to the active-language home with an honest notice", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/architecture"]}>
        <Routes>
          <Route path="/docs" element={<DocumentationHomePage />} />
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Wobei benötigen Sie Hilfe?", level: 1 })).toBeInTheDocument()
    expect(screen.getByText("Diese Seite ist nicht auf Deutsch verfügbar")).toBeInTheDocument()
  })

  it("redirects an unknown document identifier back to the active-language default", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/not-found"]}>
        <Routes>
          <Route path="/docs" element={<DocumentationHomePage />} />
          <Route path="/docs/*" element={<DocumentationDocumentPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole("heading", { name: "What do you need help with?", level: 1 })).toBeInTheDocument()
  })
})
