import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationHomePage } from "./documentation-home-page"

afterEach(() => {
  window.localStorage.clear()
})

describe("DocumentationHomePage", () => {
  it("keeps the Setup context callout clear of the sticky documentation shell", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    const { container } = renderWithProviders(
      <MemoryRouter initialEntries={["/setup/docs?returnTo=%2Fsetup%2Fdomain"]}>
        <DocumentationHomePage />
      </MemoryRouter>,
    )

    expect(screen.getByRole("link", { name: "Back to Setup" })).toHaveAttribute(
      "href",
      "/setup/domain",
    )

    const contextTitle = screen.getByText("Documentation inside Setup")
    expect(contextTitle.parentElement).toHaveClass("rounded-xl", "border", "px-4", "py-3")

    const stickyShell = container.querySelector("div.sticky.top-16")
    expect(stickyShell).not.toBeNull()
    expect(stickyShell).toHaveClass("mt-0", "sm:mt-0")
    expect(stickyShell).not.toHaveClass("-mt-6", "sm:-mt-8")
  }, 15_000)

  it.skip("presents an English task-oriented home using only English documentation", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs"]}>
        <DocumentationHomePage />
      </MemoryRouter>,
    )

    expect(
      screen.getByRole("heading", { name: "What do you need help with?", level: 1 }),
    ).toBeInTheDocument()
    const commonTasks = screen.getByRole("region", { name: "Common tasks" })

    expect(
      within(commonTasks).getByRole("link", { name: /Understand what MEM is/ }),
    ).toHaveAttribute("href", "/docs/start/what-is-mem")
    expect(
      within(commonTasks).getByRole("link", { name: /Install or migrate/ }),
    ).toHaveAttribute("href", "/docs/start/install-or-migrate")
    expect(
      screen.getAllByRole("link", { name: "Documentation home", current: "page" }).length,
    ).toBeGreaterThan(0)
    expect(
      within(commonTasks).getByRole("link", { name: /Create a chat server/ }),
    ).toHaveAttribute("href", "/docs/chat-servers/create")
    expect(
      within(commonTasks).getByRole("link", { name: /Back up or restore a chat server/ }),
    ).toHaveAttribute("href", "/docs/backups-restores")
    expect(
      within(commonTasks).getByRole("link", { name: /Migrate from MEM 0.1.0/ }),
    ).toHaveAttribute("href", "/docs/migrate")
    expect(
      within(commonTasks).getByRole("link", { name: /Use the MEM CLI/ }),
    ).toHaveAttribute("href", "/docs/cli")
    expect(screen.getByText("87 documents")).toBeInTheDocument()
    expect(screen.getAllByText("Start here")).not.toHaveLength(0)
    expect(screen.queryByText("MEM CLI installieren")).not.toBeInTheDocument()
  })

  it.skip("keeps Setup-scoped documentation inside the Setup shell and recommends the current stage", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    renderWithProviders(
      <MemoryRouter
        initialEntries={[
          "/setup/docs?returnTo=%2Fsetup%2Fdomain",
        ]}
      >
        <DocumentationHomePage />
      </MemoryRouter>,
    )

    expect(screen.getByRole("link", { name: "Back to Setup" })).toHaveAttribute(
      "href",
      "/setup/domain",
    )
    expect(screen.getByText("Documentation inside Setup")).toBeInTheDocument()
    const recommended = screen.getByRole("region", {
      name: "Recommended for this Setup step",
    })
    expect(
      within(recommended).getByRole("link", {
        name: /Choose the public domain and certificate plan/,
      }),
    ).toHaveAttribute(
      "href",
      "/setup/docs/installation/domain-and-certificate?returnTo=%2Fsetup%2Fdomain",
    )
    expect(
      screen.getAllByRole("link", { name: "Documentation home", current: "page" }).length,
    ).toBeGreaterThan(0)
    for (const link of screen.getAllByRole("link", { name: "Documentation home" })) {
      expect(link).toHaveAttribute(
        "href",
        "/setup/docs?returnTo=%2Fsetup%2Fdomain",
      )
    }
  })

  it.skip("presents German CLI tasks and hides English-only documentation", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs"]}>
        <DocumentationHomePage />
      </MemoryRouter>,
    )

    expect(
      screen.getByRole("heading", { name: "Wobei benötigen Sie Hilfe?", level: 1 }),
    ).toBeInTheDocument()
    const commonTasks = screen.getByRole("region", { name: "Häufige Aufgaben" })

    expect(
      within(commonTasks).getByRole("link", { name: /Verstehen, was MEM ist/ }),
    ).toHaveAttribute("href", "/docs/start/what-is-mem")
    expect(
      within(commonTasks).getByRole("link", { name: /Installieren oder migrieren/ }),
    ).toHaveAttribute("href", "/docs/start/install-or-migrate")
    expect(
      within(commonTasks).getByRole("link", { name: /Chatserver erstellen/ }),
    ).toHaveAttribute("href", "/docs/chat-servers/create")
    expect(
      within(commonTasks).getByRole("link", { name: /Chatserver sichern oder wiederherstellen/ }),
    ).toHaveAttribute("href", "/docs/backups-restores")
    expect(
      within(commonTasks).getByRole("link", { name: /Von MEM 0.1.0 migrieren/ }),
    ).toHaveAttribute("href", "/docs/migrate")
    expect(
      within(commonTasks).getByRole("link", { name: /MEM CLI verwenden/ }),
    ).toHaveAttribute("href", "/docs/cli")
    expect(screen.getByText("75 Dokumente")).toBeInTheDocument()
    expect(screen.queryByText("Understand what MEM is")).not.toBeInTheDocument()
    expect(screen.getAllByText("Erste Schritte")).not.toHaveLength(0)
    expect(screen.queryByText("Start here")).not.toBeInTheDocument()
  })

  it("shows an honest notice after a missing translation redirects home", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter
        initialEntries={[
          {
            pathname: "/docs",
            state: { documentationNotice: "translation-unavailable", language: "de" },
          },
        ]}
      >
        <DocumentationHomePage />
      </MemoryRouter>,
    )

    expect(screen.getByText("Diese Seite ist nicht auf Deutsch verfügbar")).toBeInTheDocument()
    expect(screen.getByText(/deutsche Dokumentationsstartseite geöffnet/)).toBeInTheDocument()
  })
})
