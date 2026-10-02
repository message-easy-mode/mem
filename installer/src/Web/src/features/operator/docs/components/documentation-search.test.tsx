import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationSearch } from "./documentation-search"

afterEach(() => {
  window.localStorage.clear()
})

describe("DocumentationSearch", () => {
  it.skip("searches only the English local pack and exposes a heading-level reader link", async () => {
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter>
        <DocumentationSearch />
      </MemoryRouter>,
    )

    const searchbox = screen.getByRole("searchbox", { name: "Search documentation" })
    await user.type(searchbox, "when to use this guide")

    const result = await screen.findByRole("link", { name: /Advanced Install/i })
    expect(result).toHaveAttribute("href", "/docs/installation/advanced-install#when-to-use-this-guide")
    expect(screen.getByText("Mentioned in: When to use this guide")).toBeInTheDocument()
    expect(screen.queryByText(/\[!IMPORTANT\]|\*\*pgAdmin|\[Optional pgAdmin\]/)).not.toBeInTheDocument()

    await user.clear(searchbox)
    await user.type(searchbox, "Geräteanmeldung")
    expect(await screen.findByText("No documentation matched “Geräteanmeldung”.")).toBeInTheDocument()
  })

  it.skip("focuses the search input with Ctrl K and filters by the active-language section", async () => {
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter>
        <DocumentationSearch />
      </MemoryRouter>,
    )

    const searchInput = screen.getByRole("searchbox", { name: "Search documentation" })
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "k", ctrlKey: true }))
    expect(searchInput).toHaveFocus()

    await user.click(screen.getByRole("button", { name: "Advanced search" }))
    await user.selectOptions(screen.getByLabelText("Section"), "tools")
    await user.type(searchInput, "optional")

    const results = await screen.findAllByRole("link")
    expect(results.some((result) => result.getAttribute("href") === "/docs/tools/optional-pgadmin")).toBe(true)
    expect(results.some((result) => result.getAttribute("href") === "/docs/tools/optional-portainer")).toBe(true)
    expect(screen.queryByRole("link", { name: "Installation Guide" })).not.toBeInTheDocument()
  })

  it.skip("shows and searches only German documentation in German mode", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter>
        <DocumentationSearch />
      </MemoryRouter>,
    )

    const searchbox = screen.getByRole("searchbox", { name: "Dokumentation durchsuchen" })
    expect(screen.getByRole("button", { name: "Erweiterte Suche" })).toBeInTheDocument()

    await user.type(searchbox, "Geräteanmeldung")
    expect(
      await screen.findByRole("link", { name: /Mit Geräteanmeldung anmelden/i }),
    ).toHaveAttribute("href", "/docs/cli/device-login")

    await user.clear(searchbox)
    await user.type(searchbox, "Advanced Install")
    expect(await screen.findByText("Keine Dokumentation entspricht „Advanced Install“.")).toBeInTheDocument()
  })

  it("keeps an honest empty state and can clear active query and filters", async () => {
    const user = userEvent.setup()

    renderWithProviders(
      <MemoryRouter>
        <DocumentationSearch />
      </MemoryRouter>,
    )

    const searchInput = screen.getByRole("searchbox", { name: "Search documentation" })
    await user.type(searchInput, "unfindable internal appliance")

    expect(await screen.findByText("No documentation matched “unfindable internal appliance”.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Clear search" }))
    expect(searchInput).toHaveValue("")
    expect(screen.queryByText("Search results")).not.toBeInTheDocument()
  })
})
